using System.Collections.Generic;

namespace Ballverse.Core.Projects
{
    /// <summary>
    /// File-system abstraction for project storage (spec §7.4 "Запись атомарная", §3.9). Paths are relative to the store root,
    /// use '/' separators and are produced by <see cref="ProjectPaths"/>. Implementations throw <see cref="System.IO.IOException"/>
    /// (or a subclass) on failure. Text is UTF-8 without BOM. The platform implementation lives outside Core.
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
        /// Atomically renames <paramref name="source"/> to <paramref name="destination"/>, replacing an existing destination
        /// (rename(2) / File.Replace semantics). After a crash either the old or the new destination exists, never a mix.
        /// The rename itself must be durable on return.
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

        /// <summary>Names (not paths) of the immediate child directories, sorted ordinally. Empty when missing.</summary>
        IReadOnlyList<string> ListDirectories(string path);

        /// <summary>Names (not paths) of the immediate child files, sorted ordinally. Empty when missing.</summary>
        IReadOnlyList<string> ListFiles(string path);
    }
}
