using System;
using System.Linq;
using System.Reflection;
using Pho.Core;
using Pho.Data;
using Pho.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Pho.EditorTools
{
    /// <summary>
    /// Generates Assets/Scenes/Boot.unity -- a minimal playable scene
    /// (ground + light + player + GameBootstrap + baked NavMesh). This is
    /// the ONLY code path allowed to create or modify that scene; per
    /// architecture.md section 1, Assets/Scenes/ is GENERATED, not
    /// hand-edited.
    ///
    /// Idempotency strategy: FULL REBUILD, not in-place patch (option (b)
    /// from the brief, chosen for simplicity and because it makes
    /// architecture.md section 10's "on conflict: git checkout --ours,
    /// re-run the builder" recovery path trivially correct -- there is no
    /// stale prior-shape state to reconcile). Concretely: every run starts
    /// from a brand-new empty in-memory scene (<see cref="EditorSceneManager.NewScene"/>)
    /// and rebuilds every element from scratch, then overwrites
    /// Assets/Scenes/Boot.unity in place via SaveScene. Overwriting the
    /// existing file (rather than deleting the asset and recreating it) is
    /// deliberate: it leaves the .meta file, and therefore the scene
    /// asset's GUID, untouched across reruns, which matters because
    /// EditorBuildSettings.scenes references scenes by GUID.
    ///
    /// Invoked headlessly via:
    ///   Unity -batchmode -nographics -projectPath . \
    ///     -executeMethod Pho.EditorTools.SceneBuilder.BuildBootScene -quit
    ///
    /// This script itself never launches Unity or runs in batch mode -- it
    /// is only ever invoked BY Unity, by a separate integration pass.
    /// </summary>
    public static class SceneBuilder
    {
        const string ScenesFolder = "Assets/Scenes";
        const string BootScenePath = ScenesFolder + "/Boot.unity";

        const string GameDatabasePath = "Assets/Content/GameDatabase.asset";
        const string GameBalanceConfigPath = "Assets/Content/GameBalanceConfig.asset";

        const string GroundName = "Ground";
        const string SunName = "Sun";
        const string PlayerName = "Player";
        const string CameraPivotName = "CameraPivot";
        const string GameManagerName = "GameManager";

        // Default Plane primitive is 10x10 Unity units at scale (1,1,1);
        // scaled to (10,1,10) that's a 100x100 walkable floor -- plenty of
        // room, no real art needed yet.
        static readonly Vector3 GroundScale = new Vector3(10f, 1f, 10f);

        // Default CharacterController is height 2, radius 0.5, center
        // (0,0,0) local -- so the capsule extends from local y=-1 to y=+1.
        // Spawning the root at world y=1 rests the capsule's feet exactly
        // on the ground plane (world y=0).
        static readonly Vector3 PlayerSpawnPosition = new Vector3(0f, 1f, -2f);
        const float CameraPivotLocalHeight = 0.7f; // ~eye height within the capsule

        // NavMeshSurface lives in the com.unity.ai.navigation package's
        // "Unity.AI.Navigation" runtime assembly, which Pho.EditorTools.asmdef
        // does not currently reference (this pass's ownership is scoped to
        // this file, GameBootstrap.cs, and Assets/Scenes/** only --
        // architecture.md section 10 treats asmdef reference changes as an
        // integration-agent escalation, not something to fold into an
        // unrelated content-generation pass). Resolved and invoked by
        // reflection instead of AddComponent<NavMeshSurface>() --
        // functionally identical, just without a compile-time reference.
        // If/when Pho.EditorTools.asmdef picks up an explicit
        // "Unity.AI.Navigation" reference, this can become a plain typed
        // call.
        const string NavMeshSurfaceAssemblyQualifiedName = "Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation";
        const string BuildNavMeshMethodName = "BuildNavMesh";

        [MenuItem("Pho/Scenes/Build Boot Scene")]
        public static void BuildBootScene()
        {
            EnsureFolder();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var ground = BuildGround();
            BuildSun();
            BuildPlayer();
            BuildGameManager();
            BakeNavMesh(ground);

            bool saved = EditorSceneManager.SaveScene(scene, BootScenePath);
            if (!saved)
            {
                Debug.LogError($"[SceneBuilder] Failed to save scene to '{BootScenePath}'.");
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            RegisterInBuildSettings();

            Debug.Log($"[SceneBuilder] Built '{BootScenePath}' -- ground, sun, player @ {PlayerSpawnPosition}, GameBootstrap, baked NavMesh.");
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(ScenesFolder))
                AssetDatabase.CreateFolder("Assets", "Scenes");
        }

        static GameObject BuildGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = GroundName;
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = GroundScale;
            // CreatePrimitive(Plane) already adds a MeshCollider -- walkable
            // out of the box. No physics layer scheme exists yet (see
            // PlayerInteractor's own "Interactable" layer mask comment), so
            // this deliberately stays on the Default layer rather than
            // inventing one.
            return ground;
        }

        static void BuildSun()
        {
            var sunGo = new GameObject(SunName);
            sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var light = sunGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = Color.white;
            light.shadows = LightShadows.Soft;
        }

        static void BuildPlayer()
        {
            var player = new GameObject(PlayerName);
            player.transform.position = PlayerSpawnPosition;

            // CharacterController defaults (height 2, radius 0.5, center
            // (0,0,0)) are fine for a vertical-slice human capsule.
            player.AddComponent<CharacterController>();

            var motor = player.AddComponent<FirstPersonMotor>();
            player.AddComponent<PlayerInteractor>();

            var cameraPivot = new GameObject(CameraPivotName);
            cameraPivot.transform.SetParent(player.transform, false);
            cameraPivot.transform.localPosition = new Vector3(0f, CameraPivotLocalHeight, 0f);
            cameraPivot.tag = "MainCamera";
            cameraPivot.AddComponent<Camera>();
            cameraPivot.AddComponent<AudioListener>();

            // FirstPersonMotor.cameraPivot has NO Awake-time auto-resolution
            // (only its `controller` field does -- confirmed by reading
            // FirstPersonMotor.cs), so mouse-look pitch has nowhere to apply
            // to unless this is wired explicitly.
            SetSerializedField(motor, "cameraPivot", cameraPivot.transform);

            // PlayerInteractor.eye auto-resolves via
            // GetComponentInChildren<Camera>() in Awake when left empty,
            // and holdAnchor auto-creates under eye when left empty --
            // both confirmed by reading PlayerInteractor.cs -- so neither
            // needs wiring here. interactableMask is deliberately left at
            // its serialized default (no layers) since the project doesn't
            // have an "Interactable" physics layer set up yet either
            // (PlayerInteractor's own tooltip says as much); wiring it to
            // something meaningful is a later pass's job once that layer
            // exists.
        }

        static void BuildGameManager()
        {
            var go = new GameObject(GameManagerName);
            var bootstrap = go.AddComponent<GameBootstrap>();

            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(GameDatabasePath);
            if (database != null)
            {
                SetSerializedField(bootstrap, "gameDatabase", database);
            }
            else
            {
                Debug.LogWarning($"[SceneBuilder] Could not load '{GameDatabasePath}' -- GameBootstrap.gameDatabase left unassigned. Run ContentGenerator first.");
            }

            var balance = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(GameBalanceConfigPath);
            if (balance != null)
            {
                SetSerializedField(bootstrap, "balanceConfig", balance);
            }
            else
            {
                Debug.LogWarning($"[SceneBuilder] Could not load '{GameBalanceConfigPath}' -- GameBootstrap.balanceConfig left unassigned. Run ContentGenerator first.");
            }
        }

        static void BakeNavMesh(GameObject ground)
        {
            var surfaceType = Type.GetType(NavMeshSurfaceAssemblyQualifiedName);
            if (surfaceType == null)
            {
                Debug.LogWarning("[SceneBuilder] Could not resolve Unity.AI.Navigation.NavMeshSurface -- is com.unity.ai.navigation installed? Skipping NavMesh bake.");
                return;
            }

            var surface = ground.AddComponent(surfaceType);

            var buildMethod = surfaceType.GetMethod(BuildNavMeshMethodName, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (buildMethod == null)
            {
                Debug.LogWarning($"[SceneBuilder] NavMeshSurface has no public parameterless {BuildNavMeshMethodName}() -- component was added but the mesh was not baked.");
                return;
            }

            buildMethod.Invoke(surface, null);
        }

        static void RegisterInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == BootScenePath)) return;

            scenes.Add(new EditorBuildSettingsScene(BootScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>
        /// Assigns a private [SerializeField] via SerializedObject -- the
        /// standard editor-tooling way to wire a field a component doesn't
        /// expose a public setter for, without touching that component's
        /// source (both FirstPersonMotor and GameBootstrap are outside this
        /// pass's ownership/API contract).
        /// </summary>
        static void SetSerializedField(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"[SceneBuilder] Could not find serialized field '{fieldName}' on {target.GetType().Name} -- skipping wire-up.");
                return;
            }

            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
