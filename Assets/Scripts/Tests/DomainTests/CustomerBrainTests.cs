using System;
using System.Collections.Generic;
using NUnit.Framework;
using Pho.Domain.Contracts;
using Pho.Domain.Cooking;
using Pho.Domain.Customers;
using Pho.Domain.Events;
using Pho.Domain.Identity;
using Pho.Domain.Infra;
using Pho.Domain.Satisfaction;
using Pho.Domain.Tests.Fakes;

namespace Pho.Domain.Tests
{
    // Plain NUnit only, Assert.That(...) constraint syntax throughout --
    // same convention as EventBusTests (compiled both by dotnet test and by
    // Unity's EditMode runner; classic Assert.AreEqual/IsTrue is unavailable
    // under Unity's bundled NUnit).
    [TestFixture]
    public class CustomerBrainTests
    {
        static FakeCustomerArchetype MakeArchetype(
            FloatRange? patienceSeconds = null,
            FloatRange? eatDurationSeconds = null,
            List<RecipeId> preferredRecipes = null)
        {
            return new FakeCustomerArchetype
            {
                Id = new ArchetypeId("arc.test"),
                PatienceSeconds = patienceSeconds ?? new FloatRange(30f, 30f),
                Budget = new FloatRange(8f, 12f),
                QualityExpectation01 = 0.5f,
                CleanlinessSensitivity01 = 0.5f,
                ServiceSensitivity01 = 0.5f,
                PriceSensitivity01 = 0.5f,
                TipChance01 = 1f,
                TipFraction = 0.1f,
                ReviewChance01 = 0f,
                PreferredRecipeIds = preferredRecipes ?? new List<RecipeId> { new RecipeId("rec.pho_tai") },
                EatDurationSeconds = eatDurationSeconds ?? new FloatRange(1f, 1f),
            };
        }

        static void RunUntil(CustomerBrain brain, FakeCustomerWorld world, Func<bool> done, float dt = 0.25f, int maxTicks = 500)
        {
            int ticks = 0;
            while (!done() && ticks < maxTicks)
            {
                brain.Tick(dt, world);
                ticks++;
            }
            Assert.That(done(), Is.True, $"did not reach the expected condition within {maxTicks} ticks (stuck in {brain.State})");
        }

        static void RunToDespawned(CustomerBrain brain, FakeCustomerWorld world, float dt = 0.25f, int maxTicks = 500) =>
            RunUntil(brain, world, () => brain.State == CustomerState.Despawned, dt, maxTicks);

        [Test]
        public void HappyPath_FullLifecycle_SpawningToDespawned_ResultSetAndCorrect()
        {
            var archetype = MakeArchetype();
            var cfg = new FakeBalanceConfig();
            var dish = new ServedDish(
                recipe: new RecipeId("rec.pho_tai"),
                quality: new DishQuality(0.9f, 0.9f, 0.9f, 0.9f, 0.9f, 0.9f),
                accuracy01: 0.95f,
                quotedPrice: 10m);
            var world = new FakeCustomerWorld
            {
                RestaurantIsOpen = true,
                HasArrived = true,
                ClaimAttemptsRemaining = 0,
                TakeDishAttemptsRemaining = 0,
                DishToServe = dish,
                Cleanliness01 = 0.9f,
            };
            var bus = new EventBus();
            var brain = new CustomerBrain(new CustomerId("cus.happy"), archetype, cfg, bus, new SeededRandom(1));

            var visited = new HashSet<CustomerState> { brain.State };
            Assert.That(brain.State, Is.EqualTo(CustomerState.Spawning));

            int ticks = 0;
            while (brain.State != CustomerState.Despawned && ticks < 200)
            {
                brain.Tick(0.25f, world);
                ticks++;
                visited.Add(brain.State);

                // Checkpoint: order must be placed exactly once, at the
                // moment we first see WaitingForFood.
                if (brain.State == CustomerState.WaitingForFood && world.PlaceOrderCalls.Count == 1)
                {
                    Assert.That(world.PlaceOrderCalls[0].Recipe, Is.EqualTo(new RecipeId("rec.pho_tai")));
                    Assert.That(brain.Result, Is.Null, "no satisfaction result before the dish is served");
                }

                // Checkpoint: no payment/Result yet while still eating.
                if (brain.State == CustomerState.Eating)
                {
                    Assert.That(world.PayCalls.Count, Is.EqualTo(0));
                    Assert.That(brain.Result, Is.Null);
                }

                // Checkpoint: Result is populated by the time we leave.
                if (brain.State == CustomerState.Leaving)
                {
                    Assert.That(brain.Result, Is.Not.Null, "Result must be set on Paying, before Leaving");
                    Assert.That(world.PayCalls.Count, Is.EqualTo(1));
                    Assert.That(world.ReleaseCallCount, Is.EqualTo(1));
                }
            }

            Assert.That(brain.State, Is.EqualTo(CustomerState.Despawned));
            Assert.That(visited, Does.Contain(CustomerState.WalkingToEntrance));
            Assert.That(visited, Does.Contain(CustomerState.WalkingToSeat));
            Assert.That(visited, Does.Contain(CustomerState.Sitting));
            Assert.That(visited, Does.Contain(CustomerState.BrowsingMenu));
            Assert.That(visited, Does.Contain(CustomerState.ReadyToOrder));
            Assert.That(visited, Does.Contain(CustomerState.WaitingForFood));
            Assert.That(visited, Does.Contain(CustomerState.Eating));
            Assert.That(visited, Does.Contain(CustomerState.Paying));
            Assert.That(visited, Does.Contain(CustomerState.Leaving));

            Assert.That(world.MoveToCalls, Does.Contain(world.EntrancePosition));
            Assert.That(world.MoveToCalls, Does.Contain(world.ExitPosition));
            Assert.That(world.Despawned, Is.True);

            Assert.That(brain.Result, Is.Not.Null);
            var result = brain.Result.Value;
            Assert.That(result.Score0to100, Is.GreaterThan(50f), "high-quality, fast, clean service should score well");
            Assert.That(result.WillReturn, Is.True);
            Assert.That(result.Tip, Is.GreaterThan(0m));

            Assert.That(world.PayCalls[0].Amount, Is.EqualTo(10m));
            Assert.That(world.PayCalls[0].Tip, Is.EqualTo(result.Tip));
        }

