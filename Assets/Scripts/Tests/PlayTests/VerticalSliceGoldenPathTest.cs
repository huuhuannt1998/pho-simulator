using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Pho.Core;
using Pho.Core.DayCycle;
using Pho.Core.Economy;
using Pho.Core.Interaction;
using Pho.Core.Orders;
using Pho.Core.Progression;
using Pho.Core.Restaurant;
using Pho.Core.Save;
using Pho.Domain.Contracts;
using Pho.Domain.Cooking;
using Pho.Domain.DayCycle;
using Pho.Domain.Events;
using Pho.Domain.Identity;
using Pho.Domain.Orders;
using Pho.Kitchen;
using Pho.UI.Presenters;

namespace Pho.PlayTests
{
    /// <summary>
    /// The GDD §55 acceptance test -- headless, against the REAL generated
    /// Boot.unity scene (not a hand-built test scene), driving REAL
    /// production services (GameBootstrap, KitchenBindInstaller,
    /// CustomerBindInstaller, InventoryService, EconomyService, OrderService,
    /// RestaurantStateServiceBehaviour, GameSaveService) through every
    /// interface a player/customer would actually use
    /// (IIngredientStation/BrothPot/PassCounter's IInteractable, not private
    /// back doors), per architecture.md §11's Tier 3 definition. This is the
    /// single most important test in the project: everything else can be
    /// individually green while the systems still don't fit together, and
    /// this is the one thing that proves they do.
    ///
    /// SCOPE, read before "fixing" a skipped step -- 14 of the 16 steps are
    /// exercised. The two that are not are an approved scope cut, not a gap:
    /// <list type="bullet">
    /// <item><b>Step 2 (buys ingredients) / Step 3 (returns to restaurant):</b>
    /// architecture.md §12's GDD-deviations table cuts the wet-market
    /// shopping trip entirely for the vertical slice ("Cut the second
    /// location... Street/market is post-VS"). InventoryService seeds
    /// starting stock directly instead.</item>
    /// </list>
    /// (Steps 12 and 14 were previously skipped for want of a cleaning
    /// mechanic and an upgrade system respectively; both now exist and are
    /// asserted below.)
    /// Steps 7-8 (customer spawns/orders) are driven directly through the
    /// real <see cref="OrderService"/> rather than waiting on a live
    /// NavMesh-driven <c>CustomerAgent</c> to physically walk, queue, and
    /// sit -- that full physical loop is <c>CustomerLoopPlayTests</c>'s job
    /// per architecture.md §11's own testing-tier table, and re-doing it
    /// here would make this test slow and NavMesh-timing-flaky for no
    /// additional wiring coverage (CustomerAgent.PlaceOrder/Pay already just
    /// delegate to these same OrderService/EconomyService calls once bound
    /// -- see CustomerBindInstaller). CustomerSpawner/CustomerBindInstaller
    /// themselves ARE verified directly (a spawner exists and is bound).
    ///
    /// BrothPot simmering and DayClock's day-length wraparound are advanced
    /// by calling their own real, pure Tick functions directly with large dt
    /// values (exactly what Update() calls every frame, just not spread
    /// across real wall-clock time) rather than yielding hundreds of real
    /// frames -- matches architecture.md §11's "Time is driven by an
    /// injected IClock so tests fast-forward" intent; neither BrothPot nor
    /// RestaurantStateServiceBehaviour actually took an injected IClock when
    /// built (both read Time.deltaTime directly), a known gap noted here
    /// rather than silently worked around.
    /// </summary>
    public class VerticalSliceGoldenPathTest
    {
        const string SaveSlot = "golden-path-test";

