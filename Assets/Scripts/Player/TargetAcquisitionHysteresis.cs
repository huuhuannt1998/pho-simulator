using System;

namespace Pho.Player
{
    /// <summary>
    /// Pure, Unity-free hysteresis state machine used by PlayerInteractor:
    /// the currently acquired target only clears after N consecutive "miss"
    /// reports, so a SphereCast that grazes a collider edge for a single
    /// frame doesn't flicker the interaction prompt. Acquiring a (new)
    /// non-null target is always immediate -- hysteresis only guards
    /// against LOSING a target, per architecture.md §5: "hysteresis (must
    /// miss for 3 consecutive frames before clearing the highlight) so the
    /// prompt doesn't flicker on edges."
    ///
    /// Deliberately Unity-free and generic (not typed to IInteractable)
    /// so this small state machine is trivially testable without a scene,
    /// a Collider, or a running Update loop -- feed it a sequence of
    /// candidate/null and assert on Current + the return value.
    /// </summary>
    public sealed class TargetAcquisitionHysteresis<T> where T : class
    {
        readonly int _missFramesToClear;
        int _consecutiveMisses;

        public T Current { get; private set; }

        public TargetAcquisitionHysteresis(int missFramesToClear = 3)
        {
            if (missFramesToClear < 1)
                throw new ArgumentOutOfRangeException(nameof(missFramesToClear), "Must be >= 1.");
            _missFramesToClear = missFramesToClear;
        }

        /// <summary>
        /// Call once per frame with this frame's detection result (null =
        /// no hit, or a hit that isn't a valid target). Returns true when
        /// Current changed as a result of this call (acquired, swapped to a
        /// different target, or cleared after the miss threshold).
        /// </summary>
        public bool Report(T candidate)
        {
            if (candidate != null)
            {
                _consecutiveMisses = 0;
                if (ReferenceEquals(Current, candidate)) return false;
                Current = candidate;
                return true;
            }

            if (Current == null) return false; // already clear, nothing to debounce

            _consecutiveMisses++;
            if (_consecutiveMisses < _missFramesToClear) return false;

            Current = null;
            return true;
        }

        public void Reset()
        {
            Current = null;
            _consecutiveMisses = 0;
        }
    }
}
