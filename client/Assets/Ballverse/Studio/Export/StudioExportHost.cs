using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core;
using Ballverse.Core.Studio;
using Ballverse.Media;
using Ballverse.Platform;

namespace Ballverse.Studio
{
    /// <summary>
    /// <see cref="IExportHost"/> of the app: frame pacing from <see cref="StudioRunner"/>, thermal state and free space from the
    /// media bridge's <see cref="DeviceSignals"/> (polled at most twice a second), pause/resume from <see cref="IAppLifecycle"/>,
    /// screen-awake tokens from <see cref="IScreenAwake"/> (spec §3.8 "Экран не гаснет"). Main thread.
    /// </summary>
    public sealed class StudioExportHost : IExportHost, IDisposable
    {
        readonly StudioRunner _runner;
        readonly IMediaBridge _media;
        readonly IAppLifecycle _lifecycle;
        readonly IScreenAwake _screen;
        readonly IStudioClock _clock;
        DeviceSignals _signals = DeviceSignals.Unknown;
        long _signalsAtMs = long.MinValue;
        int _pauseCount;

        public StudioExportHost(StudioRunner runner, IMediaBridge media, IAppLifecycle lifecycle, IScreenAwake screen, DeviceTier tier, IStudioClock clock = null)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _media = media ?? throw new ArgumentNullException(nameof(media));
            _lifecycle = lifecycle;
            _screen = screen;
            _clock = clock ?? StudioClock.Instance;
            Tier = tier;
            if (_lifecycle != null) _lifecycle.Paused += OnPaused;
        }

        void OnPaused()
        {
            Interlocked.Increment(ref _pauseCount);
        }

        public Task NextFrame() => _runner.NextFrame();

        public long NowMs => _clock.NowMs();

        public string NowIso => _clock.NowIso();

        public DeviceTier Tier { get; set; }

        public ExportThermalLevel Thermal
        {
            get
            {
                switch (Signals.Thermal)
                {
                    case ThermalState.Nominal: return ExportThermalLevel.Nominal;
                    case ThermalState.Fair: return ExportThermalLevel.Fair;
                    case ThermalState.Serious: return ExportThermalLevel.Serious;
                    case ThermalState.Critical: return ExportThermalLevel.Critical;
                    default: return ExportThermalLevel.Unknown;
                }
            }
        }

        public long FreeSpaceBytes => Signals.FreeSpaceBytes;

        public bool IsPaused => _lifecycle != null && _lifecycle.IsPaused;

        public int PauseCount => Volatile.Read(ref _pauseCount);

        public IDisposable KeepAwake(string reason)
        {
            return _screen != null ? _screen.Acquire(reason) : NoopDisposable.Instance;
        }

        DeviceSignals Signals
        {
            get
            {
                long now = _clock.NowMs();
                if (now - _signalsAtMs >= 500)
                {
                    try
                    {
                        _signals = _media.Signals ?? DeviceSignals.Unknown;
                    }
                    catch (Exception e) when (e is MediaException || e is IOException || e is InvalidOperationException)
                    {
                        _signals = DeviceSignals.Unknown;
                    }
                    _signalsAtMs = now;
                }
                return _signals;
            }
        }

        public void Dispose()
        {
            if (_lifecycle != null) _lifecycle.Paused -= OnPaused;
        }

        sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new NoopDisposable();

            public void Dispose()
            {
            }
        }
    }
}
