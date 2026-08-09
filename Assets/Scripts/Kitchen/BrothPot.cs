using UnityEngine;
using Pho.Core.Interaction;
using Pho.Domain.Contracts;
using Pho.Domain.Cooking;
using Pho.Domain.Events;
using Pho.Domain.Identity;
using Pho.Domain.Inventory;

namespace Pho.Kitchen
{
    /// <summary>
    /// The broth pot: wraps a BrothState (Pho.Domain.Cooking.BrothSimulator)
    /// and ticks it forward over real time once bound, then lets the player
    /// ladle broth into a held BowlObject once Ready.
    ///
    /// Fill trigger (judgment call, per the M5 brief's explicit invitation
    /// to choose one): interacting with the pot while NOT holding a
    /// BowlObject (hands free, or holding something else) starts or
    /// restarts a batch -- it consumes fillPortionAmount of
    /// brothIngredientId ("ing.broth_base" by default, the one Broth-category
    /// ingredient in the generated content) from the shared InventoryModel,
    /// and only when that succeeds resets BrothState to a fresh Empty state
    /// so the next Update() tick (see BrothSimulator.Tick's Empty case)
    /// naturally advances it into Filling. This trigger is only available
    /// while the pot is Empty or Overcooked (i.e. it needs a fresh batch) --
    /// interacting hands-free mid-brew is a no-op by design, so the player
    /// can't top up a pot that's already cooking. Ladling (hands full with a
    /// bowl) is a completely separate branch of Interact() and only fires
    /// while Phase == Ready.
    ///
    /// Broth quality/freshness fed into BowlObject.AddIngredient: the ladled
    /// portion's Quality01 is the pot's own simulated BrothState.Quality01 at
    /// the moment of ladling (the true quality of *that* simmer, mirroring
    /// how InventoryModel.TryConsume feeds a lot's true quality into a
    /// BowlComponent). Freshness01 is fixed at 1f -- a freshly ladled bowl of
    /// hot broth has no independent "spoilage" concept the way a stored
    /// ingredient lot does; BowlContents' own CoolTowardsAmbient already
    /// models the one time-based decay that matters for broth once it's in
    /// the bowl.
    ///
    /// Equipment: no IEquipmentDef content exists yet (Content agent's
    /// report: 0 equipment authored). heatRateMultiplier/capacityMultiplier
    /// are serialized fields defaulting to EquipmentModifiers.Default (1,1)
    /// so a later pass can wire real EquipmentData in without a code change.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class BrothPot : MonoBehaviour, IInteractable
    {
        [Header("Content")]
        [Tooltip("The one Broth-category ingredient in the generated content.")]
        [SerializeField] string brothIngredientId = "ing.broth_base";
        [Tooltip("Amount of brothIngredientId consumed from InventoryModel per start/refill trigger.")]
        [SerializeField] float fillPortionAmount = 1f;
        [Tooltip("Amount added to the bowl's Broth slot per ladle (matches the recipes' Broth RecipeComponent.amount of 1.0).")]
        [SerializeField] float ladleAmount = 1f;

        [Header("Equipment modifiers (no equipment content exists yet -- see class doc)")]
        [SerializeField] float heatRateMultiplier = 1f;
        [SerializeField] float capacityMultiplier = 1f;

        InventoryModel _inventory;
        IBalanceConfig _balance;
        IEventBus _events;
        bool _bound;

        BrothState _state;
        bool _started;
        bool _readyEventFired;

        /// <summary>Read access for a future HUD/debug display of pot state.</summary>
        public BrothState State => _state;

        /// <summary>
        /// Injection seam -- see IngredientStation.Bind for the same pattern
        /// and rationale. Inert (no ticking, CanInteract false, no throws)
        /// until called.
        /// </summary>
        public void Bind(InventoryModel inventory, IBalanceConfig balance, IEventBus events)
        {
            _inventory = inventory;
            _balance = balance;
            _events = events;
            _bound = inventory != null && balance != null;
        }

        void Update()
        {
            if (!_bound || !_started) return;

            var wasReady = _state.Phase == BrothPhase.Ready;
            var mods = new EquipmentModifiers(heatRateMultiplier, capacityMultiplier);
            BrothSimulator.Tick(ref _state, Time.deltaTime, mods, _balance);

            if (!wasReady && _state.Phase == BrothPhase.Ready && !_readyEventFired)
            {
                _readyEventFired = true;
                _events?.Publish(new BrothReady(_state.Quality01));
            }
        }

        public string GetInteractionText(in InteractionContext ctx)
        {
            if (!_bound) return string.Empty;

            if (ctx.Agent?.Held is BowlObject)
            {
                return _state.Phase == BrothPhase.Ready ? "Press E to ladle broth" : "Broth not ready";
            }

            switch (_state.Phase)
            {
                case BrothPhase.Empty: return "Press E to start broth";
                case BrothPhase.Overcooked: return "Press E to start a fresh batch";
                default: return "Broth is cooking";
            }
        }

        public bool CanInteract(in InteractionContext ctx)
        {
            if (!_bound || ctx.Agent == null) return false;

            if (ctx.Agent.Held is BowlObject bowl)
            {
                return _state.Phase == BrothPhase.Ready && !bowl.Contents.IsSealed;
            }

            // Hands free (or holding something other than a bowl): only a
            // start/refill trigger, and only when the pot actually needs a
            // fresh batch.
            return _state.Phase == BrothPhase.Empty || _state.Phase == BrothPhase.Overcooked;
        }

        public void Interact(in InteractionContext ctx)
        {
            if (!_bound || ctx.Agent == null) return;

            if (ctx.Agent.Held is BowlObject bowl)
            {
                if (_state.Phase != BrothPhase.Ready || bowl.Contents.IsSealed) return;

                bowl.AddIngredient(
                    ComponentSlot.Broth,
                    new IngredientId(brothIngredientId),
                    ladleAmount,
                    quality01: _state.Quality01,
                    freshness01: 1f,
                    nowSeconds: Time.time);
                return;
            }

            if (_state.Phase != BrothPhase.Empty && _state.Phase != BrothPhase.Overcooked) return;

            var id = new IngredientId(brothIngredientId);
            if (!_inventory.TryConsume(id, fillPortionAmount, out _)) return;

            _state = default; // fresh BrothState -- Phase defaults to BrothPhase.Empty (enum value 0).
            _readyEventFired = false;
            _started = true;
        }
    }
}
