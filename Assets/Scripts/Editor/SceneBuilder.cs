using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Pho.Core;
using Pho.Core.DayCycle;
using Pho.Core.Progression;
using Pho.Core.Restaurant;
using Pho.Core.Save;
using Pho.Customers;
using Pho.Data;
using Pho.Domain.Contracts;
using Pho.Kitchen;
using Pho.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

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

        const string NavMeshDataFolder = "Assets/NavMeshData";
        const string NavMeshDataAssetPath = NavMeshDataFolder + "/Boot.asset";

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

        // ------------------------------------------------------------------
        // Kitchen / Dining / Customer additions (Wave 4 content pass).
        //
        // ORDERING REQUIREMENT: this method loads Assets/Prefabs/Bowl.prefab
        // and Assets/Prefabs/Customer.prefab by path (see BuildBowlStack /
        // BuildCustomerSpawnerAndEntranceExit below). Run
        // "Pho/Prefabs/Build All Prefabs" (Pho.EditorTools.PrefabBuilder)
        // BEFORE "Pho/Scenes/Build Boot Scene". If the prefabs don't exist
        // yet, the affected steps log a clear warning and skip themselves
        // rather than crashing the whole build.
        // ------------------------------------------------------------------

        const string BowlPrefabPath = "Assets/Prefabs/Bowl.prefab";
        const string CustomerPrefabPath = "Assets/Prefabs/Customer.prefab";

        const string BrothPotName = "BrothPot";
        const string PassCounterName = "PassCounter";
        const string TableRegistryName = "TableRegistry";
        const string CustomerSpawnerName = "CustomerSpawner";
        const string EntranceName = "Entrance";
        const string ExitName = "Exit";
        const string RestaurantSignName = "RestaurantSign";
        const string UpgradeStationName = "UpgradeStation";

        // Kitchen sits at x=+5, dining at x=-5 -- both well clear of the
        // player's spawn at (0,1,-2) and of each other. Coordinates below
        // are the exact anchors suggested by the brief.
        static readonly Vector3 KitchenOrigin = new Vector3(5f, 0f, 0f);
        static readonly Vector3 DiningOrigin = new Vector3(-5f, 0f, 0f);

        static readonly Vector3 BrothPotOffset = new Vector3(0f, 0.5f, -2f);
        static readonly Vector3 BrothPotScale = new Vector3(0.8f, 0.5f, 0.8f);

        static readonly Vector3 PassCounterOffset = new Vector3(-3f, 0.5f, 0f);
        static readonly Vector3 PassCounterScale = new Vector3(1.5f, 1f, 0.8f);

        static readonly Vector3 BowlStackOffset = new Vector3(-3f, 0.05f, 1.5f);
        const int BowlStackCount = 4;
        const float BowlStackSpacing = 0.15f;

        const float IngredientStationSpacing = 1.5f;
        const float IngredientStationRowOffsetZ = 2.5f;
        static readonly Vector3 IngredientStationScale = new Vector3(0.8f, 0.6f, 0.8f);

        // One IngredientStation per ContentManifest ingredient ID (6 total,
        // per the brief). Slot assignments mirror ContentManifest's
        // RecipeComponent.slot for that ingredient (rec.pho_tai/rec.pho_chin
        // agree on the slot per ingredient). NOTE: ing.broth_base is
        // included here per the brief's explicit "one IngredientStation per
        // ingredient (6 total)" even though BrothPot -- not
        // IngredientStation -- is the real gameplay path for broth (see
        // IngredientStation.cs's own class doc: "Broth is handled separately
        // by BrothPot because it comes from a simmered pot, not a static
        // InventoryModel lot"). This station exists for scene/content
        // completeness alongside the real BrothPot; flagged as a judgment
        // call in the final report.
        static readonly (string id, ComponentSlot slot, string displayName, float portion)[] IngredientStationDefs =
        {
            ("ing.rice_noodles",   ComponentSlot.Noodle,   "rice noodles",     1.0f),
            ("ing.beef_brisket",   ComponentSlot.Protein,  "raw beef brisket", 1.0f),
            ("ing.beef_well_done", ComponentSlot.Protein,  "well-done beef",   1.0f),
            ("ing.onion",          ComponentSlot.Aromatic, "onion",            0.3f),
            ("ing.herbs_mixed",    ComponentSlot.Herb,     "mixed herbs",      0.3f),
            ("ing.broth_base",     ComponentSlot.Broth,    "broth base",       1.0f),
        };

        const int TableCount = 4;
        const int SeatsPerTable = 2;
        static readonly Vector3 TableScale = new Vector3(1.2f, 0.75f, 1.2f);
        const float TableHalfHeight = 0.375f;
        const float SeatSideOffset = 0.9f;
        static readonly Vector3[] TableOffsets =
        {
            new Vector3(-2f, 0f, -2f),
            new Vector3(2f, 0f, -2f),
            new Vector3(-2f, 0f, 2f),
            new Vector3(2f, 0f, 2f),
        };

        static readonly Vector3 EntrancePosition = DiningOrigin + new Vector3(0f, 0f, 4f);
        static readonly Vector3 ExitPosition = DiningOrigin + new Vector3(-4f, 0f, 4f);

        // Grey-box post beside the entrance -- the one physical object that
        // lets the player open/close the restaurant (RestaurantSign,
        // Pho.Core.DayCycle). Offset to the side of EntrancePosition so it
        // doesn't sit on top of the customer spawn point/entrance transform.
        static readonly Vector3 RestaurantSignPosition = EntrancePosition + new Vector3(1.5f, 0.9f, 0f);
        static readonly Vector3 RestaurantSignScale = new Vector3(0.3f, 1.8f, 0.3f);

        // Where the player buys eq.burner_commercial. Placed in the kitchen
        // beside the broth pot it upgrades, so the cause/effect of the
        // purchase is physically obvious.
        static readonly Vector3 UpgradeStationOffset = new Vector3(1.5f, 0.5f, -2f);
        static readonly Vector3 UpgradeStationScale = new Vector3(0.7f, 1f, 0.5f);

        [MenuItem("Pho/Scenes/Build Boot Scene")]
        public static void BuildBootScene()
        {
            EnsureFolder();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var ground = BuildGround();
            BuildSun();
            BuildPlayer();
            BuildGameManager();

            BuildKitchenArea();
            var seatSlots = BuildDiningArea();
            BuildTableRegistry(seatSlots);
            BuildCustomerSpawnerAndEntranceExit();
            BuildRestaurantSign();
            BuildUpgradeStation();

            // Baked LAST so the NavMesh accounts for every obstacle placed
            // above (kitchen stations, tables) rather than just the bare
            // ground plane.
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

            Debug.Log(
                $"[SceneBuilder] Built '{BootScenePath}' -- ground, sun, player @ {PlayerSpawnPosition}, " +
                $"GameBootstrap, kitchen ({IngredientStationDefs.Length} ingredient stations + broth pot + pass " +
                $"counter + bowl stack), dining ({TableCount} tables / {seatSlots.Count} seats), " +
                $"customer spawner + entrance/exit, restaurant sign, upgrade station, baked NavMesh.");
        }

        // ------------------------------------------------------------------
        // Art: model-backed props
        //
        // Every gameplay object in this scene used to be a
        // GameObject.CreatePrimitive cube/cylinder -- which is both the
        // visual AND the collider AND the component host. Swapping in real
        // art must NOT disturb the gameplay half of that, because the
        // colliders are what PlayerInteractor raycasts against and their
        // sizes are load-bearing (see the interactableMask incident).
        //
        // So: the prop root keeps the collider and the gameplay component at
        // exactly the position/size it always had, and the imported model is
        // parented underneath purely as visuals, with its own renderers and
        // NO collider. Gameplay is unchanged by definition; only what you
        // see changes.
        //
        // FALLBACK IS DELIBERATE: if the model file isn't on disk yet (art
        // still in flight, or a fresh clone before `make art`), this falls
        // back to the original primitive so the scene still builds and the
        // game still plays. A missing art asset must never break the build.
        // ------------------------------------------------------------------

        const string ArtFolder = "Assets/Art/Generated";

        static GameObject ArtModel(string assetName)
        {
            // FBX, not GLB: Unity imports FBX natively, whereas .glb needs
            // the gltfast package. The Blender pipeline writes both.
            var path = $"{ArtFolder}/{assetName}.fbx";
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>
        /// Builds a prop with real art when the model exists, falling back to
        /// a primitive when it doesn't.
        ///
        /// POSITION SEMANTICS -- read this before calling: <paramref name="floorPosition"/>
        /// is where the object CONTACTS THE FLOOR, not its centre. Art models
        /// have floor-contact origins by construction (see
        /// art/blender/lib.py's set_origin_to_floor), so this is the natural
        /// frame for them; the primitive fallback converts to the centre-based
        /// frame Unity primitives use. Mixing the two conventions silently
        /// leaves props hovering or sunk, so both branches are derived from
        /// the same floor position here rather than at each call site.
        /// </summary>
        static GameObject BuildProp(string name, string modelAssetName, Vector3 floorPosition, Vector3 colliderSize, PrimitiveType fallback = PrimitiveType.Cube)
        {
            var centre = floorPosition + new Vector3(0f, colliderSize.y * 0.5f, 0f);

            var model = ArtModel(modelAssetName);
            if (model == null)
            {
                var primitive = GameObject.CreatePrimitive(fallback);
                primitive.name = name;
                primitive.transform.position = centre;
                primitive.transform.localScale = colliderSize;
                Debug.LogWarning($"[SceneBuilder] No art model '{modelAssetName}.fbx' in {ArtFolder} -- falling back to a {fallback} primitive for '{name}'. Run the Blender art pipeline to replace it.");
                return primitive;
            }

            var root = new GameObject(name);
            root.transform.position = floorPosition;

            var collider = root.AddComponent<BoxCollider>();
            collider.size = colliderSize;
            // Lift the collider so it occupies the same world volume the
            // primitive did, given the root now sits on the floor.
            collider.center = new Vector3(0f, colliderSize.y * 0.5f, 0f);

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = $"{name}_Visual";
            visual.transform.SetParent(root.transform, worldPositionStays: false);
            visual.transform.localPosition = Vector3.zero;

            // Imported meshes must not contribute colliders -- the root's
            // BoxCollider is the single source of truth for interaction.
            foreach (var c in visual.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(c);
            }

            return root;
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

        // Warm interior lighting. Per the project plan, "stylized realism"
        // (GDD §40) is achieved through LIGHTING AND MATERIALS, not polygon
        // count -- this is what keeps procedural art from looking cheap, and
        // it is the cheapest single quality lever available.
        //
        // Three-part setup, all cheap:
        //  1. A low, warm key light angled in through the shopfront, so the
        //     room has a clear light direction and real shadows.
        //  2. A cool, dim ambient fill so shadowed sides aren't dead black --
        //     warm key against cool fill is the whole trick.
        //  3. Warm point lights over the seating and the kitchen pass, which
        //     is what actually sells "small restaurant at night".
        static readonly Color WarmKey = new Color(1.0f, 0.89f, 0.72f);
        static readonly Color WarmLamp = new Color(1.0f, 0.82f, 0.58f);
        static readonly Color CoolAmbient = new Color(0.32f, 0.36f, 0.45f);

        static void BuildSun()
        {
            var sunGo = new GameObject(SunName);
            // Low angle (25deg) rakes across the room and makes every bevel
            // catch a highlight; the steep 50deg it used to sit at flattened
            // everything into evenly-lit shapes.
            sunGo.transform.rotation = Quaternion.Euler(25f, -35f, 0f);

            var light = sunGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.color = WarmKey;
            light.shadows = LightShadows.Soft;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = CoolAmbient;
            RenderSettings.ambientEquatorColor = CoolAmbient * 0.7f;
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.10f, 0.09f);

            BuildLamp("Lamp_Dining", DiningOrigin + new Vector3(0f, 2.6f, 0f), range: 9f, intensity: 14f);
            BuildLamp("Lamp_Kitchen", KitchenOrigin + new Vector3(0f, 2.6f, 0f), range: 9f, intensity: 12f);
        }

        static void BuildLamp(string name, Vector3 position, float range, float intensity)
        {
            var go = new GameObject(name);
            go.transform.position = position;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = WarmLamp;
            light.range = range;
            light.intensity = intensity;
            // Point-light shadows are the expensive kind and these are fill
            // lights sitting above the play space -- the directional key
            // already provides the shadows the eye reads.
            light.shadows = LightShadows.None;
        }

        static void BuildPlayer()
        {
            var player = new GameObject(PlayerName);
            player.transform.position = PlayerSpawnPosition;

            // CharacterController defaults (height 2, radius 0.5, center
            // (0,0,0)) are fine for a vertical-slice human capsule.
            player.AddComponent<CharacterController>();

            var motor = player.AddComponent<FirstPersonMotor>();
            var interactor = player.AddComponent<PlayerInteractor>();

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
            // needs wiring here.
            //
            // interactableMask MUST be set explicitly. A LayerMask's
            // serialized default is m_Bits: 0, and Physics.SphereCast with a
            // zero mask matches NOTHING -- so leaving it at its default (as
            // this method previously did, on the reasoning that no dedicated
            // "Interactable" layer exists yet) silently made the player
            // unable to interact with anything at all in play mode. Verified
            // by reading `m_Bits: 0` straight out of the generated
            // Boot.unity, not inferred.
            //
            // ~0 (every layer) is the correct vertical-slice value rather
            // than a placeholder: PlayerInteractor.ResolveInteractable
            // already filters every hit through
            // GetComponentInParent<IInteractable>(), so a permissive
            // physics mask cannot produce a false positive -- it only costs
            // a slightly wider broadphase. Narrowing this to a dedicated
            // layer is a performance/polish refinement for later, not a
            // correctness requirement.
            SetSerializedInt(interactor, "interactableMask", ~0);
        }

        static void BuildGameManager()
        {
            var go = new GameObject(GameManagerName);
            var bootstrap = go.AddComponent<GameBootstrap>();

            // F5/F9 debug save/load -- see SaveDebugTrigger's own class doc
            // comment. No UI; this is the minimal way to actually exercise
            // the save system until a real save/load menu exists.
            go.AddComponent<SaveDebugTrigger>();

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

        // ------------------------------------------------------------------
        // Kitchen
        // ------------------------------------------------------------------

        static void BuildKitchenArea()
        {
            BuildBrothPot();
            BuildIngredientStations();
            BuildPassCounter();
            BuildBowlStack();
        }

        static void BuildBrothPot()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = BrothPotName;
            go.transform.position = KitchenOrigin + BrothPotOffset;
            go.transform.localScale = BrothPotScale;
            // CreatePrimitive(Cylinder) already adds a Collider (a
            // CapsuleCollider, per Unity's built-in primitive setup) --
            // satisfies BrothPot's [RequireComponent(typeof(Collider))]
            // with zero extra wiring. BrothPot needs no field configuration
            // beyond its own in-class defaults per the brief.
            go.AddComponent<BrothPot>();
        }

        static void BuildIngredientStations()
        {
            int n = IngredientStationDefs.Length;
            float centerIndex = (n - 1) / 2f;

            for (int i = 0; i < n; i++)
            {
                var def = IngredientStationDefs[i];

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"IngredientStation_{def.id}";
                go.transform.localScale = IngredientStationScale;

                float x = KitchenOrigin.x + (i - centerIndex) * IngredientStationSpacing;
                float y = IngredientStationScale.y * 0.5f;
                float z = KitchenOrigin.z + IngredientStationRowOffsetZ;
                go.transform.position = new Vector3(x, y, z);

                // CreatePrimitive(Cube) already adds a BoxCollider --
                // satisfies IngredientStation's [RequireComponent(typeof(Collider))].
                var station = go.AddComponent<IngredientStation>();
                SetSerializedString(station, "ingredientId", def.id);
                SetSerializedEnum(station, "slot", def.slot);
                SetSerializedFloat(station, "portionAmount", def.portion);
                SetSerializedString(station, "displayName", def.displayName);
            }
        }

        static void BuildPassCounter()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = PassCounterName;
            go.transform.position = KitchenOrigin + PassCounterOffset;
            go.transform.localScale = PassCounterScale;
            // CreatePrimitive(Cube) already adds a BoxCollider -- satisfies
            // PassCounter's [RequireComponent(typeof(Collider))]; its own
            // Awake() auto-resolves `surfaceCollider` from GetComponent<Collider>()
            // when left unassigned, so no SetSerializedField call is needed here.
            go.AddComponent<PassCounter>();
        }

        static void BuildBowlStack()
        {
            var bowlPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BowlPrefabPath);
            if (bowlPrefab == null)
            {
                Debug.LogWarning($"[SceneBuilder] Could not load '{BowlPrefabPath}' -- skipping bowl stack. Run 'Pho/Prefabs/Build All Prefabs' BEFORE 'Pho/Scenes/Build Boot Scene'.");
                return;
            }

            var basePosition = KitchenOrigin + BowlStackOffset;
            for (int i = 0; i < BowlStackCount; i++)
            {
                // InstantiatePrefab (not GameObject.Instantiate) keeps the
                // scene instance linked to the source prefab asset.
                var bowlInstance = (GameObject)PrefabUtility.InstantiatePrefab(bowlPrefab);
                bowlInstance.name = $"Bowl_{i + 1}";
                bowlInstance.transform.position = basePosition + new Vector3(0f, BowlStackSpacing * i, 0f);
            }
        }

        // ------------------------------------------------------------------
        // Dining
        // ------------------------------------------------------------------

        static List<TableRegistry.SeatSlot> BuildDiningArea()
        {
            var seatSlots = new List<TableRegistry.SeatSlot>(TableCount * SeatsPerTable);

            for (int t = 0; t < TableCount; t++)
            {
                var tableId = $"table.{t + 1}";
                var tableWorldXZ = DiningOrigin + TableOffsets[t];

                var table = BuildProp(
                    $"DiningTable_{tableId}",
                    "DiningTable",
                    new Vector3(tableWorldXZ.x, 0f, tableWorldXZ.z),
                    TableScale);

                // DirtyTable's tableId MUST match the seat slots' tableId
                // below -- that string is the only link between "a customer
                // left this seat" and "this table is now dirty"
                // (CleanlinessService keys purely on the id). It also
                // self-registers with CleanlinessService on enable, which is
                // what populates the denominator of the Cleanliness01
                // fraction, so a table without this component would silently
                // shrink the room as far as cleanliness is concerned.
                // CreatePrimitive(Cube) already supplies the BoxCollider the
                // player needs to aim at it.
                var dirtyTable = table.AddComponent<DirtyTable>();
                SetSerializedString(dirtyTable, "tableId", tableId);

                for (int s = 0; s < SeatsPerTable; s++)
                {
                    float sideSign = s == 0 ? 1f : -1f;
                    var seatGo = new GameObject($"Seat_{tableId}_{s + 1}");
                    seatGo.transform.SetParent(table.transform, worldPositionStays: false);

                    // Assigned via world .position (not .localPosition)
                    // deliberately -- the table's non-uniform localScale
                    // would otherwise distort a localPosition offset (Unity
                    // scales a child's localPosition by the parent's
                    // lossyScale). Setting world position sidesteps that.
                    seatGo.transform.position = new Vector3(
                        tableWorldXZ.x + sideSign * SeatSideOffset,
                        0f,
                        tableWorldXZ.z);

                    seatSlots.Add(new TableRegistry.SeatSlot { tableId = tableId, anchor = seatGo.transform });

                    // A visible stool at each seat. Purely decorative -- the
                    // seat ANCHOR above is what TableRegistry/CustomerAgent
                    // actually use, so the stool carries no collider and no
                    // gameplay meaning, and its absence (art not generated
                    // yet) changes nothing.
                    var stoolModel = ArtModel("Stool");
                    if (stoolModel != null)
                    {
                        var stool = (GameObject)PrefabUtility.InstantiatePrefab(stoolModel);
                        stool.name = $"Stool_{tableId}_{s + 1}";
                        stool.transform.position = seatGo.transform.position;
                        foreach (var c in stool.GetComponentsInChildren<Collider>(true))
                        {
                            UnityEngine.Object.DestroyImmediate(c);
                        }
                    }
                }
            }

            return seatSlots;
        }

        static void BuildTableRegistry(List<TableRegistry.SeatSlot> seatSlots)
        {
            var go = new GameObject(TableRegistryName);
            var registry = go.AddComponent<TableRegistry>();

            var so = new SerializedObject(registry);
            var seatsProp = so.FindProperty("seats");
            if (seatsProp == null)
            {
                Debug.LogWarning("[SceneBuilder] Could not find serialized field 'seats' on TableRegistry -- skipping seat wire-up.");
                return;
            }

            seatsProp.arraySize = seatSlots.Count;
            for (int i = 0; i < seatSlots.Count; i++)
            {
                var element = seatsProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("tableId").stringValue = seatSlots[i].tableId;
                element.FindPropertyRelative("anchor").objectReferenceValue = seatSlots[i].anchor;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        // Customer spawner + entrance/exit
        // ------------------------------------------------------------------

        static void BuildCustomerSpawnerAndEntranceExit()
        {
            var entrance = new GameObject(EntranceName);
            entrance.transform.position = EntrancePosition;

            var exit = new GameObject(ExitName);
            exit.transform.position = ExitPosition;

            var spawnerGo = new GameObject(CustomerSpawnerName);
            var spawner = spawnerGo.AddComponent<CustomerSpawner>();

            SetSerializedField(spawner, "spawnPoint", entrance.transform);

            // Forwarded by CustomerSpawner onto each spawned CustomerAgent
            // via SetWorldAnchors (integration-pass addition to
            // CustomerSpawner.cs -- these fields didn't exist when this
            // scene-building code was first written; the gap is now closed).
            SetSerializedField(spawner, "entranceTransform", entrance.transform);
            SetSerializedField(spawner, "exitTransform", exit.transform);

            var customerPrefabGo = AssetDatabase.LoadAssetAtPath<GameObject>(CustomerPrefabPath);
            if (customerPrefabGo != null)
            {
                var customerAgent = customerPrefabGo.GetComponent<CustomerAgent>();
                if (customerAgent != null)
                {
                    SetSerializedField(spawner, "customerPrefab", customerAgent);
                }
                else
                {
                    Debug.LogWarning($"[SceneBuilder] '{CustomerPrefabPath}' has no CustomerAgent component -- leaving CustomerSpawner.customerPrefab unassigned.");
                }
            }
            else
            {
                Debug.LogWarning($"[SceneBuilder] Could not load '{CustomerPrefabPath}' -- CustomerSpawner.customerPrefab left unassigned. Run 'Pho/Prefabs/Build All Prefabs' BEFORE 'Pho/Scenes/Build Boot Scene'.");
            }
        }

        // ------------------------------------------------------------------
        // Restaurant sign (open/close IInteractable)
        // ------------------------------------------------------------------

        static void BuildRestaurantSign()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = RestaurantSignName;
            go.transform.position = RestaurantSignPosition;
            go.transform.localScale = RestaurantSignScale;
            // CreatePrimitive(Cube) already adds a BoxCollider, which is all
            // RestaurantSign needs to be raycast-hittable by PlayerInteractor
            // (RestaurantSign has no [RequireComponent(typeof(Collider))],
            // but IInteractable objects need a collider to be hit at all).
            go.AddComponent<RestaurantSign>();
        }

        // ------------------------------------------------------------------
        // Upgrade station (progression)
        // ------------------------------------------------------------------

        static void BuildUpgradeStation()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = UpgradeStationName;
            go.transform.position = KitchenOrigin + UpgradeStationOffset;
            go.transform.localScale = UpgradeStationScale;
            // CreatePrimitive(Cube) supplies the BoxCollider the player needs
            // to aim at. equipmentId keeps its own serialized default
            // ("eq.burner_commercial") -- the single upgrade this slice has.
            go.AddComponent<UpgradeStation>();
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

            // Extract the baked NavMeshData to its own external .asset file.
            // GOTCHA (confirmed via a standalone repro, not assumed): if the
            // baked NavMeshData is left embedded as an inline sub-object of
            // the scene, Unity silently serializes the WHOLE containing
            // scene file as binary -- even with
            // EditorSettings.serializationMode == ForceText project-wide --
            // which defeats the "scenes are reviewable text diffs"
            // requirement (`file Boot.unity` reported "data" instead of
            // "ASCII text", and the file had no `%YAML` header at all).
            // Saving the same bake as a standalone asset keeps the scene
            // itself text-serialized; NavMeshData.asset is expected to be
            // (and always was, even before this fix) an opaque binary blob
            // -- that's fine, it was never meant to be hand-reviewed like a
            // scene diff, only the scene file needs to be.
            var navMeshDataProp = surfaceType.GetProperty("navMeshData", BindingFlags.Public | BindingFlags.Instance);
            var navMeshData = navMeshDataProp?.GetValue(surface) as NavMeshData;
            if (navMeshData != null)
            {
                if (!AssetDatabase.IsValidFolder(NavMeshDataFolder))
                    AssetDatabase.CreateFolder("Assets", "NavMeshData");

                // Idempotent regen: BuildBootScene always starts from a
                // brand-new scene, so the previous bake's asset (if any) is
                // stale and must be replaced, not merged into.
                AssetDatabase.DeleteAsset(NavMeshDataAssetPath);
                AssetDatabase.CreateAsset(navMeshData, NavMeshDataAssetPath);
            }
            else
            {
                Debug.LogWarning("[SceneBuilder] NavMeshSurface.navMeshData was null after BuildNavMesh() -- could not extract it to an external asset; the scene may end up binary-serialized.");
            }
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

        /// <summary>Same rationale as <see cref="SetSerializedField"/>, for a private string field.</summary>
        static void SetSerializedString(UnityEngine.Object target, string fieldName, string value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"[SceneBuilder] Could not find serialized field '{fieldName}' on {target.GetType().Name} -- skipping wire-up.");
                return;
            }

            prop.stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Same rationale as <see cref="SetSerializedField"/>, for a private float field.</summary>
        static void SetSerializedFloat(UnityEngine.Object target, string fieldName, float value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"[SceneBuilder] Could not find serialized field '{fieldName}' on {target.GetType().Name} -- skipping wire-up.");
                return;
            }

            prop.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Same rationale as <see cref="SetSerializedField"/>, for a private int-backed field (LayerMask serializes as an int).</summary>
        static void SetSerializedInt(UnityEngine.Object target, string fieldName, int value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"[SceneBuilder] Could not find serialized field '{fieldName}' on {target.GetType().Name} -- skipping wire-up.");
                return;
            }

            prop.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Same rationale as <see cref="SetSerializedField"/>, for a private
        /// plain-int-backed enum field (e.g. ComponentSlot -- declared with
        /// no custom numeric values, so declaration order == underlying
        /// value == enumValueIndex).
        /// </summary>
        static void SetSerializedEnum<TEnum>(UnityEngine.Object target, string fieldName, TEnum value) where TEnum : Enum
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"[SceneBuilder] Could not find serialized field '{fieldName}' on {target.GetType().Name} -- skipping wire-up.");
                return;
            }

            prop.enumValueIndex = Convert.ToInt32(value);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
