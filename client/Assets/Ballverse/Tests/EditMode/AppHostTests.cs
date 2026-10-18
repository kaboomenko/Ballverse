using System;
using System.Collections.Generic;
using Ballverse.App;
using Ballverse.Platform;
using NUnit.Framework;

namespace Ballverse.Tests
{
    public sealed class AppHostTests
    {
        interface IGreeting
        {
            string Text { get; }
        }

        sealed class Greeting : IGreeting
        {
            public string Text => "hi";
        }

        sealed class FakeModule : IAppModule
        {
            readonly List<string> _log;
            public FakeModule(string name, AppStartPhase phase, List<string> log)
            {
                Name = name;
                Phase = phase;
                _log = log;
            }

            public string Name { get; }
            public AppStartPhase Phase { get; }
            public bool ThrowOnRegister;
            public bool ThrowOnStart;
            public Action<AppServices> OnRegister;
            public Action<AppModuleContext> OnStart;

            public void Register(AppServices services)
            {
                _log.Add(Name + ".Register");
                if (ThrowOnRegister) throw new InvalidOperationException("register boom");
                OnRegister?.Invoke(services);
            }

            public void Start(AppModuleContext context)
            {
                _log.Add(Name + ".Start");
                if (ThrowOnStart) throw new InvalidOperationException("start boom");
                OnStart?.Invoke(context);
            }

            public void Shutdown() => _log.Add(Name + ".Shutdown");
        }

        readonly List<string> _errors = new List<string>();

        void Report(string context, Exception e) => _errors.Add(context);

        [SetUp]
        public void SetUp() => _errors.Clear();

        [Test]
        public void BootRegistersAllThenStartsImmediateModulesInOrder()
        {
            var log = new List<string>();
            var a = new FakeModule("A", AppStartPhase.Immediate, log);
            var b = new FakeModule("B", AppStartPhase.AfterFirstFrame, log);
            var c = new FakeModule("C", AppStartPhase.Immediate, log);
            var host = new AppHost(new IAppModule[] { a, b, c }, Report);

            host.Boot();
            CollectionAssert.AreEqual(new[] { "A.Register", "B.Register", "C.Register", "A.Start", "C.Start" }, log);
            Assert.AreEqual(AppHostState.Booted, host.State);
            Assert.IsTrue(host.Services.IsSealed);

            host.StartDeferred();
            Assert.AreEqual("B.Start", log[log.Count - 1]);
            Assert.AreEqual(AppHostState.Running, host.State);

            log.Clear();
            host.Shutdown();
            CollectionAssert.AreEqual(new[] { "B.Shutdown", "C.Shutdown", "A.Shutdown" }, log);
            Assert.AreEqual(AppHostState.ShutDown, host.State);
            host.Shutdown();
            Assert.AreEqual(3, log.Count, "second Shutdown is a no-op");
            Assert.IsEmpty(_errors);
        }

        [Test]
        public void ServicesRegisteredByOneModuleResolveInAnotherModulesStart()
        {
            var log = new List<string>();
            string seen = null;
            var provider = new FakeModule("Provider", AppStartPhase.Immediate, log)
            {
                OnRegister = s => s.Register<IGreeting>(new Greeting()),
            };
            // Listed first, starts first, but registration of all modules already happened.
            var consumer = new FakeModule("Consumer", AppStartPhase.Immediate, log)
            {
                OnStart = ctx => seen = ctx.Services.Get<IGreeting>().Text,
            };
            var host = new AppHost(new IAppModule[] { consumer, provider }, Report);
            host.Boot();
            Assert.AreEqual("hi", seen);
        }

        [Test]
        public void FailingModuleIsReportedAndSkippedWhileOthersRun()
        {
            var log = new List<string>();
            var bad = new FakeModule("Bad", AppStartPhase.Immediate, log) { ThrowOnRegister = true };
            var flaky = new FakeModule("Flaky", AppStartPhase.Immediate, log) { ThrowOnStart = true };
            var good = new FakeModule("Good", AppStartPhase.Immediate, log);
            var host = new AppHost(new IAppModule[] { bad, flaky, good }, Report);

            host.Boot();
            CollectionAssert.DoesNotContain(log, "Bad.Start");
            CollectionAssert.Contains(log, "Good.Start");
            Assert.AreEqual(2, host.Failures.Count);
            Assert.AreEqual("Register", host.Failures[0].Stage);
            Assert.AreEqual("Start", host.Failures[1].Stage);
            Assert.AreEqual(2, _errors.Count);
            CollectionAssert.AreEqual(new IAppModule[] { good }, host.StartedModules);

            log.Clear();
            host.Shutdown();
            CollectionAssert.AreEqual(new[] { "Good.Shutdown" }, log, "only started modules shut down");
        }

        interface IFarewell
        {
        }

        sealed class Farewell : IFarewell
        {
        }

