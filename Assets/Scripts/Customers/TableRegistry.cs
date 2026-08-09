using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pho.Customers
{
    /// <summary>
    /// Unity-side registry of tables and seats in the scene. Deliberately a
    /// DIFFERENT type from the Domain-side `SeatHandle` (architecture.md
    /// §6.2, `ICustomerWorld.TryClaimSeat`) -- this class doesn't know, and
    /// must not need to know, `SeatHandle`'s shape (it's owned by a sibling
    /// agent working concurrently in a different worktree). `CustomerAgent`
    /// is the translation layer between this registry's own seat identity
    /// (<see cref="SeatId"/>) and whatever opaque `SeatHandle` the Domain
    /// layer eventually hands back.
    ///
    /// No Kitchen/dining-room scene content exists yet (that's later
    /// scene-building work, out of scope for this pass), so this registry
    /// is designed to work purely against a serialized list of seat
    /// <see cref="Transform"/>s that a later Inspector pass will assign once
    /// real table/seat prefabs exist. Until then -- and any time the list is
    /// empty or an entry is unassigned -- every claim attempt degrades
    /// gracefully to "no seats available" (a `false` return) rather than
    /// throwing or indexing out of range.
    /// </summary>
    public sealed class TableRegistry : MonoBehaviour
    {
        /// <summary>
        /// One seat: which table it belongs to (matches the GDD's "Table 3"
        /// framing -- a plain string, not a typed Domain ID, since tables
        /// are a Unity-scene-only concept in this pass) and the world
        /// Transform a customer should walk to / sit at. Assign in the
        /// Inspector once table/seat prefabs exist; left unassigned for now.
        /// </summary>
        [Serializable]
        public sealed class SeatSlot
        {
            [Tooltip("Table identifier, e.g. \"table.3\" (GDD 'Table 3' framing). Purely a Unity-scene label -- not a Domain-typed ID.")]
            public string tableId = "table.unassigned";

            [Tooltip("World position a customer walks to / sits at. Assign once table/seat prefabs exist.")]
            public Transform anchor;
        }

        [Header("Seats (assign in Inspector once table/seat prefabs exist)")]
        [Tooltip("Intentionally empty by default -- no table/seat GameObjects exist in the scene yet. TryClaimSeat degrades gracefully to 'no seats available' rather than crashing while this is empty.")]
        [SerializeField] List<SeatSlot> seats = new List<SeatSlot>();

        readonly HashSet<int> _claimedIndices = new HashSet<int>();
        bool _warnedEmpty;

        /// <summary>
        /// Opaque handle identifying a seat within THIS registry. Callers
        /// outside this file should treat it as a token: obtained from
        /// <see cref="TryClaimSeat"/>, handed back unchanged to
        /// <see cref="ReleaseSeat"/>. Not related to the Domain-side
        /// `SeatHandle` -- see the class doc comment.
        /// </summary>
        public readonly struct SeatId : IEquatable<SeatId>
        {
            internal readonly int Index;
            internal SeatId(int index) => Index = index;

            public static readonly SeatId Invalid = new SeatId(-1);
            public bool IsValid => Index >= 0;

            public bool Equals(SeatId other) => Index == other.Index;
            public override bool Equals(object obj) => obj is SeatId other && Equals(other);
            public override int GetHashCode() => Index;
            public static bool operator ==(SeatId a, SeatId b) => a.Equals(b);
            public static bool operator !=(SeatId a, SeatId b) => !a.Equals(b);
        }

        /// <summary>
        /// Claims the first free, properly-assigned seat. Returns false
        /// (never throws) when the seat list is empty, every seat is
        /// malformed (unassigned anchor), or every seat is already claimed.
        /// </summary>
        public bool TryClaimSeat(out SeatId seatId, out Vector3 worldPosition)
        {
            if (seats.Count == 0)
            {
                if (!_warnedEmpty)
                {
                    Debug.LogWarning("[TableRegistry] No seats configured -- assign seat Transforms in the Inspector once table/seat prefabs exist. Reporting 'no seats available' until then.");
                    _warnedEmpty = true;
                }

                seatId = SeatId.Invalid;
                worldPosition = Vector3.zero;
                return false;
            }

            for (int i = 0; i < seats.Count; i++)
            {
                if (_claimedIndices.Contains(i)) continue;

                var slot = seats[i];
                if (slot == null || slot.anchor == null)
                {
                    // Malformed entry (e.g. a placeholder row added in the
                    // Inspector but not wired to a Transform yet) -- skip
                    // rather than crash.
                    continue;
                }

                _claimedIndices.Add(i);
                seatId = new SeatId(i);
                worldPosition = slot.anchor.position;
                return true;
            }

            seatId = SeatId.Invalid;
            worldPosition = Vector3.zero;
            return false;
        }

        /// <summary>Idempotent: releasing an invalid or already-free seat is a no-op.</summary>
        public void ReleaseSeat(SeatId seatId)
        {
            if (!seatId.IsValid) return;
            _claimedIndices.Remove(seatId.Index);
        }

        /// <summary>Convenience lookup for debug labels / UI, not required by ICustomerWorld.</summary>
        public bool TryGetTableId(SeatId seatId, out string tableId)
        {
            if (seatId.IsValid && seatId.Index < seats.Count && seats[seatId.Index] != null)
            {
                tableId = seats[seatId.Index].tableId;
                return true;
            }

            tableId = null;
            return false;
        }

        public int TotalSeatCount => seats.Count;
        public int FreeSeatCount
        {
            get
            {
                int free = 0;
                for (int i = 0; i < seats.Count; i++)
                {
                    var slot = seats[i];
                    if (slot != null && slot.anchor != null && !_claimedIndices.Contains(i)) free++;
                }
                return free;
            }
        }
    }
}
