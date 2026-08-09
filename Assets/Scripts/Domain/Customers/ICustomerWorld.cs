using Pho.Domain.Identity;
using Pho.Domain.Infra;
using Pho.Domain.Orders;

namespace Pho.Domain.Customers
{
    /// <summary>
    /// The world-facing surface <see cref="CustomerBrain"/> drives to act
    /// out its lifecycle. Implemented by <c>CustomerAgent</c> (<c>Pho.Customers</c>,
    /// Unity-side) in production and by a scripted fake in tests. Exact
    /// shape frozen by architecture.md section 6.2 -- FROZEN, zero
    /// deviation. A sibling agent is implementing the Unity-side
    /// <c>CustomerAgent</c> against this literal interface concurrently in a
    /// separate worktree; do not add, remove, or rename members here.
    /// </summary>
    public interface ICustomerWorld
    {
        bool RestaurantIsOpen { get; }
        bool TryClaimSeat(CustomerId id, out SeatHandle seat);
        void ReleaseSeat(SeatHandle seat);
        void MoveTo(Vec3 destination);
        bool HasArrived { get; }
        Vec3 EntrancePosition { get; }
        Vec3 ExitPosition { get; }
        float Cleanliness01 { get; }
        OrderId PlaceOrder(CustomerId id, SeatHandle seat, RecipeId recipe, OrderModifiers mods);
        bool TryTakeServedDish(OrderId order, out ServedDish dish);
        void Pay(CustomerId id, decimal amount, decimal tip);
        void Despawn();
    }
}
