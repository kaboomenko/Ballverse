using System;
using System.Collections.Generic;

namespace Ballverse.App
{
    public enum AppHostState
    {
        Created = 0,
        /// <summary>Registered all modules and started the <see cref="AppStartPhase.Immediate"/> ones.</summary>
        Booted = 1,
        /// <summary>Also started the <see cref="AppStartPhase.AfterFirstFrame"/> modules.</summary>
        Running = 2,
        ShutDown = 3,
    }

    /// <summary>A module call that threw. The module is skipped from then on; the app keeps running.</summary>
    public sealed class AppModuleFailure
    {
        public AppModuleFailure(string moduleName, string stage, Exception exception)
        {
            ModuleName = moduleName;
            Stage = stage;
            Exception = exception;
        }

        public string ModuleName { get; }
        /// <summary>"Register", "Start" or "Shutdown".</summary>
        public string Stage { get; }
        public Exception Exception { get; }
    }

    /// <summary>
    /// Engine-free composition root: runs the module lifecycle described on <see cref="IAppModule"/>.
    /// Driven by <see cref="Bootstrap"/> on device and directly by EditMode tests.
    /// </summary>
    public sealed class AppHost
    {
        readonly IAppModule[] _modules;
        readonly bool[] _registered;
        readonly List<IAppModule> _started = new List<IAppModule>();
        readonly List<AppModuleFailure> _failures = new List<AppModuleFailure>();
        readonly Action<string, Exception> _reportError;
        readonly AppModuleContext _context;

        public AppHost(IReadOnlyList<IAppModule> modules, Action<string, Exception> reportError)
        {
            if (modules == null) throw new ArgumentNullException(nameof(modules));
            _reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
            _modules = new IAppModule[modules.Count];
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < modules.Count; i++)
            {
                IAppModule module = modules[i] ?? throw new ArgumentException("Module list contains null at index " + i, nameof(modules));
                if (string.IsNullOrEmpty(module.Name)) throw new ArgumentException("Module at index " + i + " has no name", nameof(modules));
                if (!names.Add(module.Name)) throw new ArgumentException("Duplicate module name: " + module.Name, nameof(modules));
                _modules[i] = module;
            }
            _registered = new bool[_modules.Length];
            Services = new AppServices();
            _context = new AppModuleContext(Services, _reportError);
        }

        public AppServices Services { get; }

        public AppHostState State { get; private set; } = AppHostState.Created;

        public IReadOnlyList<IAppModule> Modules => _modules;

        /// <summary>Modules whose Start completed, in start order.</summary>
        public IReadOnlyList<IAppModule> StartedModules => _started;

        public IReadOnlyList<AppModuleFailure> Failures => _failures;

        /// <summary>Registers every module, seals the registry and starts the <see cref="AppStartPhase.Immediate"/> modules.</summary>
        public void Boot()
        {
            if (State != AppHostState.Created) throw new InvalidOperationException("Boot can run once; state is " + State);
            for (int i = 0; i < _modules.Length; i++)
            {
                _registered[i] = Invoke(_modules[i], "Register", m => m.Register(Services));
            }
            Services.Seal();
            StartPhase(AppStartPhase.Immediate);
            State = AppHostState.Booted;
        }

        /// <summary>Starts the <see cref="AppStartPhase.AfterFirstFrame"/> modules. Call once the first frame is on screen.</summary>
        public void StartDeferred()
        {
            if (State != AppHostState.Booted) throw new InvalidOperationException("StartDeferred requires state Booted; state is " + State);
            StartPhase(AppStartPhase.AfterFirstFrame);
            State = AppHostState.Running;
        }

        /// <summary>Shuts started modules down in reverse start order. Safe to call more than once.</summary>
        public void Shutdown()
        {
            if (State == AppHostState.ShutDown) return;
            for (int i = _started.Count - 1; i >= 0; i--)
            {
                Invoke(_started[i], "Shutdown", m => m.Shutdown());
            }
            _started.Clear();
            State = AppHostState.ShutDown;
        }

        void StartPhase(AppStartPhase phase)
        {
            for (int i = 0; i < _modules.Length; i++)
            {
                IAppModule module = _modules[i];
                if (!_registered[i] || module.Phase != phase) continue;
                if (Invoke(module, "Start", m => m.Start(_context))) _started.Add(module);
            }
        }

        bool Invoke(IAppModule module, string stage, Action<IAppModule> call)
        {
            try
            {
                call(module);
                return true;
            }
            catch (Exception e)
            {
                _failures.Add(new AppModuleFailure(module.Name, stage, e));
                _reportError("Module " + module.Name + " failed in " + stage, e);
                return false;
            }
        }
    }
}
