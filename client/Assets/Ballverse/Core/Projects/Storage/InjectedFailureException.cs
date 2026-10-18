using System.IO;

namespace Ballverse.Core.Projects
{
    /// <summary>Thrown by <see cref="InMemoryFileStore"/> for an injected failure.</summary>
    public sealed class InjectedFailureException : IOException
    {
        public FileOp Operation { get; }
        public string Path { get; }

        public InjectedFailureException(FileOp operation, string path)
            : base("Injected failure in " + operation + " on '" + path + "'.")
        {
            Operation = operation;
            Path = path;
        }
    }
}
