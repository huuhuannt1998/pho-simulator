using Pho.Customers;
using Pho.Kitchen;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Pho.EditorTools
{
    /// <summary>
    /// Generates Assets/Prefabs/Customer.prefab and Assets/Prefabs/Bowl.prefab
    /// -- grey-box stand-in prefabs (no real character/prop art exists yet,
    /// per docs/architecture.md's "primitives ship first" principle). This is
    /// one of the ONLY code paths allowed to create or modify prefabs under
    /// Assets/Prefabs/ (alongside SceneBuilder.cs, which instances these
    /// prefabs into Boot.unity but never authors new ones itself); per
    /// architecture.md section 1, Assets/Prefabs/ is GENERATED, not
    /// hand-edited.
    ///
    /// ORDERING REQUIREMENT: SceneBuilder.BuildBootScene() loads
    /// Assets/Prefabs/Bowl.prefab and Assets/Prefabs/Customer.prefab by path
    /// (for the kitchen's bowl stack and the CustomerSpawner's prefab
    /// reference respectively). Run this menu item BEFORE
    /// "Pho/Scenes/Build Boot Scene" -- if the prefabs don't exist yet,
    /// SceneBuilder logs a clear warning and skips the dependent step rather
    /// than crashing, but the scene will be missing that content until this
    /// is run and BuildBootScene is re-run.
    ///
    /// Idempotency strategy: LOAD-MODIFY-RESAVE, not "just call
    /// SaveAsPrefabAsset and hope it's a safe overwrite". Concretely: if a
    /// prefab already exists at the target path, its contents are loaded via
    /// <see cref="PrefabUtility.LoadPrefabContents"/>, reconfigured in place,
    /// and saved back over the same path (preserving the asset's GUID/.meta
    /// across reruns, same reasoning as SceneBuilder's "overwrite in place"
    /// comment). Only when nothing exists at the path yet is a fresh
    /// GameObject built from scratch (in the current scene, temporarily) and
    /// then destroyed once <see cref="PrefabUtility.SaveAsPrefabAsset"/> has
    /// persisted it.
    ///
    /// Invoked headlessly via:
    ///   Unity -batchmode -nographics -projectPath . \
    ///     -executeMethod Pho.EditorTools.PrefabBuilder.BuildAllPrefabs -quit
    ///
    /// This script itself never launches Unity or runs in batch mode -- it
    /// is only ever invoked BY Unity, by a separate integration pass.
    /// </summary>
    public static class PrefabBuilder
    {
        const string PrefabsFolder = "Assets/Prefabs";
        const string CustomerPrefabPath = PrefabsFolder + "/Customer.prefab";
        const string BowlPrefabPath = PrefabsFolder + "/Bowl.prefab";
        const string PlayerPrefabPath = PrefabsFolder + "/Player.prefab";

        const string CustomerRootName = "Customer";
        const string BowlRootName = "Bowl";
        const string PlayerRootName = "Player";
        const string PlayerCameraPivotName = "CameraPivot";
        const string PlayerModelRootName = "ModelRoot";

        // "reasonable defaults ... matching a capsule" per the brief.
        const float CustomerNavAgentRadius = 0.4f;
        const float CustomerNavAgentHeight = 2f;

        // Squashed cylinder grey-box stand-in for a bowl -- not real art.
        static readonly Vector3 BowlVisualScale = new Vector3(0.3f, 0.1f, 0.3f);

        [MenuItem("Pho/Prefabs/Build All Prefabs")]
        public static void BuildAllPrefabs()
        {
            EnsureFolder();

            BuildCustomerPrefab();
            BuildBowlPrefab();
            BuildPlayerPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[PrefabBuilder] Built '{CustomerPrefabPath}', '{BowlPrefabPath}' and '{PlayerPrefabPath}'.");
        }

        /// <summary>
        /// Builds Assets/Prefabs/Player.prefab -- the object Netcode spawns
        /// once per connected client.
        ///
        /// WHY A PREFAB AT ALL, when SceneBuilder already places a player:
        /// NGO spawns players from NetworkManager.NetworkConfig.PlayerPrefab,
        /// so a scene-placed player can never be one of the four. The
        /// scene-placed player remains the OFFLINE player for single-player;
        /// reconciling the two (disable the offline one when a session
        /// starts) is the next integration step and is deliberately not done
        /// here, so the currently-working single-player path stays working.
        ///
        /// Camera and AudioListener are CHILDREN of the prefab, not scene
        /// objects, because NetworkPlayer.SetLocalRigEnabled finds them with
        /// GetComponentsInChildren and switches them off on every remote
        /// body -- otherwise four cameras fight and every client runs four
        /// AudioListeners.
        /// </summary>
        static void BuildPlayerPrefab()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) != null
                ? PrefabUtility.LoadPrefabContents(PlayerPrefabPath)
                : new GameObject(PlayerRootName);

            root.name = PlayerRootName;

            // Strip children so a rerun rebuilds cleanly rather than
            // accumulating duplicate camera pivots.
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

            EnsureComponent<CharacterController>(root);
            EnsureComponent<Pho.Player.FirstPersonMotor>(root);
            EnsureComponent<Pho.Player.PlayerInteractor>(root);
            EnsureComponent<Unity.Netcode.NetworkObject>(root);
            EnsureComponent<Pho.Net.Player.NetworkPlayer>(root);

            var pivot = new GameObject(PlayerCameraPivotName);
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            pivot.AddComponent<Camera>();
            pivot.AddComponent<AudioListener>();

            var modelRoot = new GameObject(PlayerModelRootName);
            modelRoot.transform.SetParent(root.transform, false);

            var motor = root.GetComponent<Pho.Player.FirstPersonMotor>();
            SetRef(motor, "cameraPivot", pivot.transform);

            var netPlayer = root.GetComponent<Pho.Net.Player.NetworkPlayer>();
            SetRef(netPlayer, "modelRoot", modelRoot.transform);

            var interactor = root.GetComponent<Pho.Player.PlayerInteractor>();
            SetInt(interactor, "interactableMask", ~0);

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        static T EnsureComponent<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        static void SetRef(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogWarning($"[PrefabBuilder] no field '{field}' on {target.GetType().Name}"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetInt(UnityEngine.Object target, string field, int value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogWarning($"[PrefabBuilder] no field '{field}' on {target.GetType().Name}"); return; }
            prop.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(PrefabsFolder))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
        }

        /// <summary>
        /// Builds/updates Assets/Prefabs/Customer.prefab: a capsule visual +
        /// CapsuleCollider (from CreatePrimitive(Capsule)), a NavMeshAgent
        /// with radius/height matched to that capsule, and a CustomerAgent
        /// component (its [RequireComponent(typeof(NavMeshAgent))] is
        /// already satisfied by the explicit NavMeshAgent added below).
        ///
        /// entranceTransform/exitTransform are deliberately left unassigned
        /// here -- they are per-instance scene Transforms (see
        /// CustomerAgent.cs's own field tooltips) and cannot be meaningfully
        /// baked into a shared prefab asset. See SceneBuilder.cs and this
        /// pass's final report for how that gap is (and currently isn't)
        /// closed at the scene/spawner level.
        /// </summary>
        static void BuildCustomerPrefab()
        {
            bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(CustomerPrefabPath) != null;
            GameObject root = existed
                ? PrefabUtility.LoadPrefabContents(CustomerPrefabPath)
                : BuildCustomerVisualRoot();

            var navAgent = root.GetComponent<NavMeshAgent>();
            if (navAgent == null) navAgent = root.AddComponent<NavMeshAgent>();
            navAgent.radius = CustomerNavAgentRadius;
            navAgent.height = CustomerNavAgentHeight;

            if (root.GetComponent<CustomerAgent>() == null)
                root.AddComponent<CustomerAgent>();

            PrefabUtility.SaveAsPrefabAsset(root, CustomerPrefabPath);

            if (existed) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }

        static GameObject BuildCustomerVisualRoot()
        {
            // CreatePrimitive(Capsule) already adds a CapsuleCollider --
            // satisfies the brief's "CharacterController or CapsuleCollider"
            // requirement for a simple humanoid stand-in with zero extra
            // wiring.
            var root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            root.name = CustomerRootName;
            return root;
        }

        /// <summary>
        /// Builds/updates Assets/Prefabs/Bowl.prefab: a squashed cylinder
        /// visual (grey-box stand-in, not real art), a Rigidbody, the
        /// cylinder primitive's default Collider (satisfies BowlObject's
        /// [RequireComponent(typeof(Rigidbody))] plus IHoldable's implicit
        /// need for a Collider to disable/re-enable on pickup/drop), and a
        /// BowlObject component.
        /// </summary>
        static void BuildBowlPrefab()
        {
            bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(BowlPrefabPath) != null;
            GameObject root = existed
                ? PrefabUtility.LoadPrefabContents(BowlPrefabPath)
                : BuildBowlVisualRoot();

            root.transform.localScale = BowlVisualScale;

            if (root.GetComponent<Rigidbody>() == null)
                root.AddComponent<Rigidbody>();

            if (root.GetComponent<BowlObject>() == null)
                root.AddComponent<BowlObject>();

            PrefabUtility.SaveAsPrefabAsset(root, BowlPrefabPath);

            if (existed) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }

        static GameObject BuildBowlVisualRoot()
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            root.name = BowlRootName;
            return root;
        }
    }
}
