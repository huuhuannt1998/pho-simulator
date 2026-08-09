using System.Reflection;
using Pho.Domain.Contracts;
using Pho.Domain.Inventory;
using UnityEngine;

namespace Pho.Core.Kitchen
{
    /// <summary>
    /// Binds every Kitchen-station MonoBehaviour placed in the scene
    /// (IngredientStation, BrothPot, PassCounter) to the shared
    /// InventoryModel/IBalanceConfig/IGameDatabase/IEventBus.
    ///
    /// GAP THIS CLOSES: SceneBuilder.BuildKitchenArea() only AddComponents
    /// these three types -- it never calls their Bind(...) (that seam is
    /// explicitly reserved for "a future integration/wiring pass", per each
    /// station's own class doc comment). Without this installer every
    /// kitchen station is permanently inert (Bind never called -> _bound
    /// stays false -> CanInteract always false -> empty prompts), which
    /// means the player could never actually cook anything -- found by
    /// tracing the real runtime wiring while building
    /// VerticalSliceGoldenPathTest, not assumed from the scene merely
    /// containing the right GameObjects.
    ///
    /// Reflection, not a typed reference (same reason/pattern as
    /// GameBootstrap.BindPlayer): Pho.Kitchen.asmdef already references
    /// Pho.Core, so Pho.Core cannot reference Pho.Kitchen back without a
    /// circular asmdef edge.
    ///
    /// Runs at InstallOrder.Kitchen (500) -- after Inventory (400) and
    /// DayCycle (450) so InventoryModel/IBalanceConfig are already
    /// registered, exactly the slot InstallOrder.cs reserves for this.
    /// </summary>
    [AutoInstall]
    public sealed class KitchenBindInstaller : IInstaller
    {
        const string IngredientStationTypeName = "Pho.Kitchen.IngredientStation";
        const string BrothPotTypeName = "Pho.Kitchen.BrothPot";
        const string PassCounterTypeName = "Pho.Kitchen.PassCounter";
        const string BindMethodName = "Bind";

        public int Order => InstallOrder.Kitchen;

        public void Install(GameContext ctx)
        {
            if (!ctx.TryGet<InventoryModel>(out var inventory))
            {
                Debug.LogWarning("[KitchenBindInstaller] No InventoryModel registered yet -- skipping kitchen station binding. Kitchen stations will remain inert in this scene.");
                return;
            }

            ctx.TryGet<IBalanceConfig>(out var balance);
            ctx.TryGet<IGameDatabase>(out var database);
            var events = ctx.Events;

            int bound = 0;
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour == null) continue;

                var typeName = behaviour.GetType().FullName;
                if (typeName == IngredientStationTypeName)
                {
                    if (InvokeBind(behaviour, inventory)) bound++;
                }
                else if (typeName == BrothPotTypeName)
                {
                    if (InvokeBind(behaviour, inventory, balance, events)) bound++;
                }
                else if (typeName == PassCounterTypeName)
                {
                    if (InvokeBind(behaviour, database, balance, events)) bound++;
                }
            }

            if (bound > 0)
            {
                Debug.Log($"[KitchenBindInstaller] Bound {bound} kitchen station(s).");
            }
        }

        static bool InvokeBind(MonoBehaviour behaviour, params object[] args)
        {
            var method = behaviour.GetType().GetMethod(BindMethodName, BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
            {
                Debug.LogWarning($"[KitchenBindInstaller] {behaviour.GetType().FullName} has no public Bind(...) method -- skipped.");
                return false;
            }

            method.Invoke(behaviour, args);
            return true;
        }
    }
}
