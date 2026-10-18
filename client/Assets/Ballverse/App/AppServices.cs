using System;
using System.Collections.Generic;

namespace Ballverse.App
{
    /// <summary>
    /// Minimal typed service registry used by the composition root. Services are registered by contract type during
    /// the register phase and resolved during start; registering after <see cref="Seal"/> is a programming error.
    /// </summary>
    public sealed class AppServices
    {
        readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        public bool IsSealed { get; private set; }

        public int Count => _services.Count;

        public void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (IsSealed) throw new InvalidOperationException("Service registration is closed; register " + typeof(T).Name + " in IAppModule.Register.");
            if (_services.ContainsKey(typeof(T))) throw new InvalidOperationException("Service already registered: " + typeof(T).FullName);
            _services.Add(typeof(T), service);
        }

        public T Get<T>() where T : class
        {
            if (_services.TryGetValue(typeof(T), out object service)) return (T)service;
            throw new InvalidOperationException("Service not registered: " + typeof(T).FullName + ". Is its module listed in AppModuleCatalog?");
        }

        public bool TryGet<T>(out T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out object value))
            {
                service = (T)value;
                return true;
            }
            service = null;
            return false;
        }

        public bool Contains<T>() where T : class => _services.ContainsKey(typeof(T));

        /// <summary>Closes registration. Called by <see cref="AppHost"/> after every module has registered.</summary>
        public void Seal() => IsSealed = true;
    }
}