        [Test]
        public void ModuleThatThrowsMidRegisterLeavesNoServicesBehind()
        {
            var log = new List<string>();
            var before = new FakeModule("Before", AppStartPhase.Immediate, log)
            {
                OnRegister = s => s.Register<IFarewell>(new Farewell()),
            };
            var half = new FakeModule("Half", AppStartPhase.Immediate, log)
            {
                OnRegister = s =>
                {
                    s.Register<IGreeting>(new Greeting());
                    throw new InvalidOperationException("boom after the first service");
                },
            };
            bool sawGreeting = true;
            var consumer = new FakeModule("Consumer", AppStartPhase.Immediate, log)
            {
                OnStart = ctx => sawGreeting = ctx.Services.Contains<IGreeting>(),
            };
            var host = new AppHost(new IAppModule[] { before, half, consumer }, Report);

            host.Boot();
            Assert.IsFalse(host.Services.TryGet(out IGreeting _), "service of the skipped module was removed");
            Assert.IsFalse(sawGreeting);
            Assert.IsTrue(host.Services.Contains<IFarewell>(), "services of other modules stay");
            Assert.AreEqual(1, host.Services.Count);
            CollectionAssert.DoesNotContain(log, "Half.Start");
            Assert.AreEqual(1, host.Failures.Count);
            Assert.AreEqual("Half", host.Failures[0].ModuleName);
        }

        [Test]
        public void ModuleThatHitsADuplicateRegistrationIsRolledBack()
        {
            var log = new List<string>();
            var first = new FakeModule("First", AppStartPhase.Immediate, log)
            {
                OnRegister = s => s.Register<IGreeting>(new Greeting()),
            };
            var clash = new FakeModule("Clash", AppStartPhase.Immediate, log)
            {
                OnRegister = s =>
                {
                    s.Register<IFarewell>(new Farewell());
                    s.Register<IGreeting>(new Greeting()); // duplicate: throws
                },
            };
            var host = new AppHost(new IAppModule[] { first, clash }, Report);
            host.Boot();
            Assert.IsTrue(host.Services.Contains<IGreeting>(), "the first module's service is untouched");
            Assert.IsFalse(host.Services.Contains<IFarewell>(), "the clashing module's earlier service is removed");
            CollectionAssert.AreEqual(new IAppModule[] { first }, host.StartedModules);
        }

        [Test]
        public void LifecycleMisuseThrows()
        {
            var host = new AppHost(Array.Empty<IAppModule>(), Report);
            Assert.Throws<InvalidOperationException>(() => host.StartDeferred());
            host.Boot();
            Assert.Throws<InvalidOperationException>(() => host.Boot());
            Assert.Throws<InvalidOperationException>(() => host.Services.Register<IGreeting>(new Greeting()));
        }

        [Test]
        public void DuplicateOrNullModulesAreRejected()
        {
            var log = new List<string>();
            Assert.Throws<ArgumentException>(() => new AppHost(new IAppModule[]
            {
                new FakeModule("X", AppStartPhase.Immediate, log),
                new FakeModule("X", AppStartPhase.Immediate, log),
            }, Report));
            Assert.Throws<ArgumentException>(() => new AppHost(new IAppModule[] { null }, Report));
        }

        [Test]
        public void ServiceRegistryRejectsDuplicatesAndExplainsMissingServices()
        {
            var services = new AppServices();
            services.Register<IGreeting>(new Greeting());
            Assert.Throws<InvalidOperationException>(() => services.Register<IGreeting>(new Greeting()));
            Assert.IsTrue(services.TryGet(out IGreeting g));
            Assert.AreEqual("hi", g.Text);
            var ex = Assert.Throws<InvalidOperationException>(() => services.Get<IAppPaths>());
            StringAssert.Contains("AppModuleCatalog", ex.Message);
        }

        [Test]
        public void CatalogStartsWithPlatformModuleThatPublishesPlatformServices()
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ballverse-tests-" + System.IO.Path.GetRandomFileName());
            try
            {
                int appliedFps = 0;
                var platform = new AppPlatform(
                    new AppLifecycle(Report),
                    new FrameRatePolicy(fps => appliedFps = fps),
                    new ScreenAwake(_ => { }),
                    new SafeAreaProvider(Report),
                    new AppPaths(System.IO.Path.Combine(root, "persistent"), System.IO.Path.Combine(root, "cache")));
                IReadOnlyList<IAppModule> modules = AppModuleCatalog.Create(platform);
                Assert.IsInstanceOf<PlatformModule>(modules[0]);

                var host = new AppHost(modules, Report);
                host.Boot();
                Assert.AreSame(platform.Lifecycle, host.Services.Get<IAppLifecycle>());
                Assert.AreSame(platform.FrameRate, host.Services.Get<IFrameRatePolicy>());
                Assert.AreSame(platform.ScreenAwake, host.Services.Get<IScreenAwake>());
                Assert.AreSame(platform.SafeArea, host.Services.Get<ISafeAreaProvider>());
                Assert.AreSame(platform.Paths, host.Services.Get<IAppPaths>());
                Assert.AreEqual(60, appliedFps);
                Assert.IsTrue(System.IO.Directory.Exists(platform.Paths.Projects));
                Assert.IsTrue(System.IO.Directory.Exists(platform.Paths.RenderStreamCache));
                Assert.IsEmpty(_errors);
            }
            finally
            {
                if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
            }
        }
    }
}
