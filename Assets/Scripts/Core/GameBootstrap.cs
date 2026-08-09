using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Pho.Data;
using Pho.Domain.Contracts;
using Pho.Domain.Events;
using UnityEngine;

namespace Pho.Core
{
    /// <summary>
    /// The composition-root bootstrap. Lives once per boot-capable scene
    /// (Boot.unity today; any future menu/restaurant scene later). On
    /// Awake():
    ///   1. Builds a real EventBus.
    ///   2. Builds the GameContext around it.
    ///   3. Registers the scene's content assets (GameDatabase,
    ///      GameBalanceConfig) into the context, if wired.
    ///   4. Reflects over every loaded assembly for concrete,
    ///      [AutoInstall]-tagged IInstaller types, sorts by Order, and
    ///      installs them.
    ///   5. Binds the scene's PlayerInteractor, if one exists.
    ///
    /// This is the "Bootstrap wiring" row of architecture.md section 10:
    /// adding an installer is a code-only change in some other assembly --
    /// zero edits to this file, zero scene edits. There are no concrete
    /// [AutoInstall] installers yet (later milestones add them); the scan
    /// finding zero today is expected, not an error.
    ///
    /// Singleton note: GameContext.cs is deliberately not a singleton and
    /// its own doc comment warns every system will want to smuggle a field
    /// onto it. This class's static <see cref="Current"/> is the one
    /// narrow, deliberate exception the architecture doc's engineering
    /// principles allow: a single well-known access point for the ONE root
    /// composition object a scene-based game needs, not a general
    /// service-locator pattern. Do not add more static state here.
    ///
    /// Assembly-layering note: PlayerInteractor lives in Pho.Player, which
    /// (per Pho.Player.asmdef) already depends on Pho.Core. Pho.Core cannot
    /// reference Pho.Player back -- Unity refuses to compile a circular
    /// asmdef reference -- so the player bind below resolves and invokes
    /// PlayerInteractor.Bind(GameContext) by reflection instead of a typed
    /// FindFirstObjectByType&lt;PlayerInteractor&gt;() call. This is the only
    /// asmdef-legal way for Core-side code to reach a Player-side
    /// component without an architecture-level asmdef change.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        const string PlayerInteractorTypeName = "Pho.Player.PlayerInteractor";
        const string BindMethodName = "Bind";

        [Header("Content (see Assets/Content/*.asset)")]
        [Tooltip("Assets/Content/GameDatabase.asset. Assigned by SceneBuilder when it generates the scene. Optional here -- a boot-capable scene without content (e.g. a future main menu) logs a warning instead of crashing.")]
        [SerializeField] GameDatabase gameDatabase;

        [Tooltip("Assets/Content/GameBalanceConfig.asset. Same optionality note as gameDatabase.")]
        [SerializeField] GameBalanceConfig balanceConfig;

        /// <summary>
        /// The one well-known access point for this scene's composition
        /// root. See the singleton note in the class doc comment before
        /// reaching for this outside of "I am code that runs after the
        /// boot scene has loaded and needs the root context."
        /// </summary>
        public static GameContext Current { get; private set; }

        void Awake()
        {
            var eventBus = new EventBus(onHandlerException: Debug.LogException);
            var ctx = new GameContext(eventBus);
            Current = ctx;

            RegisterContent(ctx);
            RunInstallers(ctx);
            BindPlayer(ctx);
        }

        void RegisterContent(GameContext ctx)
        {
            if (gameDatabase != null)
            {
                ctx.Register<IGameDatabase>(gameDatabase);
            }
            else
            {
                Debug.LogWarning("[GameBootstrap] No GameDatabase assigned -- content-dependent services will be unavailable in this scene.");
            }

            if (balanceConfig != null)
            {
                ctx.Register<IBalanceConfig>(balanceConfig);
            }
            else
            {
                Debug.LogWarning("[GameBootstrap] No GameBalanceConfig assigned -- balance-dependent services will be unavailable in this scene.");
            }
        }

        void RunInstallers(GameContext ctx)
        {
            var installers = DiscoverInstallers();
            installers.Sort((a, b) => a.Order.CompareTo(b.Order));

            foreach (var installer in installers)
            {
                try
                {
                    installer.Install(ctx);
                }
                catch (Exception ex)
                {
                    // Same "one bad participant doesn't take down the whole
                    // bootstrap" philosophy as the discovery scan below.
                    Debug.LogException(ex);
                    Debug.LogWarning($"[GameBootstrap] Installer '{installer.GetType().FullName}' threw during Install -- continuing with the remaining installers.");
                }
            }

            if (installers.Count > 0)
            {
                Debug.Log($"[GameBootstrap] Ran {installers.Count} installer(s).");
            }
        }

        static List<IInstaller> DiscoverInstallers()
        {
            var installers = new List<IInstaller>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // Some assemblies partially fail to load their types in
                    // edge cases. Salvage whatever DID load rather than
                    // aborting discovery for every other assembly.
                    types = ex.Types.Where(t => t != null).ToArray();
                    Debug.LogWarning($"[GameBootstrap] Partial type load for assembly '{assembly.GetName().Name}' -- continuing with {types.Length} type(s) that did load.");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[GameBootstrap] Skipping assembly '{assembly.GetName().Name}' during installer scan: {ex.Message}");
                    continue;
                }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(IInstaller).IsAssignableFrom(type)) continue;
                    if (type.GetCustomAttribute<AutoInstallAttribute>() == null) continue;

                    var ctor = type.GetConstructor(Type.EmptyTypes);
                    if (ctor == null)
                    {
                        Debug.LogWarning($"[GameBootstrap] Installer '{type.FullName}' has [AutoInstall] but no parameterless constructor -- skipped.");
                        continue;
                    }

                    try
                    {
                        installers.Add((IInstaller)Activator.CreateInstance(type));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[GameBootstrap] Failed to instantiate installer '{type.FullName}': {ex.Message}");
                    }
                }
            }

            return installers;
        }

        static void BindPlayer(GameContext ctx)
        {
            try
            {
                MonoBehaviour interactor = null;
                foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                {
                    if (behaviour != null && behaviour.GetType().FullName == PlayerInteractorTypeName)
                    {
                        interactor = behaviour;
                        break;
                    }
                }

                if (interactor == null)
                {
                    // Not every boot-capable scene has a player (a future
                    // menu scene, for instance) -- this is a warning, not a
                    // failure.
                    Debug.LogWarning("[GameBootstrap] No PlayerInteractor found in the scene -- skipping player bind.");
                    return;
                }

                var bindMethod = interactor.GetType().GetMethod(BindMethodName, BindingFlags.Public | BindingFlags.Instance);
                if (bindMethod == null)
                {
                    Debug.LogWarning($"[GameBootstrap] Found a {PlayerInteractorTypeName} but it has no public {BindMethodName}(GameContext) method -- skipping.");
                    return;
                }

                bindMethod.Invoke(interactor, new object[] { ctx });
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Debug.LogWarning("[GameBootstrap] Player bind failed -- continuing bootstrap without it.");
            }
        }
    }
}
