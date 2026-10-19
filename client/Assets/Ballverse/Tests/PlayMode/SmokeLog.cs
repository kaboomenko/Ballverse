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
            // graphics device no shader can run, so the renderer reports each layer once. CreateMaterial uses the same text
            // ("missing or unsupported") for a shader that is not in the project at all, which is a packaging defect and not a
            // GPU artefact: AppBootSmokeTest therefore asserts first, with or without a device, that every renderer shader
            // resolves (RendererShaders below), so only the "unsupported" half of this message can actually be whitelisted.
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

        /// <summary>
        /// The renderer's shaders as RenderResources loads them: (Resources name under <c>RenderResources.ShaderFolder</c>,
        /// <c>Shader.Find</c> name). Copied from Render/Runtime/RenderResources.cs (its constructor); CompileCheck~/run.sh checks
        /// every pair against that file. A null result for both lookups is the "missing" half of the whitelisted message above.
        /// </summary>
        public static readonly IReadOnlyList<KeyValuePair<string, string>> RendererShaders = new[]
        {
            new KeyValuePair<string, string>("BallverseBatch", "Hidden/Ballverse/Batch"),
            new KeyValuePair<string, string>("BallverseSdfBall", "Hidden/Ballverse/SdfBall"),
            new KeyValuePair<string, string>("BallverseParticle", "Hidden/Ballverse/Particle"),
            new KeyValuePair<string, string>("BallverseText", "Hidden/Ballverse/Text"),
            new KeyValuePair<string, string>("BallverseBackground", "Hidden/Ballverse/Background"),
            new KeyValuePair<string, string>("BallversePost", "Hidden/Ballverse/Post"),
        };
    }

    /// <summary>
    /// Recognises error-class messages that come from GPU-side code (the renderer, the live view and gallery previews, the
    /// studio compositor / preview / export frame renderer / covers, the glass backdrop, Unity's graphics API), by the
    /// message text and its stack trace. Without a graphics device these are soft findings in whatever step they arrive,
    /// because the live view renders every frame and covers / previews render from per-frame ticks, not only inside the
    /// steps flagged GPU-dependent.
    /// </summary>
    public static class GpuSources
    {
        static readonly Regex[] s_patterns =
        {
            // The renderer's own log tag (Render/Runtime/*.cs).
            new Regex(@"\[Ballverse\.Render\]", RegexOptions.CultureInvariant),
            // Any frame in the renderer assembly.
            new Regex(@"\bBallverse\.Render\.", RegexOptions.CultureInvariant),
            // Sim/Unity: the live view and the gallery previews (both draw through BallverseRenderer every frame).
            new Regex(@"\bBallverse\.Sim\.(LiveSimView|GalleryPreviewRenderer)\b", RegexOptions.CultureInvariant),
            // Studio GPU code: compositor, frame / text renderers, emoji atlas, export frame renderer, preview player, timeline
            // thumbnails, and the project cover (StudioServices.RenderCover). Studio storage / projects / clips are logic.
            new Regex(@"\bBallverse\.Studio\.(StudioCompositor|StudioFrameRenderer|StudioTextRenderer|StudioEmojiAtlas|VideoTextureUploader|GpuExportFrameRenderer|StudioPreviewPlayer|TimelineThumbnails)\b",
                RegexOptions.CultureInvariant),
            new Regex(@"\bBallverse\.Studio\.StudioServices[:.]RenderCover\b", RegexOptions.CultureInvariant),
            // UI glass backdrop (blurred render texture behind the glass panels).
            new Regex(@"\bBallverse\.UI\.Glass\.", RegexOptions.CultureInvariant),
            // Unity's graphics API.
            new Regex(@"\bUnityEngine\.(RenderTexture|Graphics|Rendering\.|Texture2D[:.]ReadPixels|Material|Shader)\b", RegexOptions.CultureInvariant),
        };

        /// <summary>True when <paramref name="message"/> or <paramref name="stackTrace"/> points at GPU-side code.</summary>
        public static bool IsGpuSide(string message, string stackTrace)
        {
            foreach (Regex r in s_patterns)
            {
                if ((message != null && r.IsMatch(message)) || (stackTrace != null && r.IsMatch(stackTrace))) return true;
            }
            return false;
        }
    }

    /// <summary>How <see cref="SmokeLog"/> treats one log message.</summary>
    public enum SmokeLogClass
    {
        /// <summary>Not error-class (Log / Warning).</summary>
        Ignored,
        /// <summary>Matches <see cref="ExpectedLogs"/>.</summary>
        Whitelisted,
        /// <summary>No graphics device, and the message comes from GPU-side code or arrived during a GPU-dependent step.</summary>
        Soft,
        /// <summary>Fails the test.</summary>
        Unexpected,
    }

    /// <summary>
    /// Watches every log message of the running app (all threads) and attributes error-class messages (Error, Exception,
    /// Assert) to the smoke step that was running. Unexpected ones fail the test at the next check with the step name, the
    /// message and its stack trace. With a graphics device every error that is not whitelisted fails. Without one (local runs
    /// only: CI passes -ballverseSmokeRequireGpu), see <see cref="Classify"/>.
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

        /// <summary>
        /// The rule for one message. Error / Exception / Assert only. Whitelisted first. Without a graphics device a message is
        /// soft when it comes from GPU-side code (<see cref="GpuSources"/>, any step) or arrives while a step flagged
        /// GPU-dependent is running (its time window: a logic error raised during such a step is soft too, which is why CI
        /// requires a device). Everything else is unexpected.
        /// </summary>
        public static SmokeLogClass Classify(LogType type, string message, string stackTrace, bool hasGpu, bool inGpuDependentStep)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return SmokeLogClass.Ignored;
            if (ExpectedLogs.Find(type, message, hasGpu) != null) return SmokeLogClass.Whitelisted;
            if (!hasGpu && (inGpuDependentStep || GpuSources.IsGpuSide(message, stackTrace))) return SmokeLogClass.Soft;
            return SmokeLogClass.Unexpected;
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            lock (_gate)
            {
                var entry = new Entry(type, condition, stackTrace, _step);
                switch (Classify(type, condition, stackTrace, _hasGpu, _gpuDependent))
                {
                    case SmokeLogClass.Whitelisted: _whitelisted.Add(entry); break;
                    case SmokeLogClass.Soft: _soft.Add(entry); break;
                    case SmokeLogClass.Unexpected: _unexpected.Add(entry); break;
                }
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
