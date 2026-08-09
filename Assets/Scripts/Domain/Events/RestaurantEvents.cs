namespace Pho.Domain.Events
{
    /// <summary>
    /// Notification-only, past-tense facts (architecture.md §3's event rule)
    /// that the restaurant's open/closed sign changed. Published by
    /// Pho.Core's RestaurantStateServiceBehaviour (Core/DayCycle) whenever
    /// DayClock.OpenRestaurant()/CloseRestaurant() succeeds. Deliberately
    /// empty -- there is nothing beyond the fact itself to carry; a
    /// subscriber that needs the current phase/day reads
    /// RestaurantStateServiceBehaviour.Phase/Day directly, same as
    /// CashChanged carries its own NewBalance rather than making every
    /// subscriber re-query an EconomyService.
    /// </summary>
    public readonly struct RestaurantOpened : IGameEvent { }

    /// <summary>See <see cref="RestaurantOpened"/> -- the closing counterpart.</summary>
    public readonly struct RestaurantClosed : IGameEvent { }
}
