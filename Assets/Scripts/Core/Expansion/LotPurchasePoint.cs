using Pho.Core.Interaction;
using Pho.Domain.Events;
using Pho.Domain.Expansion;
using UnityEngine;

namespace Pho.Core.Expansion
{
    /// <summary>
    /// The physical thing a player walks up to and buys a unit at: the boarded
    /// door / FOR LEASE sign standing between the shop and the next lot. Put
    /// this on a collider on the <c>Interactable</c> layer inside the unit the
    /// player ALREADY owns, facing the unit being sold, and set
    /// <see cref="lotId"/> to the lot on the far side.
    ///
    /// Placement is what makes adjacency legible without any UI: you can only
    /// stand in front of a door that exists in a room you own, so the pure
    /// <see cref="ExpansionModel"/> adjacency rule and the level layout say
    /// the same thing. The model is still the authority -- a mis-placed door
    /// declines with <see cref="LotRefusalReason.NotAdjacent"/> rather than
    /// letting geometry override the rule.
    ///
    /// CO-OP: this passes the interacting agent's identity through as
    /// <see cref="LotPurchaseRequest.RequestedBy"/> and calls
    /// <c>TryPurchase</c> unconditionally. On a client that is not the
    /// authority, TryPurchase declines with
    /// <see cref="LotRefusalReason.NotAuthorised"/> and publishes a refusal --
    /// which is exactly the hook the networking agent replaces with "send the
    /// request to the host". Nothing here needs to change when that lands;
    /// see ExpansionService's SHARED-BANK DECISION.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LotPurchasePoint : MonoBehaviour, IInteractable
    {
        [Tooltip("Lot sold at this door, e.g. lot.unit_2. Must exist in the lot catalog.")]
        [SerializeField] string lotId = "lot.unit_2";

        [Tooltip("Hidden once the lot is owned -- the door has been opened up, there is nothing left to buy.")]
        [SerializeField] bool hideWhenOwned = true;

        ExpansionService _service;

        public LotId Lot => new LotId(lotId);

        void OnEnable() => Refresh();

        ExpansionService Service()
        {
            if (_service != null) return _service;

            var ctx = GameBootstrap.Current;
            if (ctx == null) return null;

            ctx.TryGet<ExpansionService>(out _service);
            return _service;
        }

        void Refresh()
        {
            if (!hideWhenOwned) return;

            var service = Service();
            if (service == null) return;
            if (service.IsOwned(Lot)) gameObject.SetActive(false);
        }

        public string GetInteractionText(in InteractionContext ctx)
        {
            var service = Service();
            if (service == null) return "Unavailable";
            if (!service.Registry.TryGet(Lot, out var def)) return "Unavailable";

            if (service.IsOwned(Lot)) return $"{def.DisplayName} -- already yours";
            if (!service.TryGetPrice(Lot, out var price)) return "Unavailable";

            // RequestPurchase deliberately reports no authority failure, so a
            // co-op client's prompt reads the same as the host's.
            if (service.RequestPurchase(new LotPurchaseRequest(Lot), out var reason))
                return $"Press E to buy {def.DisplayName} -- {price:C0}";

            switch (reason)
            {
                case LotRefusalReason.InsufficientFunds:
                    return $"{def.DisplayName} -- {price:C0} (not enough in the till)";
                case LotRefusalReason.NotAdjacent:
                    return $"{def.DisplayName} -- buy the unit next to it first";
                case LotRefusalReason.Vetoed:
                    return $"{def.DisplayName} -- {price:C0} (needs approval)";
                default:
                    return $"{def.DisplayName} -- unavailable";
            }
        }

        /// <summary>
        /// True whenever the prompt should be actionable. Uses the same
        /// preflight the text does, so the two can never disagree.
        /// </summary>
        public bool CanInteract(in InteractionContext ctx)
        {
            var service = Service();
            return service != null && service.RequestPurchase(new LotPurchaseRequest(Lot), out _);
        }

        public void Interact(in InteractionContext ctx)
        {
            var service = Service();
            if (service == null) return;

            if (service.TryPurchase(new LotPurchaseRequest(Lot, DescribeAgent(ctx.Agent))))
            {
                Refresh();
            }
        }

        /// <summary>
        /// Best-effort human identity for the buyer toast. <c>IInteractorAgent</c>
        /// is frozen contract surface with no name/id member, and Pho.Core
        /// cannot reference Pho.Player or the networking assembly, so the
        /// agent's GameObject name is the only identity available today. The
        /// networking agent will supply a real player id by constructing the
        /// request itself; this string is never used as a key, only displayed.
        /// </summary>
        static string DescribeAgent(IInteractorAgent agent)
        {
            var anchor = agent?.HoldAnchor;
            if (anchor == null) return null;

            var root = anchor.root;
            return root != null ? root.name : anchor.name;
        }
    }
}
