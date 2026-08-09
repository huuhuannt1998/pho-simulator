using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Pho.Core;
using Pho.Core.DayCycle;
using Pho.Customers;
using Pho.Domain.Contracts;

namespace Pho.PlayTests
{
    /// <summary>
    /// Proves customers can actually ARRIVE in the real generated scene.
    ///
    /// WHY THIS EXISTS: VerticalSliceGoldenPathTest drives orders straight
    /// through OrderService, which is the right call for that test but means
    /// it never asks a customer to exist. That blind spot let a total
    /// showstopper ship green: the generated GameDatabase carried
    /// `archetypes: []`, and CustomerSpawner.TrySpawn bails with a warning
    /// when the archetype list is empty -- so no customer ever spawned in
    /// play, making GDD §55 steps 7-11 unreachable by a human while every
    /// automated test passed. This suite covers the arrival path end to end
    /// so that class of content gap can't silently return.
    /// </summary>
    public class CustomerSpawnPlayTests
    {
        [UnityTest]
        public IEnumerator Database_HasCustomerArchetypes_OtherwiseNobodyCanEverSpawn()
        {
            SceneManager.LoadScene("Boot", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var ctx = GameBootstrap.Current;
            Assert.That(ctx, Is.Not.Null);
            Assert.That(ctx.TryGet<IGameDatabase>(out var database), Is.True);

            Assert.That(database.Archetypes, Is.Not.Null);
            Assert.That(database.Archetypes.Count, Is.GreaterThan(0),
                "GameDatabase has zero customer archetypes -- CustomerSpawner will refuse to spawn anyone and the whole customer half of the game is unreachable. Re-run 'Pho/Content/Build All Content'.");
        }

        [UnityTest]
        public IEnumerator Spawner_WhileOpen_ActuallyInstantiatesABoundCustomer()
        {
            SceneManager.LoadScene("Boot", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var ctx = GameBootstrap.Current;
            Assert.That(ctx, Is.Not.Null);
            Assert.That(ctx.TryGet<RestaurantStateServiceBehaviour>(out var restaurant), Is.True);

            var spawner = Object.FindFirstObjectByType<CustomerSpawner>();
            Assert.That(spawner, Is.Not.Null, "No CustomerSpawner in the scene.");

            int before = Object.FindObjectsByType<CustomerAgent>(FindObjectsSortMode.None).Length;

            // The spawner refuses to spawn while closed (customers arriving
            // during Prep would burn their patience before the player could
            // possibly cook) -- so open first, exactly as a player would at
            // the restaurant sign.
            restaurant.OpenRestaurant();
            yield return null;

            // Drive the spawn directly instead of waiting out the real
            // spawnIntervalSeconds -- same fast-forward principle the golden
            // path uses for BrothPot/DayClock.
            var trySpawn = typeof(CustomerSpawner).GetMethod("TrySpawn", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(trySpawn, Is.Not.Null, "CustomerSpawner.TrySpawn not found -- update this test's reflection target.");
            trySpawn.Invoke(spawner, null);
            yield return null;

            var agents = Object.FindObjectsByType<CustomerAgent>(FindObjectsSortMode.None);
            Assert.That(agents.Length, Is.EqualTo(before + 1),
                "No CustomerAgent was instantiated. Most likely causes: the Customer prefab isn't assigned on the spawner, or GameDatabase has no archetypes.");

            var spawned = agents[agents.Length - 1];
            var boundField = typeof(CustomerAgent).GetField("_bound", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(boundField, Is.Not.Null);
            Assert.That((bool)boundField.GetValue(spawned), Is.True,
                "The customer spawned but was never bound -- it would stand inert forever.");
        }

        [UnityTest]
        public IEnumerator Spawner_WhileClosed_SpawnsNobody()
        {
            SceneManager.LoadScene("Boot", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var spawner = Object.FindFirstObjectByType<CustomerSpawner>();
            Assert.That(spawner, Is.Not.Null);

            int before = Object.FindObjectsByType<CustomerAgent>(FindObjectsSortMode.None).Length;

            // Never opened -- still in Prep. Run well past spawnIntervalSeconds
            // worth of Update calls; nobody should appear.
            for (int i = 0; i < 5; i++) yield return null;

            Assert.That(Object.FindObjectsByType<CustomerAgent>(FindObjectsSortMode.None).Length, Is.EqualTo(before),
                "A customer arrived while the restaurant was still in Prep -- they would burn their patience before the player could cook anything.");
        }
    }
}