        [UnityTest]
        public IEnumerator FullGoldenPathLoop_AllReachableSteps_WireTogetherCorrectly()
        {
            // ---- Step 1: Player starts a new game. ----
            SceneManager.LoadScene("Boot", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var ctx = GameBootstrap.Current;
            Assert.That(ctx, Is.Not.Null, "GameBootstrap did not run -- Boot.unity is not wired.");

            Assert.That(ctx.TryGet<IBalanceConfig>(out var balance), Is.True, "No IBalanceConfig registered -- content was not built (run 'Pho/Content/Generate All' before this test).");
            Assert.That(ctx.TryGet<IGameDatabase>(out var database), Is.True, "No IGameDatabase registered -- content was not built.");
            Assert.That(ctx.TryGet<EconomyService>(out var economy), Is.True);
            Assert.That(ctx.TryGet<OrderService>(out var orderService), Is.True);
            Assert.That(ctx.TryGet<RestaurantStateServiceBehaviour>(out var restaurantState), Is.True);
            Assert.That(ctx.TryGet<GameSaveService>(out var saveService), Is.True);

            Assert.That(economy.Cash, Is.EqualTo(EconomyService.StartingCash), "New game must start with the GDD's starting cash.");
            Assert.That(restaurantState.Phase, Is.EqualTo(DayPhase.Prep), "A new day always starts in Prep.");

            // ---- Steps 2-3: SKIPPED -- approved GDD deviation, see class doc comment. ----
            // InventoryService has already seeded starting stock during Install(),
            // which is this vertical slice's stand-in for "player buys ingredients".

            var fakeHands = new FakeInteractorAgent();

            // ---- Step 5 (started early, deliberately -- see class doc comment): cook broth. ----
            var brothPotGo = GameObject.Find("BrothPot");
            Assert.That(brothPotGo, Is.Not.Null, "BrothPot not found -- was Boot.unity built by SceneBuilder?");
            var brothPot = brothPotGo.GetComponent<BrothPot>();

            var startBrothCtx = new InteractionContext(fakeHands, default, InteractionVerb.Primary);
            Assert.That(brothPot.CanInteract(startBrothCtx), Is.True, "BrothPot should be bindable/interactable hands-free to start a batch -- if this fails, KitchenBindInstaller did not bind it.");
            brothPot.Interact(startBrothCtx); // hands-free -> starts a fresh batch, consumes ing.broth_base

            AdvanceBrothToReady(brothPot, balance);
            Assert.That(brothPot.State.Phase, Is.EqualTo(BrothPhase.Ready), "Broth did not reach Ready after fast-forwarding BrothSimulator.Tick.");

            // ---- Step 4: Player prepares ingredients (physically walks between stations). ----
            var bowlGo = GameObject.Find("Bowl_1");
            Assert.That(bowlGo, Is.Not.Null, "No bowl found in the bowl stack -- was PrefabBuilder/SceneBuilder run?");
            var bowl = bowlGo.GetComponent<BowlObject>();

            var pickupCtx = new InteractionContext(fakeHands, default, InteractionVerb.Primary);
            Assert.That(bowl.CanInteract(pickupCtx), Is.True);
            bowl.Interact(pickupCtx); // ctx.Agent.TryTake(bowl)
            Assert.That(fakeHands.Held, Is.EqualTo(bowl));

            InteractWithStation("IngredientStation_ing.rice_noodles", fakeHands);
            InteractWithStation("IngredientStation_ing.beef_brisket", fakeHands); // Tái-specific protein

            // ---- Step 5 (ladle): finish assembling with the now-ready broth. ----
            var ladleCtx = new InteractionContext(fakeHands, default, InteractionVerb.Primary);
            Assert.That(brothPot.CanInteract(ladleCtx), Is.True, "BrothPot should accept a ladle now that it's Ready.");
            brothPot.Interact(ladleCtx);

            Assert.That(bowl.Contents.Components.Count, Is.EqualTo(3), "Expected noodle + protein + broth components in the bowl.");

            // ---- Step 6: Player opens the restaurant. ----
            restaurantState.OpenRestaurant();
            Assert.That(restaurantState.Phase, Is.EqualTo(DayPhase.Open));

            // ---- Steps 7-8: Customer spawns and orders (see class doc comment for scope). ----
            var spawnerGo = GameObject.Find("CustomerSpawner");
            Assert.That(spawnerGo, Is.Not.Null, "CustomerSpawner not found in the scene.");
            Assert.That(IsBound(spawnerGo.GetComponent(FindType("Pho.Customers.CustomerSpawner"))), Is.True,
                "CustomerSpawner exists but was never bound -- CustomerBindInstaller did not run/find it.");

            var customerId = new CustomerId(System.Guid.NewGuid().ToString());
            var recipe = new RecipeId("rec.pho_tai");
            var orderId = orderService.CreateOrder(customerId, "table.1", recipe, OrderModifiers.Default, Time.time);

            // ---- Step 9: Player cooks and serves the bowl (places it on the pass). ----
            var passCounterGo = GameObject.Find("PassCounter");
            Assert.That(passCounterGo, Is.Not.Null);
            var passCounter = passCounterGo.GetComponent<PassCounter>();

            var placeCtx = new InteractionContext(fakeHands, default, InteractionVerb.Primary);
            Assert.That(passCounter.CanInteract(placeCtx), Is.True, "PassCounter should accept the held, unsealed bowl.");
            passCounter.Interact(placeCtx); // seals + scores + publishes BowlAssembled -> OrderService.OnBowlAssembled fulfils orderId synchronously

            Assert.That(fakeHands.Held, Is.Null, "Hands should be empty after placing the bowl on the pass.");

            // ---- Step 10: Customer pays. ----
            Assert.That(orderService.TryTakeServedDish(orderId, out var servedDish), Is.True,
                "No served dish for this order -- BowlAssembled did not FIFO-match it (RecipeMatcher likely picked a different recipe than rec.pho_tai, or OrderService never received the event).");
            Assert.That(servedDish.Recipe, Is.EqualTo(recipe));
            Assert.That(servedDish.QuotedPrice, Is.GreaterThan(0m), "Quoted price should resolve from the real GameDatabase, not default to $0.");

            var cashBeforePay = economy.Cash;
            economy.Credit(servedDish.QuotedPrice, LedgerCategory.Sale); // mirrors CustomerAgent.Pay's real body exactly

            // ---- Step 11: Player earns money (and the HUD actually shows it). ----
            Assert.That(economy.Cash, Is.EqualTo(cashBeforePay + servedDish.QuotedPrice));
            Assert.That(economy.Cash, Is.GreaterThan(EconomyService.StartingCash), "Cash should have grown past the starting balance after a paid sale.");

            // The HUD presenters are live event subscribers, so by this point
            // they must already reflect real gameplay -- this is what proves
            // HudInstaller ran and bound them, not just that the classes exist.
            Assert.That(ctx.TryGet<CashPresenter>(out var cashPresenter), Is.True, "No CashPresenter registered -- HudInstaller did not run.");
            Assert.That(cashPresenter.HasReceivedUpdate, Is.True, "CashPresenter never observed a CashChanged -- it was constructed but never bound to the event bus.");
            Assert.That(cashPresenter.Cash, Is.EqualTo(economy.Cash), "HUD cash is out of sync with the real EconomyService balance.");

            Assert.That(ctx.TryGet<OrderBoardPresenter>(out var orderBoard), Is.True, "No OrderBoardPresenter registered -- HudInstaller did not run.");
            Assert.That(orderBoard.TryGetOrder(orderId, out var ticket), Is.True, "The order never reached the order board -- OrderBoardPresenter is not bound to OrderPlaced.");
            Assert.That(ticket.State, Is.EqualTo(OrderState.Completed), "Order board should have tracked this order all the way to Completed via OrderStateChanged.");

            // ---- Step 12: Player cleans. ----
            Assert.That(ctx.TryGet<CleanlinessService>(out var cleanliness), Is.True, "No CleanlinessService registered.");
            Assert.That(cleanliness.Cleanliness01, Is.EqualTo(1f).Within(1e-5f), "The dining room should start spotless.");

            // The scene's tables self-register via their DirtyTable
            // components, so a real table id is guaranteed to exist here.
            const string DirtiedTable = "table.1";
            Assert.That(cleanliness.IsKnownTable(DirtiedTable), Is.True,
                "table.1 never registered itself -- SceneBuilder did not put a DirtyTable component on the dining tables.");

            Assert.That(cleanliness.MarkDirty(DirtiedTable), Is.True);
            Assert.That(cleanliness.Cleanliness01, Is.LessThan(1f), "Dirtying a table must lower cleanliness.");

            Assert.That(cleanliness.TryClean(DirtiedTable), Is.True);
            Assert.That(cleanliness.Cleanliness01, Is.EqualTo(1f).Within(1e-5f), "Cleaning the only dirty table must restore a spotless room.");
            Assert.That(cleanliness.TryClean(DirtiedTable), Is.False, "Cleaning an already-clean table is a normal no-op, not a second success.");

            // ---- Step 13: Player closes the restaurant. ----
            restaurantState.CloseRestaurant();
            Assert.That(restaurantState.Phase, Is.EqualTo(DayPhase.Closed));

            // ---- Step 14: Player buys one upgrade. ----
            Assert.That(ctx.TryGet<ProgressionService>(out var progression), Is.True, "No ProgressionService registered.");

            var burner = new EquipmentId("eq.burner_commercial");
            Assert.That(progression.IsOwned(burner), Is.False, "Nothing should be owned at the start of a new game.");
            Assert.That(progression.CurrentModifiers.HeatRateMultiplier, Is.EqualTo(1f).Within(1e-5f), "Unupgraded equipment must be the neutral (1,1) modifier.");
            Assert.That(progression.TryGetCost(burner, out var burnerCost), Is.True, "Could not resolve the burner's cost -- is eq.burner_commercial in the GameDatabase?");

            var cashBeforeUpgrade = economy.Cash;
            Assert.That(progression.TryPurchase(burner), Is.True, "Purchase failed despite sufficient funds.");

            Assert.That(progression.IsOwned(burner), Is.True);
            Assert.That(economy.Cash, Is.EqualTo(cashBeforeUpgrade - burnerCost), "Buying the upgrade must debit exactly its cost.");

            // The progression proof that matters: owning it measurably
            // changes gameplay, not just a string in a save file.
            Assert.That(progression.CurrentModifiers.HeatRateMultiplier, Is.GreaterThan(1f),
                "Owning the commercial burner must produce a faster-than-default heat rate.");
            Assert.That(ProgressionService.CurrentModifiersOrDefault().HeatRateMultiplier, Is.GreaterThan(1f),
                "The static seam BrothPot reads must also reflect the purchase.");

            // ---- Step 15: Game saves. ----
            var cashAtSaveTime = economy.Cash;
            var dayAtSaveTime = restaurantState.Day;

            var written = saveService.Save(SaveSlot);
            Assert.That(written.economy.cash, Is.EqualTo(cashAtSaveTime));
            Assert.That(written.world.day, Is.EqualTo(dayAtSaveTime));
            Assert.That(written.world.phase, Is.EqualTo(DayPhase.Closed.ToString()));

            // ---- Step 16: Next day begins. ----
            AdvancePastDayBoundary(restaurantState, balance);
            Assert.That(restaurantState.Day, Is.EqualTo(dayAtSaveTime + 1), "Day should have advanced exactly once.");
            Assert.That(restaurantState.Phase, Is.EqualTo(DayPhase.Prep), "A newly-begun day must start in Prep, even though the previous day ended Closed.");

            // ---- Load proves Save captured a real, distinguishable snapshot ----
            // (not just "whatever the live state happens to be right now") --
            // the live state was already advanced to day+1/Prep above, so a
            // successful load must pull it back to the saved day/Closed.
            Assert.That(saveService.TryLoad(database, out var loaded, SaveSlot), Is.True, "Load failed -- SaveCoordinator reported no valid save (see any SaveCorrupted event).");
            Assert.That(loaded.world.day, Is.EqualTo(dayAtSaveTime));
            Assert.That(loaded.economy.cash, Is.EqualTo(cashAtSaveTime));
            Assert.That(restaurantState.Day, Is.EqualTo(dayAtSaveTime), "RestaurantStateServiceBehaviour.Restore should pull the live DayClock back to the saved day.");
            Assert.That(restaurantState.Phase, Is.EqualTo(DayPhase.Closed), "RestaurantStateServiceBehaviour.Restore should pull the live DayClock back to the saved (Closed) phase.");
            Assert.That(economy.Cash, Is.EqualTo(cashAtSaveTime), "EconomyService.Restore should pull cash back to the saved value.");
        }

        // ---- Helpers ----

        static void InteractWithStation(string gameObjectName, FakeInteractorAgent agent)
        {
            var go = GameObject.Find(gameObjectName);
            Assert.That(go, Is.Not.Null, $"Station '{gameObjectName}' not found -- was Boot.unity built by SceneBuilder with the expected ingredient stations?");

            var station = go.GetComponent<IngredientStation>();
            Assert.That(station, Is.Not.Null);

            var ctx = new InteractionContext(agent, default, InteractionVerb.Primary);
            Assert.That(station.CanInteract(ctx), Is.True, $"'{gameObjectName}' refused the interaction -- likely not bound (KitchenBindInstaller gap) or out of stock.");
            station.Interact(ctx);
        }

        /// <summary>
        /// Drives BrothSimulator.Tick directly (see class doc comment) until
        /// Ready. Each phase (Filling/Heating/Simmering) only advances ONE
        /// step per Tick call regardless of dt size -- BrothSimulator.Tick's
        /// switch statement transitions at most once per call -- so this
        /// calls Tick repeatedly (bounded) rather than assuming a single
        /// huge dt reaches Ready in one call.
        /// </summary>
        static void AdvanceBrothToReady(BrothPot pot, IBalanceConfig balance)
        {
            var stateField = typeof(BrothPot).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(stateField, Is.Not.Null, "BrothPot._state field not found -- has BrothPot been refactored? Update this test's reflection target.");

            const float bigDt = 1000f;
            const int maxSteps = 10; // Empty->Filling->Heating->Simmering->Ready is 4 transitions; generous safety margin.

            for (int i = 0; i < maxSteps; i++)
            {
                var state = (BrothState)stateField.GetValue(pot);
                if (state.Phase == BrothPhase.Ready) return;

                BrothSimulator.Tick(ref state, bigDt, EquipmentModifiers.Default, balance);
                stateField.SetValue(pot, state);
            }

            Assert.Fail($"BrothPot did not reach Ready within {maxSteps} Tick steps -- final phase was {((BrothState)stateField.GetValue(pot)).Phase}.");
        }

        /// <summary>Drives DayClock.Tick directly (see class doc comment) past one full day length.</summary>
        static void AdvancePastDayBoundary(RestaurantStateServiceBehaviour behaviour, IBalanceConfig balance)
        {
            var clockField = typeof(RestaurantStateServiceBehaviour).GetField("_clock", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(clockField, Is.Not.Null, "RestaurantStateServiceBehaviour._clock field not found -- update this test's reflection target.");

            var clock = (DayClock)clockField.GetValue(behaviour);
            clock.Tick(balance.DayLengthSeconds + 10f, balance); // DayClock.Tick's own while-loop safely handles a dt spanning more than one day.
        }

        static bool IsBound(Component component)
        {
            Assert.That(component, Is.Not.Null);
            var boundField = component.GetType().GetField("_bound", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(boundField, Is.Not.Null, $"{component.GetType().FullName} has no private '_bound' field -- update this test's reflection target.");
            return (bool)boundField.GetValue(component);
        }

        static System.Type FindType(string fullName)
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null) return type;
            }
            Assert.Fail($"Type '{fullName}' not found in any loaded assembly.");
            return null;
        }

        /// <summary>Minimal, direct IInteractorAgent -- deliberately not PlaceholderInteractorAgent/PlayerInteractor, so this test exercises station wiring without depending on physics raycasts or a real Camera/CharacterController.</summary>
        sealed class FakeInteractorAgent : IInteractorAgent
        {
            public IHoldable Held { get; private set; }
            public bool HasFreeHands => Held == null;
            public Transform HoldAnchor => null;

            public bool TryTake(IHoldable item)
            {
                if (Held != null || item == null) return false;
                Held = item;
                item.OnPickedUp(this);
                return true;
            }

            public bool TryGiveHeld(out IHoldable item)
            {
                item = Held;
                if (Held == null) return false;
                Held = null;
                return true;
            }
        }
    }
}
