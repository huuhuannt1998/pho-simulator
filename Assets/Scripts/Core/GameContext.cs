using System;
using System.Collections.Generic;
using Pho.Domain.Events;

namespace Pho.Core
{
    /// <summary>
    /// Never add a field to this file for a specific system. Register the
    /// service through your own XInstaller (see IInstaller) and fetch it
    /// with Get&lt;T&gt;() instead -- this is the one shared file every agent
    /// would otherwise want to edit simultaneously.
    /// </summary>
    public sealed class GameContext
    {
        readonly ServiceRegistry _registry = new ServiceRegistry();

        public IEventBus Events { get; }

        public GameContext(IEventBus events)
        {
            Events = events ?? throw new ArgumentNullException(nameof(events));
            _registry.Register(events);
        }

        public void Register<T>(T instance) where T : class => _registry.Register(instance);
        public T Get<T>() where T : class => _registry.Get<T>();
        public bool TryGet<T>(out T service) where T : class => _registry.TryGet(out service);
    }

    public sealed class ServiceRegistry
    {
        readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        public void Register<T>(T instance) where T : class
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            var type = typeof(T);
            if (_services.ContainsKey(type))
            {
                throw new InvalidOperationException($"Service {type.Name} is already registered.");
            }
            _services[type] = instance;
        }

        public T Get<T>() where T : class
        {
            if (TryGet<T>(out var service)) return service;
            throw new InvalidOperationException($"Service {typeof(T).Name} is not registered.");
        }

        public bool TryGet<T>(out T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out var raw))
            {
                service = (T)raw;
                return true;
            }
            service = null;
            return false;
        }
    }
}
