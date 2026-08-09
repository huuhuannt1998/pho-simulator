using UnityEngine;
using Pho.Core.Interaction;
using Pho.Domain.Contracts;
using Pho.Domain.Identity;
using Pho.Domain.Inventory;

namespace Pho.Kitchen
{
    /// <summary>
    /// One physical ingredient-source station: player aims at it while
    /// holding a BowlObject, presses E, the station draws one fixed portion
    /// of a configured ingredient from the shared InventoryModel and adds it
    /// into the held bowl at a configured ComponentSlot. Covers noodle,
    /// protein (meat), aromatic (onion), and herb sources -- every
    /// non-broth slot in the two generated recipes (rec.pho_tai,
    /// rec.pho_chin); Broth is handled separately by BrothPot because it
    /// comes from a simmered pot, not a static InventoryModel lot.
    ///
    /// Meat-station-variant design (judgment call): the task asks how the
    /// player picks between ing.beef_brisket (Tái) and ing.beef_well_done
    /// (Chín). Rather than write two near-duplicate hardcoded MonoBehaviour
    /// classes, or build an in-scene toggle/selector UI (the M5 brief
    /// explicitly scopes variable/interactive amount-picking out of this
    /// pass -- "not a variable-amount slider/hold-to-fill mechanic"), this
    /// single class is configured per scene instance via the [SerializeField]
    /// below. Two GameObjects using this exact component -- one with
    /// ingredientId = "ing.beef_brisket", one with
    /// ingredientId = "ing.beef_well_done", both slot = Protein, placed at
    /// different points in the kitchen -- ARE the two meat-station variants:
    /// the player picks which meat by walking to the physical station for
    /// it, exactly the way they already pick noodles vs. onion by walking to
    /// a different counter. Zero duplicated station code, and the same
    /// component also covers the noodle/onion/herb stations with no further
    /// subclassing. A later content/scene-generation pass instances this
    /// component once per station and fills in the Inspector fields (this
    /// script owns no scene/prefab authoring -- see Pho.Kitchen's exclusive
    /// ownership of Assets/Scripts/Kitchen/** only).
    ///
    /// Fixed portion per press (no hold-to-fill): matches the M5 brief's
    /// "one press = one standard portion" simplification. portionAmount is
    /// serialized so a later balance/content pass can tune it per station to
    /// match each recipe's RecipeComponent.amount (1.0 for noodle/protein,
    /// 0.3 for the optional onion/herb garnish slots in the generated
    /// content) without a code change.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class IngredientStation : MonoBehaviour, IInteractable
    {
        [Header("Content (configure per station instance)")]
        [Tooltip("e.g. \"ing.rice_noodles\", \"ing.beef_brisket\", \"ing.beef_well_done\", \"ing.onion\", \"ing.herbs_mixed\".")]
        [SerializeField] string ingredientId = "ing.rice_noodles";
        [SerializeField] ComponentSlot slot = ComponentSlot.Noodle;
        [Tooltip("Fixed amount drawn from InventoryModel per interaction -- no variable-amount/hold-to-fill mechanic in this pass.")]
        [SerializeField] float portionAmount = 1f;
        [Tooltip("Prompt name, e.g. \"noodles\" -> \"Press E to add noodles\".")]
        [SerializeField] string displayName = "ingredient";

        InventoryModel _inventory;
        bool _bound;

        /// <summary>
        /// Injection seam: a future integration/wiring pass calls this once
        /// per station with the scene's single shared InventoryModel, so
        /// every station draws from the same stock rather than a private
        /// one. Until this is called the station is inert (prompts stay
        /// empty, CanInteract stays false) rather than throwing or reaching
        /// for a singleton -- see GameBootstrap/PlayerInteractor.Bind for
        /// the established pattern this mirrors.
        /// </summary>
        public void Bind(InventoryModel inventory)
        {
            _inventory = inventory;
            _bound = inventory != null;
        }

        public string GetInteractionText(in InteractionContext ctx)
        {
            if (!_bound) return string.Empty;
            if (ctx.Agent == null || !(ctx.Agent.Held is BowlObject)) return string.Empty;

            var id = new IngredientId(ingredientId);
            if (_inventory.TotalQuantity(id) <= 0f)
                return $"Out of {displayName}";

            return $"Press E to add {displayName}";
        }

        public bool CanInteract(in InteractionContext ctx)
        {
            if (!_bound || ctx.Agent == null) return false;
            if (!(ctx.Agent.Held is BowlObject bowl)) return false;

            // Defends against wasting a TryConsume on a bowl that can no
            // longer accept it (already placed/scored at the pass). Slot
            // over-capacity (e.g. a second noodle portion) is NOT
            // pre-checked here -- BowlContents' per-slot capacity is a
            // private implementation detail of the frozen Domain type, not
            // something this station may duplicate/guess. In normal play
            // (one visit per station per bowl) this never triggers; if a
            // player presses E twice at the same station, the second
            // AddIngredient call safely returns AddResult.SlotOverCapacity
            // from BowlContents.Add without corrupting the bowl -- the one
            // known, accepted rough edge is that the ingredient already
            // drawn from inventory for that second press is not refunded.
            return !bowl.Contents.IsSealed;
        }

        public void Interact(in InteractionContext ctx)
        {
            if (!_bound) return;
            if (!(ctx.Agent?.Held is BowlObject bowl)) return;
            if (bowl.Contents.IsSealed) return;

            var id = new IngredientId(ingredientId);
            if (!_inventory.TryConsume(id, portionAmount, out var consumed)) return;

            bowl.AddIngredient(slot, consumed, Time.time);
        }
    }
}
