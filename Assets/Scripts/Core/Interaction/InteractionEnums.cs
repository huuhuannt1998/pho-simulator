namespace Pho.Core.Interaction
{
    /// <summary>Which interaction button produced this InteractionContext.</summary>
    public enum InteractionVerb
    {
        Primary,
        Secondary
    }

    /// <summary>
    /// How a held IHoldable is carried -- drives the pose blended onto
    /// IInteractorAgent.HoldAnchor by the carry system (M3).
    /// </summary>
    public enum HoldPose
    {
        TwoHandedLow,
        OneHandedFront,
        Tray
    }
}
