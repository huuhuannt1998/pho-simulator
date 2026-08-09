using UnityEngine;

namespace Pho.Net.Player
{
    /// <summary>
    /// The data-driven answer to "which of these four people am I?".
    ///
    /// Four players in a shared kitchen need to be told apart at a glance,
    /// from behind, mid-rush -- and they also need to not be mistaken for the
    /// customers, who already use the Customer_A/Customer_B models. Both of
    /// those are art decisions, so they live in an asset an artist can edit,
    /// not in a string literal in a MonoBehaviour. <see cref="NetworkPlayer"/>
    /// only ever asks the palette for "entry N"; it never knows a path, a
    /// Resources folder, or a model name.
    ///
    /// Entries are indexed by the player's slot (see
    /// <c>PlayerSlotAllocator</c>), so slot 0 always looks the same on every
    /// client without replicating anything but the slot number itself.
    ///
    /// Authoring note for whoever generates this asset: give each entry a
    /// distinct <see cref="Entry.tint"/> even if you reuse a model. Colour
    /// carries the distinction; the model is a bonus.
    /// </summary>
    [CreateAssetMenu(menuName = "Pho/Net/Player Visual Palette", fileName = "PlayerVisualPalette")]
    public sealed class PlayerVisualPalette : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            [Tooltip("Body model instantiated under NetworkPlayer.modelRoot for this slot. Optional -- leave empty to keep whatever the prefab already has and only tint it.")]
            public GameObject modelPrefab;

            [Tooltip("Applied to every renderer under modelRoot via a MaterialPropertyBlock, so slots share one material and cost no extra draw-call batches.")]
            public Color tint;

            [Tooltip("Optional flavour name shown before the player supplies their own. Leave empty to use the generic 'Cook N'.")]
            public string defaultName;
        }

        [SerializeField] Entry[] entries = new Entry[0];

        public int Count => entries != null ? entries.Length : 0;

        /// <summary>
        /// Entry for a slot. Wraps rather than clamps so an under-filled
        /// palette still yields *some* distinction between neighbouring
        /// slots instead of painting the overflow players identically.
        /// Returns false when the palette is empty, which the caller treats
        /// as "leave the prefab's own look alone".
        /// </summary>
        public bool TryGet(int slot, out Entry entry)
        {
            if (entries == null || entries.Length == 0 || slot < 0)
            {
                entry = default;
                return false;
            }

            entry = entries[slot % entries.Length];
            return true;
        }
    }
}
