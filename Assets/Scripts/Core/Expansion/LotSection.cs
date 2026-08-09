using System;
using Pho.Domain.Events;
using Pho.Domain.Expansion;
using UnityEngine;

namespace Pho.Core.Expansion
{
    /// <summary>
    /// The scene half of a lot. Put this on a small ALWAYS-ACTIVE marker
    /// object ("Unit2_Gate") and point <see cref="sectionRoot"/> at the unit's
    /// geometry root (walls, floor, its tables, its lights), which the scene
    /// ships DEACTIVATED. While the lot is unowned that root stays off, so the
    /// unit costs nothing to run and the player cannot walk into it; the
    /// moment it is bought -- or restored from a save -- it switches on.
    ///
    /// The marker/geometry split is not ceremony: a component on an inactive
    /// object gets no OnEnable and no events, so it could never switch itself
    /// back on. The gate must outlive the thing it gates. If
    /// <see cref="sectionRoot"/> is left empty this falls back to the first
    /// child, which makes the common case (gate object with the unit
    /// parented under it) zero-config.
    ///
    /// This is deliberately the ONLY thing that translates ownership into
    /// world state. One component, one id, one SetActive: nothing else in the
    /// scene needs to know the expansion system exists, and adding a sixth
    /// unit is a prefab + one id string, not a code change.
    ///
    /// The starting shop should NOT carry one of these -- it is always owned,
    /// and a component that never does anything is a component someone later
    /// has to reason about. (If one is attached anyway it activates on the
    /// first frame and is harmless.)
    ///
    /// Resolution goes through the one deliberate <c>GameBootstrap.Current</c>
    /// singleton exception (see GameBootstrap's class doc; RestaurantSign and
    /// ProgressionService.CurrentModifiersOrDefault use the same convention),
    /// because a scene-built object exists long before any installer runs and
    /// has no Bind seam of its own. In a scene that never booted this leaves
    /// the section in its authored state rather than throwing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LotSection : MonoBehaviour
    {
        [Tooltip("Stable lot id this section of the building belongs to, e.g. lot.unit_2. Must exist in the lot catalog.")]
        [SerializeField] string lotId = "lot.unit_2";

        [Tooltip("Geometry root switched on when the lot is owned; ship it deactivated. Leave empty to use the first child of this object.")]
        [SerializeField] GameObject sectionRoot;

        ExpansionService _service;
        IDisposable _purchaseSub;

        public LotId Lot => new LotId(lotId);

        void OnEnable()
        {
            Resolve();
            Apply();
        }

        void OnDisable()
        {
            _purchaseSub?.Dispose();
            _purchaseSub = null;
        }

        void Resolve()
        {
            if (_service != null) return;

            var ctx = GameBootstrap.Current;
            if (ctx == null) return;
            if (!ctx.TryGet<ExpansionService>(out var service)) return;

            _service = service;

            // Subscribed here rather than in OnEnable's first line so a
            // scene that boots AFTER this component enables still ends up
            // subscribed the first time Resolve() succeeds.
            _purchaseSub = ctx.Events.Subscribe<LotPurchased>(OnLotPurchased);
        }

        void OnLotPurchased(LotPurchased evt)
        {
            if (!evt.Lot.Equals(Lot)) return;
            Apply();
        }

        /// <summary>
        /// Pushes current ownership onto the scene. Public so the save-restore
        /// path, or an editor tool previewing a fully-expanded complex, can
        /// force a refresh without a purchase event.
        /// </summary>
        public void Apply()
        {
            Resolve();
            if (_service == null) return;

            var root = ResolveRoot();
            if (root == null) return;

            var owned = _service.IsOwned(Lot);
            if (root.activeSelf != owned) root.SetActive(owned);
        }

        /// <summary>
        /// The gated geometry. Never this GameObject -- see the class doc's
        /// marker/geometry note; gating the gate would be a one-way trip.
        /// </summary>
        GameObject ResolveRoot()
        {
            if (sectionRoot != null && sectionRoot != gameObject) return sectionRoot;
            return transform.childCount > 0 ? transform.GetChild(0).gameObject : null;
        }
    }
}
