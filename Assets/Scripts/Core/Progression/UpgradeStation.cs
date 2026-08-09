using Pho.Core.Interaction;
using Pho.Domain.Identity;
using UnityEngine;

namespace Pho.Core.Progression
{
    /// <summary>
    /// The physical thing the player walks up to and buys the upgrade from
    /// (GDD §55.14 -- "buys one upgrade"). A thin IInteractable wrapper
    /// around <see cref="ProgressionService"/>, deliberately holding no
    /// state of its own: what is owned and what it costs both live in the
    /// service, so this can never drift out of sync with the save file.
    ///
    /// Like <see cref="Pho.Core.DayCycle.RestaurantSign"/> (which this is
    /// modelled on) it does NOT take an explicit Bind(...) call from the
    /// scene-building pass, because its dependency does not exist at
    /// edit-time -- GameBootstrap.Awake() constructs ProgressionService at
    /// runtime, after the scene has already been built. It resolves lazily
    /// on first use via the one deliberate GameBootstrap.Current singleton
    /// exception (see GameBootstrap's own class doc comment), which is
    /// exactly the situation that seam exists for. The same caveat applies
    /// as for RestaurantSign: this is not a general service locator, and
    /// nothing else should grow a static hook here.
    ///
    /// Inert but non-throwing until it can resolve: before bootstrap, or in
    /// a scene with no ProgressionService, GetInteractionText returns empty,
    /// CanInteract returns false, and Interact is a no-op.
    /// </summary>
    public sealed class UpgradeStation : MonoBehaviour, IInteractable
    {
        [Header("Upgrade")]
        [Tooltip("Equipment id to sell here. Must match an EquipmentData asset in Assets/Content/Equipment (see ContentManifest).")]
        [SerializeField] string equipmentId = "eq.burner_commercial";

        ProgressionService _service;

        EquipmentId Id => new EquipmentId(equipmentId);

        bool TryResolveService()
        {
            if (_service != null) return true;

            var ctx = GameBootstrap.Current;
            if (ctx == null) return false;

            return ctx.TryGet(out _service);
        }

        public string GetInteractionText(in InteractionContext ctx)
        {
            if (!TryResolveService()) return string.Empty;

            var id = Id;
            if (_service.IsOwned(id)) return "Already installed";

            if (!_service.TryGetCost(id, out var cost)) return string.Empty;

            // CanPurchase and the prompt are driven by the same rules, so
            // the text can never promise something Interact would decline.
            return _service.CanPurchase(id)
                ? $"Press E to buy ({cost:C})"
                : $"Not enough cash ({cost:C})";
        }

        public bool CanInteract(in InteractionContext ctx)
        {
            return TryResolveService() && _service.CanPurchase(Id);
        }

        public void Interact(in InteractionContext ctx)
        {
            if (!TryResolveService()) return;

            // TryPurchase re-checks everything CanInteract did; its return
            // value is intentionally ignored here because every decline path
            // is a normal runtime occurrence the prompt already communicates.
            _service.TryPurchase(Id);
        }
    }
}
