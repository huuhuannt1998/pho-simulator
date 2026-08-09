using System;
using System.Collections.Generic;

namespace Pho.Domain.Events
{
    public sealed class EventBus : IEventBus
    {
        // Reference-typed per-event-type handler lists live behind a
        // Dictionary<Type, object> box, but the struct events flowing
        // through Action<T> invocations are never boxed.
        readonly Dictionary<Type, object> _handlerLists = new Dictionary<Type, object>();
        readonly Action<Exception> _onHandlerException;

        public EventBus(Action<Exception> onHandlerException = null)
        {
            _onHandlerException = onHandlerException ?? (ex => Console.Error.WriteLine(ex));
        }

        public void Publish<T>(in T evt) where T : struct, IGameEvent
        {
            var list = GetOrCreateList<T>();
            list.Dispatching++;
            try
            {
                // Index-based iteration: safe even if a handler (via Dispose
                // on Unsubscribe) mutates PendingRemovals/PendingAdds, since
                // those are applied only after Dispatching drops back to 0.
                for (int i = 0; i < list.Handlers.Count; i++)
                {
                    var handler = list.Handlers[i];
                    if (handler == null) continue; // tombstoned by a same-frame unsubscribe

                    try
                    {
                        handler(evt);
                    }
                    catch (Exception ex)
                    {
                        _onHandlerException(ex);
                    }
                }
            }
            finally
            {
                list.Dispatching--;
                if (list.Dispatching == 0)
                {
                    list.ApplyPending();
                }
            }
        }

        public IDisposable Subscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var list = GetOrCreateList<T>();
            if (list.Dispatching > 0)
            {
                list.PendingAdds.Add(handler);
            }
            else
            {
                list.Handlers.Add(handler);
            }

            return new Subscription<T>(list, handler);
        }

        HandlerList<T> GetOrCreateList<T>() where T : struct, IGameEvent
        {
            var type = typeof(T);
            if (_handlerLists.TryGetValue(type, out var existing))
            {
                return (HandlerList<T>)existing;
            }

            var created = new HandlerList<T>();
            _handlerLists[type] = created;
            return created;
        }

        sealed class HandlerList<T> where T : struct, IGameEvent
        {
            public readonly List<Action<T>> Handlers = new List<Action<T>>();
            public readonly List<Action<T>> PendingAdds = new List<Action<T>>();
            public readonly List<Action<T>> PendingRemoves = new List<Action<T>>();
            public int Dispatching;

            public void Remove(Action<T> handler)
            {
                if (Dispatching > 0)
                {
                    PendingRemoves.Add(handler);
                    // Tombstone immediately so a handler that unsubscribes
                    // itself (or another handler still in this dispatch)
                    // doesn't fire again this same Publish.
                    var idx = Handlers.IndexOf(handler);
                    if (idx >= 0) Handlers[idx] = null;
                }
                else
                {
                    Handlers.Remove(handler);
                }
            }

            public void ApplyPending()
            {
                if (PendingRemoves.Count > 0)
                {
                    foreach (var h in PendingRemoves) Handlers.Remove(h);
                    PendingRemoves.Clear();
                }
                Handlers.RemoveAll(h => h == null);

                if (PendingAdds.Count > 0)
                {
                    Handlers.AddRange(PendingAdds);
                    PendingAdds.Clear();
                }
            }
        }

        sealed class Subscription<T> : IDisposable where T : struct, IGameEvent
        {
            HandlerList<T> _list;
            Action<T> _handler;

            public Subscription(HandlerList<T> list, Action<T> handler)
            {
                _list = list;
                _handler = handler;
            }

            public void Dispose()
            {
                if (_list == null) return; // idempotent
                _list.Remove(_handler);
                _list = null;
                _handler = null;
            }
        }
    }
}