        [Test]
        public void WaitingForFood_PatienceExpires_TransitionsToLeavingAngry_WithWorstCaseResult()
        {
            var archetype = MakeArchetype(patienceSeconds: new FloatRange(1f, 1f));
            var cfg = new FakeBalanceConfig(); // PatienceDecayRate = 1
            var world = new FakeCustomerWorld
            {
                RestaurantIsOpen = true,
                HasArrived = true,
                ClaimAttemptsRemaining = 0, // seated instantly -- isolates the WaitingForFood expiry
                AllowServeDish = false,     // food never arrives
            };
            var brain = new CustomerBrain(new CustomerId("cus.hangry"), archetype, cfg, new EventBus(), new SeededRandom(7));

            RunUntil(brain, world, () => brain.State == CustomerState.LeavingAngry, dt: 0.1f, maxTicks: 200);

            Assert.That(brain.Result, Is.Not.Null);
            var result = brain.Result.Value;
            Assert.That(result.Tier, Is.EqualTo(SatisfactionTier.Angry));
            Assert.That(result.Tip, Is.EqualTo(0m));
            Assert.That(result.WillReturn, Is.False);
            Assert.That(result.ReputationDelta, Is.LessThan(0f));
            Assert.That(world.ReleaseCallCount, Is.EqualTo(1), "a seat claimed before giving up must be released");

            RunToDespawned(brain, world, dt: 0.1f, maxTicks: 200);
            Assert.That(world.Despawned, Is.True);
            Assert.That(world.MoveToCalls, Does.Contain(world.ExitPosition));
        }

        [Test]
        public void Queuing_SeatNeverAvailable_GivesUp_TransitionsToLeavingAngry_WithoutEverHoldingASeat()
        {
            var archetype = MakeArchetype(patienceSeconds: new FloatRange(0.5f, 0.5f));
            var cfg = new FakeBalanceConfig();
            var world = new FakeCustomerWorld
            {
                RestaurantIsOpen = true,
                HasArrived = true,
                AllowSeatClaim = false, // no seat ever frees up
            };
            var brain = new CustomerBrain(new CustomerId("cus.queued"), archetype, cfg, new EventBus(), new SeededRandom(3));

            RunUntil(brain, world, () => brain.State == CustomerState.LeavingAngry, dt: 0.1f, maxTicks: 200);

            Assert.That(world.ClaimAttemptCount, Is.GreaterThan(0), "must have actually tried to claim a seat while queuing");
            Assert.That(world.ReleaseCallCount, Is.EqualTo(0), "never held a seat, so nothing to release");
            Assert.That(brain.Result, Is.Not.Null);
            Assert.That(brain.Result.Value.Tier, Is.EqualTo(SatisfactionTier.Angry));
            Assert.That(world.PlaceOrderCalls.Count, Is.EqualTo(0), "never got seated, so never got to order");
        }

        [Test]
        public void Queuing_SeatBecomesAvailableAfterSeveralAttempts_EventuallyProceeds()
        {
            var archetype = MakeArchetype(patienceSeconds: new FloatRange(60f, 60f));
            var cfg = new FakeBalanceConfig();
            var world = new FakeCustomerWorld
            {
                RestaurantIsOpen = true,
                HasArrived = true,
                ClaimAttemptsRemaining = 3, // fails 3 times, then succeeds
                AllowServeDish = false,     // stop the lifecycle once seated -- not the point of this test
            };
            var brain = new CustomerBrain(new CustomerId("cus.patient"), archetype, cfg, new EventBus(), new SeededRandom(5));

            RunUntil(brain, world, () => brain.State == CustomerState.WalkingToSeat || brain.State == CustomerState.Sitting, dt: 0.25f, maxTicks: 200);

            Assert.That(world.ClaimAttemptCount, Is.GreaterThanOrEqualTo(4));
            Assert.That(world.SeatCurrentlyClaimed, Is.True);
        }

