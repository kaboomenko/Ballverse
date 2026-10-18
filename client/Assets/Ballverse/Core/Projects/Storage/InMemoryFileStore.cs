using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ballverse.Core.Projects
{
    /// <summary>
    /// In-memory <see cref="IFileStore"/> for tests, with failure injection and a crash model (spec §7.4 atomic write, §8 ST6).
    /// Crash model: writes and copies truncate the file durably and keep the new bytes in a volatile cache until
    /// <see cref="Flush"/>; renames, deletes and directory changes are durable immediately (journaled metadata).
    /// <see cref="SimulateCrash"/> drops every unflushed cache, leaving such files empty.
    /// Not thread-safe.
    /// </summary>
    public sealed class InMemoryFileStore : IFileStore
    {
        static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false, true);

        sealed class Entry
        {
            public byte[] Current;
            public byte[] Durable;
            public bool Dirty;

            public Entry Copy()
            {
                return new Entry { Current = (byte[])Current.Clone(), Durable = (byte[])Durable.Clone(), Dirty = Dirty };
            }
        }

        readonly Dictionary<string, Entry> _files = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly HashSet<string> _dirs = new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> _mutationLog = new List<string>();

        int _failAtMutation = -1;
        FailureKind _failAtKind;
        Func<FileOp, string, bool> _failWhen;
        FailureKind _failWhenKind;

        /// <summary>Number of mutating operations attempted so far (writes, flushes, renames, copies, deletes, directory creation).</summary>
        public int MutationCount { get; private set; }

        /// <summary>Human-readable log of attempted mutating operations, in order.</summary>
        public IReadOnlyList<string> MutationLog => _mutationLog;

        /// <summary>Makes the mutating operation with zero-based index <paramref name="mutationIndex"/> (counted by <see cref="MutationCount"/>) fail once.</summary>
        public void FailAtMutation(int mutationIndex, FailureKind kind = FailureKind.Throw)
        {
            if (mutationIndex < 0) throw new ArgumentOutOfRangeException(nameof(mutationIndex));
            _failAtMutation = mutationIndex;
            _failAtKind = kind;
        }

        /// <summary>
        /// Makes every operation (reads included) matching <paramref name="predicate"/> fail until <see cref="ClearFailures"/>.
        /// For two-path operations (replace, move, copy) the predicate gets "source -> destination".
        /// </summary>
        public void FailWhen(Func<FileOp, string, bool> predicate, FailureKind kind = FailureKind.Throw)
        {
            _failWhen = predicate ?? throw new ArgumentNullException(nameof(predicate));
            _failWhenKind = kind;
        }

        public void ClearFailures()
        {
            _failAtMutation = -1;
            _failWhen = null;
        }

        /// <summary>Power loss: every unflushed write is lost (the file keeps its durable, usually empty, content).</summary>
        public void SimulateCrash()
        {
            foreach (var e in _files.Values)
            {
                if (!e.Dirty) continue;
                e.Current = (byte[])e.Durable.Clone();
                e.Dirty = false;
            }
        }

        /// <summary>Deep copy of files and directories; failure settings and logs are not copied.</summary>
        public InMemoryFileStore Clone()
        {
            var c = new InMemoryFileStore();
            foreach (var kv in _files) c._files.Add(kv.Key, kv.Value.Copy());
            foreach (var d in _dirs) c._dirs.Add(d);
            return c;
        }

        /// <summary>All file paths, sorted ordinally (for tests and diagnostics).</summary>
        public IReadOnlyList<string> AllFiles()
        {
            var list = new List<string>(_files.Keys);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        // ---- IFileStore ----

        public bool FileExists(string path)
        {
            Check(FileOp.FileExists, path);
            return _files.ContainsKey(Normalize(path, false));
        }

        public bool DirectoryExists(string path)
        {
            Check(FileOp.DirectoryExists, path);
            string p = Normalize(path, true);
            return p.Length == 0 || _dirs.Contains(p);
        }

        public string ReadAllText(string path)
        {
            Check(FileOp.ReadText, path);
            return s_utf8.GetString(Get(path).Current);
        }

        public byte[] ReadAllBytes(string path)
        {
            Check(FileOp.ReadBytes, path);
            return (byte[])Get(path).Current.Clone();
        }

        public void WriteAllText(string path, string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            WriteCore(FileOp.WriteText, path, s_utf8.GetBytes(text));
        }

        public void WriteAllBytes(string path, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            WriteCore(FileOp.WriteBytes, path, (byte[])data.Clone());
        }

        public void Flush(string path)
        {
            var kind = Mutate(FileOp.Flush, path);
            var e = Get(path);
            ThrowIfFailing(kind, FileOp.Flush, path);
            e.Durable = (byte[])e.Current.Clone();
            e.Dirty = false;
        }

        public void Replace(string source, string destination)
        {
            var kind = Mutate(FileOp.Replace, source + " -> " + destination);
            string s = Normalize(source, false), d = Normalize(destination, false);
            var e = Get(source);
            ThrowIfFailing(kind, FileOp.Replace, source);
            EnsureParents(d);
            _files.Remove(s);
            _files[d] = e;
        }

        public void Move(string source, string destination)
        {
            var kind = Mutate(FileOp.Move, source + " -> " + destination);
            string s = Normalize(source, false), d = Normalize(destination, false);
            var e = Get(source);
            if (_files.ContainsKey(d)) throw new IOException("Destination exists: " + destination);
            ThrowIfFailing(kind, FileOp.Move, source);
            EnsureParents(d);
            _files.Remove(s);
            _files[d] = e;
        }

        public void Copy(string source, string destination)
        {
            var kind = Mutate(FileOp.Copy, source + " -> " + destination);
            var data = (byte[])Get(source).Current.Clone();
            Store(Normalize(destination, false), data, kind, FileOp.Copy, destination);
        }

        public void DeleteFile(string path)
        {
            var kind = Mutate(FileOp.DeleteFile, path);
            string p = Normalize(path, false);
            ThrowIfFailing(kind, FileOp.DeleteFile, path);
            _files.Remove(p);
        }

        public void CreateDirectory(string path)
        {
            var kind = Mutate(FileOp.CreateDirectory, path);
            string p = Normalize(path, false);
            ThrowIfFailing(kind, FileOp.CreateDirectory, path);
            if (_files.ContainsKey(p)) throw new IOException("A file exists at " + path);
            AddDirWithParents(p);
        }

        public void DeleteDirectory(string path)
        {
            var kind = Mutate(FileOp.DeleteDirectory, path);
            string p = Normalize(path, false);
            ThrowIfFailing(kind, FileOp.DeleteDirectory, path);
            string prefix = p + "/";
            var files = new List<string>();
            foreach (var k in _files.Keys)
            {
                if (k.StartsWith(prefix, StringComparison.Ordinal)) files.Add(k);
            }
            foreach (var k in files) _files.Remove(k);
            _dirs.RemoveWhere(d => d == p || d.StartsWith(prefix, StringComparison.Ordinal));
        }

        public IReadOnlyList<string> ListDirectories(string path)
        {
            Check(FileOp.ListDirectories, path);
            string p = Normalize(path, true);
            var result = new List<string>();
            foreach (var d in _dirs)
            {
                string name = ChildName(p, d);
                if (name != null) result.Add(name);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        public IReadOnlyList<string> ListFiles(string path)
        {
            Check(FileOp.ListFiles, path);
            string p = Normalize(path, true);
            var result = new List<string>();
            foreach (var f in _files.Keys)
            {
                string name = ChildName(p, f);
                if (name != null) result.Add(name);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        // ---- internals ----

        void WriteCore(FileOp op, string path, byte[] data)
        {
            var kind = Mutate(op, path);
            Store(Normalize(path, false), data, kind, op, path);
        }

        void Store(string p, byte[] data, FailureKind? kind, FileOp op, string displayPath)
        {
            if (_dirs.Contains(p)) throw new IOException("A directory exists at " + displayPath);
            if (kind == FailureKind.Throw || kind == FailureKind.Crash)
            {
                Fail(kind.Value, op, displayPath);
            }
            EnsureParents(p);
            if (kind == FailureKind.TornWrite)
            {
                var torn = new byte[data.Length / 2];
                Array.Copy(data, torn, torn.Length);
                data = torn;
            }
            // O_TRUNC is durable metadata; the new bytes are volatile until Flush.
            _files[p] = new Entry { Current = data, Durable = new byte[0], Dirty = true };
            if (kind == FailureKind.TornWrite) throw new InjectedFailureException(op, displayPath);
        }

        /// <summary>Counts a mutating op and returns the failure to inject, if any.</summary>
        FailureKind? Mutate(FileOp op, string display)
        {
            int index = MutationCount++;
            _mutationLog.Add(op + " " + display);
            if (index == _failAtMutation)
            {
                _failAtMutation = -1;
                return _failAtKind;
            }
            if (_failWhen != null && _failWhen(op, display)) return _failWhenKind;
            return null;
        }

        void Check(FileOp op, string path)
        {
            if (_failWhen != null && _failWhen(op, path)) Fail(_failWhenKind, op, path);
        }

        void ThrowIfFailing(FailureKind? kind, FileOp op, string path)
        {
            if (kind != null) Fail(kind.Value, op, path);
        }

        void Fail(FailureKind kind, FileOp op, string path)
        {
            if (kind == FailureKind.Crash) SimulateCrash();
            throw new InjectedFailureException(op, path);
        }

        Entry Get(string path)
        {
            if (!_files.TryGetValue(Normalize(path, false), out var e)) throw new FileNotFoundException("File not found: " + path, path);
            return e;
        }

        void EnsureParents(string p)
        {
            int slash = p.LastIndexOf('/');
            if (slash > 0) AddDirWithParents(p.Substring(0, slash));
        }

        void AddDirWithParents(string p)
        {
            while (p.Length > 0 && _dirs.Add(p))
            {
                int slash = p.LastIndexOf('/');
                if (slash < 0) break;
                p = p.Substring(0, slash);
            }
        }

        static string ChildName(string parent, string candidate)
        {
            if (parent.Length == 0)
            {
                return candidate.IndexOf('/') < 0 ? candidate : null;
            }
            if (candidate.Length <= parent.Length + 1 || !candidate.StartsWith(parent, StringComparison.Ordinal) || candidate[parent.Length] != '/') return null;
            string rest = candidate.Substring(parent.Length + 1);
            return rest.IndexOf('/') < 0 ? rest : null;
        }

        /// <summary>Rejects backslashes, absolute paths and '.'/'..' segments so traversal bugs surface in tests.</summary>
        static string Normalize(string path, bool allowRoot)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (path.Length == 0)
            {
                if (allowRoot) return path;
                throw new ArgumentException("Empty path.", nameof(path));
            }
            if (path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0) throw new ArgumentException("Invalid path: " + path, nameof(path));
            foreach (var seg in path.Split('/'))
            {
                if (seg.Length == 0 || seg == "." || seg == "..") throw new ArgumentException("Invalid path: " + path, nameof(path));
            }
            return path;
        }
    }
}
