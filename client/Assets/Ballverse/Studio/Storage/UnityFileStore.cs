using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ballverse.Core.Projects;

namespace Ballverse.Studio
{
    /// <summary>
    /// <see cref="IFileStore"/> on the device file system under one root folder (spec §3.9, §7.4 "Запись атомарная: временный
    /// файл → fsync → переименование"). Roots used by the studio: <c>Application.persistentDataPath</c> (projects, clip library),
    /// <c>{temporaryCachePath}/render-streams</c> (recent runs) and <c>{temporaryCachePath}/export</c> (export jobs); see
    /// <see cref="StudioStorage"/>.
    /// <para>Durability: <see cref="Flush"/> is <c>FileStream.Flush(true)</c> (fsync). <see cref="Replace"/> renames atomically
    /// (<c>File.Replace</c> onto an existing destination, <c>File.Move</c> onto a missing one — POSIX rename semantics, as the
    /// <see cref="IFileStore.Replace"/> contract requires) and then fsyncs the parent folder where the platform allows it
    /// (<see cref="DirectorySync"/>: Android; iOS/APFS commits renames through its metadata journal).</para>
    /// <para>Paths are '/'-separated and relative to the root; rooted paths and ".." segments are refused (the Core callers only
    /// pass <see cref="ProjectPaths"/>-checked paths, this is a second line of defence). Text is UTF-8 without BOM and invalid
    /// UTF-8 throws on read (the repository then treats the file as corrupt). Thread-safe in the sense of the file system: calls
    /// may come from a worker thread (autosave), but two writers of the same file must not overlap.</para>
    /// </summary>
    public sealed class UnityFileStore : IFileStore
    {
        static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false, true);

