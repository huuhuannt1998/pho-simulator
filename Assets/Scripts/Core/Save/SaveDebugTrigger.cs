using Pho.Domain.Contracts;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pho.Core.Save
{
    /// <summary>
    /// Minimal, vertical-slice-only way to actually exercise save/load:
    /// F5 saves to the default slot, F9 loads it. No UI, no confirmation --
    /// a real save/load menu is out of scope for this pass (see the class
    /// doc comments across Pho.Save for the "no shared save-wiring file"
    /// design this sits on top of). Resolves GameSaveService lazily via the
    /// one deliberate GameBootstrap.Current singleton exception, same
    /// pattern as RestaurantSign.
    /// </summary>
    public sealed class SaveDebugTrigger : MonoBehaviour
    {
        InputAction _saveAction;
        InputAction _loadAction;

        void Awake()
        {
            _saveAction = new InputAction(name: "DebugSave", type: InputActionType.Button, binding: "<Keyboard>/f5");
            _loadAction = new InputAction(name: "DebugLoad", type: InputActionType.Button, binding: "<Keyboard>/f9");
        }

        void OnEnable()
        {
            _saveAction?.Enable();
            _loadAction?.Enable();
        }

        void OnDisable()
        {
            _saveAction?.Disable();
            _loadAction?.Disable();
        }

        void OnDestroy()
        {
            _saveAction?.Dispose();
            _loadAction?.Dispose();
        }

        void Update()
        {
            if (_saveAction.WasPerformedThisFrame()) TrySave();
            if (_loadAction.WasPerformedThisFrame()) TryLoad();
        }

        void TrySave()
        {
            var ctx = GameBootstrap.Current;
            if (ctx == null || !ctx.TryGet<GameSaveService>(out var saveService))
            {
                Debug.LogWarning("[SaveDebugTrigger] No GameSaveService available -- is GameBootstrap set up in this scene?");
                return;
            }

            var save = saveService.Save();
            Debug.Log($"[SaveDebugTrigger] Saved -- day {save.world.day}, cash {save.economy.cash}, {save.inventory.lots.Count} lot(s).");
        }

        void TryLoad()
        {
            var ctx = GameBootstrap.Current;
            if (ctx == null || !ctx.TryGet<GameSaveService>(out var saveService))
            {
                Debug.LogWarning("[SaveDebugTrigger] No GameSaveService available -- is GameBootstrap set up in this scene?");
                return;
            }

            ctx.TryGet<IGameDatabase>(out var db); // optional -- Restore is expected to tolerate a null db

            if (saveService.TryLoad(db, out var save))
            {
                Debug.Log($"[SaveDebugTrigger] Loaded -- day {save.world.day}, cash {save.economy.cash}, {save.inventory.lots.Count} lot(s).");
            }
            else
            {
                Debug.LogWarning("[SaveDebugTrigger] Load failed -- no valid save found (see any SaveCorrupted event for details).");
            }
        }
    }
}
