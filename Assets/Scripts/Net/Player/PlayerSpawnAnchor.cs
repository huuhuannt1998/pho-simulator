using Pho.Domain.Multiplayer;
using UnityEngine;

namespace Pho.Net.Player
{
    /// <summary>
    /// Marks the point in the restaurant where players arrive, and turns a
    /// slot index into an actual world pose by applying
    /// <see cref="PlayerSlotAllocator.SpawnOffset"/>.
    ///
    /// This exists because four <c>CharacterController</c>s spawned on one
    /// exact point do not politely queue -- Unity's depenetration ejects them
    /// in an order nobody controls, and on a tight kitchen floor that
    /// regularly means through a wall. The offsets themselves are pure and
    /// tested; this component only supplies the anchor they are measured
    /// from.
    ///
    /// Optional by design: if no anchor is present in the scene,
    /// <see cref="NetworkPlayer"/> offsets from wherever the prefab was
    /// placed instead. A missing anchor should spread players out, not stop
    /// the session from starting.
    /// </summary>
    public sealed class PlayerSpawnAnchor : MonoBehaviour
    {
        [Tooltip("Metres between adjacent players in the 2x2 spawn square. Keep it above the CharacterController diameter or they still overlap.")]
        [SerializeField] float spacing = 1.2f;

        /// <summary>
        /// The scene's anchor, if one exists. Set on Awake, so anything that
        /// reads it must run later than that -- which OnNetworkSpawn always
        /// does.
        /// </summary>
        public static PlayerSpawnAnchor Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[PlayerSpawnAnchor] A second anchor ('{name}') was found; keeping '{Instance.name}'. Players spawn around one point only.");
                return;
            }

            Instance = this;
        }

        void OnDestroy()
        {
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        /// <summary>World position a player in <paramref name="slot"/> should start at.</summary>
        public Vector3 PositionForSlot(int slot)
        {
            var offset = PlayerSlotAllocator.SpawnOffset(slot, spacing);
            return transform.position + transform.rotation * new Vector3(offset.X, offset.Y, offset.Z);
        }

        /// <summary>Body yaw a player should start facing. Everyone faces the same way the anchor does -- into the restaurant, not at each other.</summary>
        public float YawForSlot(int slot) => transform.eulerAngles.y;

        /// <summary>
        /// Spawn pose for a slot, falling back to spreading around
        /// <paramref name="fallbackPosition"/> when no anchor exists in the
        /// scene.
        /// </summary>
        public static void ResolvePose(int slot, Vector3 fallbackPosition, float fallbackYaw, out Vector3 position, out float yaw)
        {
            var anchor = Instance;
            if (anchor != null)
            {
                position = anchor.PositionForSlot(slot);
                yaw = anchor.YawForSlot(slot);
                return;
            }

            var offset = PlayerSlotAllocator.SpawnOffset(slot);
            position = fallbackPosition + new Vector3(offset.X, offset.Y, offset.Z);
            yaw = fallbackYaw;
        }
    }
}
