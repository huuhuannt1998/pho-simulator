using Pho.Core.Interaction;
using UnityEngine;

namespace Pho.Core.DayCycle
{
    /// <summary>
    /// Physical world object the player interacts with to open/close the
    /// restaurant -- "the player controls when the restaurant opens and
    /// closes" (GDD §3). A thin IInteractable wrapper around
    /// <see cref="RestaurantStateServiceBehaviour"/>.
    ///
    /// Unlike Kitchen stations/PlayerInteractor, this does not use an
    /// explicit Bind(...) call from a scene-building pass, because its
    /// dependency (RestaurantStateServiceBehaviour) doesn't exist at
    /// edit-time -- GameBootstrap.Awake() constructs it at runtime, after
    /// the scene is already built. Instead this resolves it lazily on first
    /// use via the one deliberate GameBootstrap.Current singleton exception
    /// (see GameBootstrap's own class doc comment), exactly the situation
    /// that seam exists for.
    /// </summary>
    public sealed class RestaurantSign : MonoBehaviour, IInteractable
    {
        RestaurantStateServiceBehaviour _service;

        /// <summary>The composition root <see cref="_service"/> came from -- see the stale-context note in TryResolveService.</summary>
        GameContext _ctx;

        bool TryResolveService()
        {
            var ctx = GameBootstrap.Current;
            if (ctx == null) return false;

            // Same stale-context hazard DirtyTable.TryResolveService documents
            // at length: GameBootstrap.Current is static and survives a scene
            // unload, so a cached service can belong to a dead context after a
            // reload -- here that would leave the sign driving the PREVIOUS
            // scene's day clock, so opening the restaurant would appear to do
            // nothing. Re-resolve whenever the root object changes.
            if (!ReferenceEquals(ctx, _ctx))
            {
                _ctx = ctx;
                _service = null;
            }

            if (_service != null) return true;
            return ctx.TryGet(out _service);
        }

        public string GetInteractionText(in InteractionContext ctx)
        {
            if (!TryResolveService()) return string.Empty;

            return _service.Phase switch
            {
                Pho.Domain.DayCycle.DayPhase.Open => "Press E to close the restaurant",
                Pho.Domain.DayCycle.DayPhase.Prep => "Press E to open the restaurant",
                Pho.Domain.DayCycle.DayPhase.Closed => "Closed for the day",
                _ => string.Empty,
            };
        }

        public bool CanInteract(in InteractionContext ctx)
        {
            if (!TryResolveService()) return false;
            return _service.Phase == Pho.Domain.DayCycle.DayPhase.Prep || _service.Phase == Pho.Domain.DayCycle.DayPhase.Open;
        }

        public void Interact(in InteractionContext ctx)
        {
            if (!TryResolveService()) return;

            switch (_service.Phase)
            {
                case Pho.Domain.DayCycle.DayPhase.Prep:
                    _service.OpenRestaurant();
                    break;
                case Pho.Domain.DayCycle.DayPhase.Open:
                    _service.CloseRestaurant();
                    break;
                // Closed: CanInteract already refuses this case.
            }
        }
    }
}
