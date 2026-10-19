using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Ballverse.PlayModeTests
{
    /// <summary>
    /// An error-class log message the smoke test accepts. Only messages that are provably expected belong here, each with the
    /// source that logs it and the exact condition under which it appears.
    /// </summary>
    public sealed class ExpectedLog
    {
        public ExpectedLog(LogType type, string pattern, bool onlyWithoutGpu, string reason)
        {
            Type = type;
            Pattern = new Regex(pattern, RegexOptions.CultureInvariant);
            OnlyWithoutGpu = onlyWithoutGpu;
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }

        public LogType Type { get; }
        public Regex Pattern { get; }
        /// <summary>Accepted only when the editor runs with <c>GraphicsDeviceType.Null</c> (-nographics).</summary>
        public bool OnlyWithoutGpu { get; }
        public string Reason { get; }

        public bool Matches(LogType type, string message, bool hasGpu) =>
            type == Type && (!OnlyWithoutGpu || !hasGpu) && Pattern.IsMatch(message ?? string.Empty);
    }

    /// <summary>The whitelist. With a graphics device (GameCI: Xvfb + Mesa, see README.md) it is empty: every error fails.</summary>
    public static class ExpectedLogs
    {
        public static readonly IReadOnlyList<ExpectedLog> All = new[]
        {
            // Render/Runtime/RenderResources.cs CreateMaterial / CreateInstancedMaterial log this for every renderer shader for
            // which Shader.isSupported is false; the product then skips that layer (its documented degraded path). Without a
            // graphics device no shader can run, so the renderer reports each layer once.
            new ExpectedLog(LogType.Error,
                @"^\[Ballverse\.Render\] Shader '[^'\r\n]+' is (missing or )?unsupported on this device; that layer is skipped\.$",
                true, "renderer shader unsupported without a graphics device (RenderResources.CreateMaterial)"),
            // Render/Runtime/BallverseRenderer.cs RenderCore: the same condition, reported once per renderer instance.
            new ExpectedLog(LogType.Error,
                @"^\[Ballverse\.Render\] Some renderer shaders failed to load; missing layers are skipped\.$",
                true, "renderer incomplete without a graphics device (BallverseRenderer.RenderCore)"),
        };

        public static ExpectedLog Find(LogType type, string message, bool hasGpu)
        {
            foreach (ExpectedLog e in All)
            {
                if (e.Matches(type, message, hasGpu)) return e;
            }
            return null;
        }
    }

    /// <summary>
    /// Watches every log message of the running app (all threads) and attributes error-class messages (Error, Exception,
    /// Assert) to the smoke step that was running. Unexpected ones fail the test at the next check with the step name, the
    /// message and its stack trace. Without a graphics device, errors inside GPU-dependent steps are kept as soft findings
    /// (reported, not failed); with one, every unexpected error fails.
    /// </summary>
    public sealed class SmokeLog : IDisposable
    {
        public sealed class Entry
        {
            public Entry(LogType type, string message, string stackTrace, string step)
            {
                Type = type;
                Message = message ?? string.Empty;
                StackTrace = stackTrace ?? string.Empty;
                Step = step;
            }

            public LogType Type { get; }
            public string Message { get; }
            public string StackTrace { get; }
            public string Step { get; }

            public override string ToString() => "[" + Type + "] during '" + Step + "': " + Message + (StackTrace.Length > 0 ? "\n" + StackTrace : string.Empty);
        }

        readonly object _gate = new object();
        readonly bool _hasGpu;
        readonly List<Entry> _unexpected = new List<Entry>();
        readonly List<Entry> _soft = new List<Entry>();
        readonly List<Entry> _whitelisted = new List<Entry>();
        int _reported;
        string _step = "(before the first step)";
        bool _gpuDependent;
        bool _disposed;

        public SmokeLog(bool hasGpu)
        {
            _hasGpu = hasGpu;
            Application.logMessageReceivedThreaded += OnLog;
        }

        public string CurrentStep
        {
            get { lock (_gate) return _step; }
        }

        public IReadOnlyList<Entry> Soft
        {
            get { lock (_gate) return _soft.ToArray(); }
        }

        public IReadOnlyList<Entry> Whitelisted
        {
            get { lock (_gate) return _whitelisted.ToArray(); }
        }

        /// <summary>Messages from now on belong to <paramref name="step"/>.</summary>
        public void Enter(string step, bool gpuDependent)
        {
            lock (_gate)
            {
                _step = step;
                _gpuDependent = gpuDependent;
            }
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            lock (_gate)
            {
                var entry = new Entry(type, condition, stackTrace, _step);
                if (ExpectedLogs.Find(type, condition, _hasGpu) != null) _whitelisted.Add(entry);
                else if (!_hasGpu && _gpuDependent) _soft.Add(entry);
                else _unexpected.Add(entry);
            }
        }

        /// <summary>Fails the test when an unexpected error-class message arrived since the last call.</summary>
        public void ThrowIfUnexpected()
        {
            string failure = null;
            lock (_gate)
            {
                if (_unexpected.Count > _reported)
                {
                    var sb = new StringBuilder();
                    sb.Append(_unexpected.Count - _reported).Append(" unexpected error log message(s):");
                    for (int i = _reported; i < _unexpected.Count; i++) sb.Append("\n").Append(_unexpected[i]);
                    _reported = _unexpected.Count;
                    failure = sb.ToString();
                }
            }
            if (failure != null) Assert.Fail(failure);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Application.logMessageReceivedThreaded -= OnLog;
        }
    }
}
