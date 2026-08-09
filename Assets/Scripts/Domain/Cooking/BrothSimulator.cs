using System.Collections.Generic;
using Pho.Domain.Contracts;
using Pho.Domain.MathUtil;

namespace Pho.Domain.Cooking
{
    /// <summary>Lifecycle phase of a pot of broth. See BrothSimulator.</summary>
    public enum BrothPhase
    {
        Empty,
        Filling,
        Heating,
        Simmering,
        Ready,
        Overcooked,
    }

    /// <summary>Mutable runtime state of one pot of broth. See architecture.md section 7.</summary>
    public struct BrothState
    {
        public float VolumeLiters;
        public float Quality01;
        public float TemperatureC;
        public float SimmerSeconds;
        public BrothPhase Phase;
    }

    /// <summary>
    /// Modifiers derived from the equipment currently in use (see IEquipmentDef
    /// in Domain/Contracts/DefInterfaces.cs -- HeatRateMultiplier and
    /// CapacityMultiplier are its fields). architecture.md section 7 names
    /// this type ("produced from the owned EquipmentData") but doesn't spell
    /// out its shape beyond "heatRateMultiplier 1.0 -> 1.5"; mirroring
    /// IEquipmentDef's two relevant fields is the smallest faithful shape.
    /// </summary>
    public readonly struct EquipmentModifiers
    {
        public readonly float HeatRateMultiplier;
        public readonly float CapacityMultiplier;

        public EquipmentModifiers(float heatRateMultiplier, float capacityMultiplier)
        {
            HeatRateMultiplier = heatRateMultiplier;
            CapacityMultiplier = capacityMultiplier;
        }

        /// <summary>Unupgraded equipment: no bonus, no penalty.</summary>
        public static readonly EquipmentModifiers Default = new EquipmentModifiers(1f, 1f);
    }

    /// <summary>
    /// Pure simulator advancing a pot of broth through its lifecycle. See
    /// architecture.md section 7.
    /// </summary>
    public static class BrothSimulator
    {
        // TODO(balance): IBalanceConfig has no broth-timing section today (it
        // is frozen for this pass -- see the same note on QualityCalculator).
        // These are clearly-marked placeholder constants pending a real config
        // surface, not literals scattered ad hoc through the tick logic. Each
        // is `public const` (not private) specifically so
        // BrothSimulatorTests can assert against the real thresholds instead
        // of duplicating guessed numbers.
        //
        // BrothState has no generic "time in current phase" field (only
        // VolumeLiters, Quality01, TemperatureC, SimmerSeconds, Phase per the
        // frozen shape), so progress within Filling/Heating is tracked using
        // the field that phase is naturally filling: VolumeLiters ramps up
        // during Filling, TemperatureC ramps up during Heating, and
        // SimmerSeconds accumulates through both Simmering and Ready (so it
        // doubles as the Ready -> Overcooked timer too).
        public const float TargetVolumeLiters = 1f;
        public const float FillingDurationSeconds = 10f;
        public const float AmbientTemperatureC = 22f;
        public const float SimmerTemperatureC = 95f;
        public const float HeatingDurationSeconds = 30f;
        public const float SimmerDurationSecondsToReady = 60f;
        public const float OvercookDurationSeconds = 45f;
        public const float OvercookQualityDecaySeconds = 60f;

        // TODO(balance): same placeholder-pending-config status as the timing
        // constants above. Simple linear contribution: each aromatic
        // contributes proportionally to how much of it was used and how good
        // it was, scaled down so a realistic handful of aromatics tops out
        // near +1.0 (fully clamped) rather than trivially saturating.
        public const float AromaticContributionScale = 0.1f;

        /// <summary>
        /// Advances the broth by dt simulated seconds. A non-positive dt is a
        /// no-op. heatRateMultiplier speeds up both the Heating ramp and the
        /// Simmering-to-Ready countdown -- per architecture.md section 7
        /// ("heatRateMultiplier 1.0 -> 1.5") and M13's acceptance criteria
        /// ("cuts measured broth-ready time"), a hotter burner has to reduce
        /// real (wall-clock/simulated) time-to-Ready, not just raise the
        /// temperature curve's slope while leaving the total simmer time
        /// fixed.
        /// </summary>
        public static void Tick(ref BrothState s, float dt, in EquipmentModifiers eq, IBalanceConfig cfg)
        {
            if (dt <= 0f)
                return;

            float heatRate = eq.HeatRateMultiplier > 0f ? eq.HeatRateMultiplier : 1f;

            switch (s.Phase)
            {
                case BrothPhase.Empty:
                    // Starting to use the pot begins filling; the rest of dt
                    // is not applied to Filling in the same tick to keep the
                    // phase machine simple and strictly ordered.
                    s.Phase = BrothPhase.Filling;
                    s.TemperatureC = AmbientTemperatureC;
                    break;

                case BrothPhase.Filling:
                {
                    float fillRate = TargetVolumeLiters / FillingDurationSeconds;
                    s.VolumeLiters = MathP.Clamp(s.VolumeLiters + fillRate * dt, 0f, TargetVolumeLiters);
                    if (s.VolumeLiters >= TargetVolumeLiters)
                        s.Phase = BrothPhase.Heating;
                    break;
                }

                case BrothPhase.Heating:
                {
                    float heatRise = (SimmerTemperatureC - AmbientTemperatureC) / HeatingDurationSeconds * heatRate;
                    s.TemperatureC = MathP.Clamp(s.TemperatureC + heatRise * dt, AmbientTemperatureC, SimmerTemperatureC);
                    if (s.TemperatureC >= SimmerTemperatureC)
                        s.Phase = BrothPhase.Simmering;
                    break;
                }

                case BrothPhase.Simmering:
                {
                    s.TemperatureC = SimmerTemperatureC;
                    s.SimmerSeconds += dt * heatRate;
                    s.Quality01 = MathP.Clamp01(s.SimmerSeconds / SimmerDurationSecondsToReady);
                    if (s.SimmerSeconds >= SimmerDurationSecondsToReady)
                        s.Phase = BrothPhase.Ready;
                    break;
                }

                case BrothPhase.Ready:
                {
                    s.SimmerSeconds += dt * heatRate;
                    if (s.SimmerSeconds >= SimmerDurationSecondsToReady + OvercookDurationSeconds)
                        s.Phase = BrothPhase.Overcooked;
                    break;
                }

                case BrothPhase.Overcooked:
                {
                    s.SimmerSeconds += dt * heatRate;
                    float overcookSeconds = s.SimmerSeconds - (SimmerDurationSecondsToReady + OvercookDurationSeconds);
                    s.Quality01 = MathP.Clamp01(1f - overcookSeconds / OvercookQualityDecaySeconds);
                    break;
                }
            }
        }

        /// <summary>
        /// Sum of amount*quality across the given aromatics (onion, star
        /// anise, cinnamon, etc. per the GDD), scaled and clamped to 0..1.
        /// Non-Aromatic entries in the list are ignored defensively -- the
        /// caller is expected to pass only aromatic components, but filtering
        /// here costs nothing and avoids a silent miscount if it passes the
        /// whole bowl by mistake.
        /// </summary>
        public static float AromaticContribution(IReadOnlyList<BowlComponent> aromatics, IBalanceConfig cfg)
        {
            float sum = 0f;
            for (int i = 0; i < aromatics.Count; i++)
            {
                var a = aromatics[i];
                if (a.Slot != ComponentSlot.Aromatic)
                    continue;

                sum += a.Amount * a.Quality01;
            }

            return MathP.Clamp01(sum * AromaticContributionScale);
        }
    }
}
