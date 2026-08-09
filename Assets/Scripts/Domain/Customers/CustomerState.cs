namespace Pho.Domain.Customers
{
    /// <summary>
    /// Customer lifecycle states. Exact enum from architecture.md section 6.2
    /// -- frozen shape, a sibling agent is coding the Unity-side
    /// <see cref="ICustomerWorld"/> implementation (<c>CustomerAgent</c>)
    /// against this literal set of names.
    /// </summary>
    public enum CustomerState
    {
        Spawning, WalkingToEntrance, Queuing, WalkingToSeat, Sitting,
        BrowsingMenu, ReadyToOrder, WaitingForFood, Eating, Paying,
        Leaving, LeavingAngry, Despawned
    }
}