        /// <param name="root">Absolute root folder; created if missing.</param>
        public UnityFileStore(string root)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentException("root required", nameof(root));
            Root = Path.GetFullPath(root);
            Directory.CreateDirectory(Root);
        }

        /// <summary>Absolute root folder.</summary>
        public string Root { get; }

        /// <summary>Absolute path of a store path (for native APIs such as the media bridge). "" is the root.</summary>
        public string FullPath(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (path.Length == 0) return Root;
            if (Path.IsPathRooted(path) || path.IndexOf('\\') >= 0) throw new ArgumentException("Store paths are relative and '/'-separated: " + path, nameof(path));
            foreach (string segment in path.Split('/'))
            {
                if (segment == "..") throw new ArgumentException("Store paths must not leave the root: " + path, nameof(path));
            }
            return Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
        }

        public bool FileExists(string path) => File.Exists(FullPath(path));

        public bool DirectoryExists(string path) => Directory.Exists(FullPath(path));

        public string ReadAllText(string path) => s_utf8.GetString(ReadAllBytes(path));

        public byte[] ReadAllBytes(string path)
        {
            string full = FullPath(path);
            if (!File.Exists(full)) throw new FileNotFoundException("File not found: " + path, path);
            return File.ReadAllBytes(full);
        }

        public void WriteAllText(string path, string text) => WriteAllBytes(path, s_utf8.GetBytes(text ?? string.Empty));

        public void WriteAllBytes(string path, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            string full = FullPath(path);
            EnsureParent(full);
            using (var fs = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024))
            {
                fs.Write(data, 0, data.Length);
            }
        }

        public void Flush(string path)
        {
            string full = FullPath(path);
            if (!File.Exists(full)) throw new FileNotFoundException("File not found: " + path, path);
            FsyncFile(full);
        }

        public void Replace(string source, string destination)
        {
            string src = FullPath(source), dst = FullPath(destination);
            if (!File.Exists(src)) throw new FileNotFoundException("File not found: " + source, source);
            EnsureParent(dst);
            AtomicReplace(src, dst);
            DirectorySync.TrySync(Path.GetDirectoryName(dst));
        }

        public void Move(string source, string destination)
        {
            string src = FullPath(source), dst = FullPath(destination);
            if (!File.Exists(src)) throw new FileNotFoundException("File not found: " + source, source);
            if (File.Exists(dst) || Directory.Exists(dst)) throw new IOException("Destination exists: " + destination);
            EnsureParent(dst);
            File.Move(src, dst);
            DirectorySync.TrySync(Path.GetDirectoryName(dst));
        }

        public void Copy(string source, string destination)
        {
            string src = FullPath(source), dst = FullPath(destination);
            if (!File.Exists(src)) throw new FileNotFoundException("File not found: " + source, source);
            EnsureParent(dst);
            File.Copy(src, dst, true);
        }

        public void DeleteFile(string path)
        {
            string full = FullPath(path);
            if (File.Exists(full)) File.Delete(full);
        }

        public void CreateDirectory(string path) => Directory.CreateDirectory(FullPath(path));

        public void DeleteDirectory(string path)
        {
            string full = FullPath(path);
            if (path.Length == 0) throw new ArgumentException("Refusing to delete the store root", nameof(path));
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }

        public long GetFileSize(string path)
        {
            var info = new FileInfo(FullPath(path));
            if (!info.Exists) throw new FileNotFoundException("File not found: " + path, path);
            return info.Length;
        }

        public IReadOnlyList<string> ListDirectories(string path) => Names(FullPath(path), true);

        public IReadOnlyList<string> ListFiles(string path) => Names(FullPath(path), false);

        /// <summary>Total bytes of all files under a folder (storage screen, spec §3.9); 0 when missing.</summary>
        public long DirectorySize(string path)
        {
            string full = FullPath(path);
            if (!Directory.Exists(full)) return 0;
            long total = 0;
            foreach (string file in Directory.GetFiles(full, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    // vanished meanwhile
                }
            }
            return total;
        }

        /// <summary>
        /// Deletes files ending in <paramref name="suffix"/> anywhere under <paramref name="path"/> (startup cleanup of interrupted
        /// atomic writes, spec §3.9). Returns the number deleted; files that cannot be deleted are skipped.
        /// </summary>
        public int DeleteFilesWithSuffix(string path, string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) throw new ArgumentException("suffix required", nameof(suffix));
            string full = FullPath(path);
            if (!Directory.Exists(full)) return 0;
            int deleted = 0;
            foreach (string file in Directory.GetFiles(full, "*" + suffix, SearchOption.AllDirectories))
            {
                if (!file.EndsWith(suffix, StringComparison.Ordinal)) continue;
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch (IOException)
                {
                    // in use or gone: retried at the next start
                }
                catch (UnauthorizedAccessException)
                {
                    // read-only: leave it
                }
            }
            return deleted;
        }

        /// <summary>fsync of a file by absolute path.</summary>
        public static void FsyncFile(string fullPath)
        {
            using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                fs.Flush(true);
            }
        }

        /// <summary>
        /// rename(2) semantics on netstandard2.1: <c>File.Replace</c> when the destination exists (atomic), else <c>File.Move</c>.
        /// A runtime without <c>File.Replace</c> falls back to delete + move (not atomic; never seen on Mono/IL2CPP, kept as a
        /// last resort so a save still completes).
        /// </summary>
        static void AtomicReplace(string src, string dst)
        {
            if (!File.Exists(dst))
            {
                File.Move(src, dst);
                return;
            }
            try
            {
                File.Replace(src, dst, null);
            }
            catch (PlatformNotSupportedException)
            {
                File.Delete(dst);
                File.Move(src, dst);
            }
            catch (NotImplementedException)
            {
                File.Delete(dst);
                File.Move(src, dst);
            }
        }

        static void EnsureParent(string full)
        {
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        static IReadOnlyList<string> Names(string dir, bool directories)
        {
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            string[] paths = directories ? Directory.GetDirectories(dir) : Directory.GetFiles(dir);
            var names = new List<string>(paths.Length);
            foreach (string p in paths) names.Add(Path.GetFileName(p));
            names.Sort(StringComparer.Ordinal);
            return names;
        }
    }
}
