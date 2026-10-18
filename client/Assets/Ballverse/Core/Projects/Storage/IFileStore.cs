using System.Collections.Generic;

namespace Ballverse.Core.Projects
{
    /// <summary>
    /// File-system abstraction for project storage (spec §7.4 "Запись атомарная", §3.9). Paths are relative to the store root,
    /// use '/' separators and are produced by <see cref="ProjectPaths"/>. Implementations throw <see cref="System.IO.IOException"/>
    /// (or a subclass) on I/O failure. Text is UTF-8 without BOM. The platform implementation lives outside Core and must pass
    /// the same contract tests as <see cref="InMemoryFileStore"/> (core-tests: <c>FileStoreContractTests</c>).
    /// </summary>
    public interface IFileStore
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);

        string ReadAllText(string path);
        byte[] ReadAllBytes(string path);

        /// <summary>Creates or truncates the file; parent directories are created. Data may stay in caches until <see cref="Flush"/>.</summary>
        void WriteAllText(string path, string text);
        /// <summary>Creates or truncates the file; parent directories are created. Data may stay in caches until <see cref="Flush"/>.</summary>
        void WriteAllBytes(string path, byte[] data);

        /// <summary>Makes the file's content durable (fsync).</summary>
        void Flush(string path);

        /// <summary>
        /// Atomically renames <paramref name="source"/> to <paramref name="destination"/>. The destination may or may not exist:
        /// <see cref="ProjectRepository.Save"/> calls this onto a missing <c>project.json</c> on the first save and onto a missing
        /// <c>project.json.bak</c> on the first backup rotation (POSIX rename(2) semantics). After a crash either the old or the
        /// new destination exists, never a mix; the rename must be durable on return.
        /// <para>Note for the .NET implementation: <c>System.IO.File.Replace</c> throws when the destination is missing and
        /// <c>File.Move(src, dst, overwrite)</c> does not exist in netstandard2.1. Use
        /// <c>if (File.Exists(dst)) File.Replace(src, dst, null); else File.Move(src, dst);</c>, then fsync the parent folder
        /// on POSIX so the rename is durable.</para>
        /// </summary>
        void Replace(string source, string destination);

        /// <summary>Atomically renames a file; throws if <paramref name="destination"/> already exists.</summary>
        void Move(string source, string destination);

        /// <summary>Copies a file, replacing the destination (not atomic; the copy is not flushed).</summary>
        void Copy(string source, string destination);

        /// <summary>Deletes a file; no-op when missing.</summary>
        void DeleteFile(string path);

        void CreateDirectory(string path);

        /// <summary>Deletes a directory recursively; no-op when missing.</summary>
        void DeleteDirectory(string path);

        /// <summary>Size of the file in bytes (spec §3.9 "Хранилище"); throws <see cref="System.IO.FileNotFoundException"/> when missing.</summary>
        long GetFileSize(string path);

        /// <summary>Names (not paths) of the immediate child directories, sorted ordinally. Empty when missing.</summary>
        IReadOnlyList<string> ListDirectories(string path);

        /// <summary>Names (not paths) of the immediate child files, sorted ordinally. Empty when missing.</summary>
        IReadOnlyList<string> ListFiles(string path);
    }
}
