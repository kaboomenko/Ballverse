using System;
using System.Collections.Generic;

namespace Ballverse.Services
{
    /// <summary>Logging sink of the services. Messages never contain personal data or user file paths (spec §7.12, §9.2).</summary>
    public interface IServiceLog
    {
        void Info(string tag, string message);
        void Warning(string tag, string message);
        void Error(string tag, string message, Exception exception = null);
    }

    /// <summary>Discards everything.</summary>
    public sealed class NullServiceLog : IServiceLog
    {
        public static readonly NullServiceLog Instance = new NullServiceLog();

        public void Info(string tag, string message) { }
        public void Warning(string tag, string message) { }
        public void Error(string tag, string message, Exception exception = null) { }
    }

    /// <summary>Keeps the lines in memory (tests).</summary>
    public sealed class MemoryServiceLog : IServiceLog
    {
        readonly List<string> _lines = new List<string>();

        public IReadOnlyList<string> Lines => _lines;

        public int ErrorCount { get; private set; }

        public int WarningCount { get; private set; }

        public void Info(string tag, string message)
        {
            _lines.Add("I [" + tag + "] " + message);
        }

        public void Warning(string tag, string message)
        {
            WarningCount++;
            _lines.Add("W [" + tag + "] " + message);
        }

        public void Error(string tag, string message, Exception exception = null)
        {
            ErrorCount++;
            _lines.Add("E [" + tag + "] " + message + (exception != null ? " :: " + exception.GetType().Name + ": " + exception.Message : string.Empty));
        }
    }
}
