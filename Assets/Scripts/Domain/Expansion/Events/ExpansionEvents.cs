using Pho.Domain.Expansion;

namespace Pho.Domain.Events
{
    // New event structs go in the owning agent's own Events file, never a
    // shared one (architecture.md section 10, rule 4) -- hence this file
    // living under Domain/Expansion/Events/ instead of alongside
    // EconomyEvents.cs. The NAMESPACE is still Pho.Domain.Events so
    // subscribers keep one using directive for every game event, matching
    // KitchenEvents/OrderEvents/EconomyEvents.
    //
    // Past-tense names, facts that already happened -- commands go through
    // ExpansionService, not the bus (architecture.md section 3).

    /// <summary>
    /// A unit was added to the complex. Carries everything a listener needs
    /// without a callback into the service: the door prefab that should now
    /// open, the toast that should read "Minh bought Unit 2", the NavMesh
    /// rebake trigger, and the co-op announcement all subscribe to this.
    /// </summary>
    public readonly struct LotPurchased : IGameEvent
    {
        public readonly LotId Lot;

        /// <summary>What was actually debited, so a listener never re-reads a price that may have been retuned.</summary>
        public readonly decimal Price;

        /// <summary>
        /// Who asked for it. Free-form so it can hold a network player id, a
        /// display name, or <c>null</c> in single player -- Pho.Domain must
        /// not depend on the networking agent's player-id type, and a plain
        /// string keeps the save/replay path trivial.
        /// </summary>
        public readonly string RequestedBy;

        /// <summary>Complex size AFTER this purchase. Never zero.</summary>
        public readonly int OwnedLotCount;

        public LotPurchased(LotId lot, decimal price, string requestedBy, int ownedLotCount)
        {
            Lot = lot;
            Price = price;
            RequestedBy = requestedBy;
            OwnedLotCount = ownedLotCount;
        }
    }

    /// <summary>
    /// A purchase was asked for and declined. Still a past-tense fact, not a
    /// command -- and it is the co-op case that makes it worth publishing:
    /// when a client asks the host to buy a unit, the refusal has to travel
    /// back and surface as "not enough in the till" on THAT player's screen.
    /// Single-player UI can use it for the same message with no extra path.
    /// </summary>
    public readonly struct LotPurchaseRefused : IGameEvent
    {
        public readonly LotId Lot;
        public readonly LotRefusalReason Reason;
        public readonly string RequestedBy;

        public LotPurchaseRefused(LotId lot, LotRefusalReason reason, string requestedBy)
        {
            Lot = lot;
            Reason = reason;
            RequestedBy = requestedBy;
        }
    }

    /// <summary>
    /// Every way a lot purchase can be declined. A superset of the pure
    /// <see cref="LotEligibility"/>: the Domain model knows nothing about
    /// money, authority, or whether a service was ever bound, so those three
    /// reasons only exist once a service is in play.
    /// </summary>
    public enum LotRefusalReason
    {
        UnknownLot = 0,
        AlreadyOwned,
        NotAdjacent,

        /// <summary>The shared bank cannot cover it. See ExpansionService's AFFORDABILITY DECISION.</summary>
        InsufficientFunds,

        /// <summary>This peer is not the authority over shared money. See ExpansionService's SHARED-BANK DECISION.</summary>
        NotAuthorised,

        /// <summary>An approval hook (a future confirmation prompt or party vote) said no.</summary>
        Vetoed,

        /// <summary>No economy/registry bound -- a scene that never booted. Not a player-facing case.</summary>
        ServiceUnavailable,
    }
}