        [Test]
        public void ReadyToOrder_NoPreferredRecipes_LeavesGracefully_WithoutOrdering()
        {
            var archetype = MakeArchetype(preferredRecipes: new List<RecipeId>());
            var cfg = new FakeBalanceConfig();
            var world = new FakeCustomerWorld
            {
                RestaurantIsOpen = true,
                HasArrived = true,
                ClaimAttemptsRemaining = 0,
            };
            var brain = new CustomerBrain(new CustomerId("cus.norecipe"), archetype, cfg, new EventBus(), new SeededRandom(11));

            RunToDespawned(brain, world, dt: 0.25f, maxTicks: 200);

            Assert.That(world.PlaceOrderCalls.Count, Is.EqualTo(0));
            Assert.That(world.Despawned, Is.True);
            // Never ordered, never served, never paid, never got angry --
            // Result is only set on Paying or LeavingAngry, neither reached.
            Assert.That(brain.Result, Is.Null);
        }

        [Test]
        public void SeededDeterminism_SameSeedAndScript_ProducesIdenticalResults()
        {
            var recipes = new List<RecipeId> { new RecipeId("rec.pho_tai"), new RecipeId("rec.pho_chin") };
            var dish = new ServedDish(
                recipe: new RecipeId("rec.pho_tai"),
                quality: new DishQuality(0.8f, 0.8f, 0.8f, 0.8f, 0.8f, 0.8f),
                accuracy01: 0.85f,
                quotedPrice: 9.5m);

            CustomerBrain BuildBrain(int seed, out FakeCustomerWorld world)
            {
                var archetype = MakeArchetype(
                    patienceSeconds: new FloatRange(20f, 40f),
                    eatDurationSeconds: new FloatRange(5f, 15f),
                    preferredRecipes: recipes);
                var cfg = new FakeBalanceConfig();
                world = new FakeCustomerWorld
                {
                    RestaurantIsOpen = true,
                    HasArrived = true,
                    ClaimAttemptsRemaining = 0,
                    TakeDishAttemptsRemaining = 0,
                    DishToServe = dish,
                    Cleanliness01 = 0.7f,
                };
                return new CustomerBrain(new CustomerId("cus.seeded"), archetype, cfg, new EventBus(), new SeededRandom(seed));
            }

            var brain1 = BuildBrain(12345, out var world1);
            var brain2 = BuildBrain(12345, out var world2);

            RunToDespawned(brain1, world1, dt: 0.2f, maxTicks: 500);
            RunToDespawned(brain2, world2, dt: 0.2f, maxTicks: 500);

            Assert.That(brain1.Result, Is.Not.Null);
            Assert.That(brain2.Result, Is.Not.Null);
            var r1 = brain1.Result.Value;
            var r2 = brain2.Result.Value;

            Assert.That(r2.Score0to100, Is.EqualTo(r1.Score0to100).Within(0.0001f));
            Assert.That(r2.Tier, Is.EqualTo(r1.Tier));
            Assert.That(r2.Tip, Is.EqualTo(r1.Tip));
            Assert.That(r2.ReputationDelta, Is.EqualTo(r1.ReputationDelta).Within(0.0001f));
            Assert.That(r2.WillReturn, Is.EqualTo(r1.WillReturn));

            Assert.That(world1.PlaceOrderCalls.Count, Is.EqualTo(1));
            Assert.That(world2.PlaceOrderCalls.Count, Is.EqualTo(1));
            Assert.That(world2.PlaceOrderCalls[0].Recipe, Is.EqualTo(world1.PlaceOrderCalls[0].Recipe),
                "same seed must pick the same preferred recipe");
        }

        [Test]
        public void Constructor_RejectsNullDependencies()
        {
            var archetype = MakeArchetype();
            var cfg = new FakeBalanceConfig();
            var bus = new EventBus();
            var rng = new SeededRandom(1);
            var id = new CustomerId("cus.null");

            Assert.That(() => new CustomerBrain(id, null, cfg, bus, rng), Throws.ArgumentNullException);
            Assert.That(() => new CustomerBrain(id, archetype, null, bus, rng), Throws.ArgumentNullException);
            Assert.That(() => new CustomerBrain(id, archetype, cfg, null, rng), Throws.ArgumentNullException);
            Assert.That(() => new CustomerBrain(id, archetype, cfg, bus, null), Throws.ArgumentNullException);
        }
    }
}
