using System;
using Pho.Domain.Contracts;

namespace Pho.Domain.DayCycle
{
    /// <summary>Matches WorldDto.phase strings "Prep|Open|Closed" (architecture.md §4).</summary>
    public enum DayPhase
    {
        Prep, Open, Closed
    }

    /// <summary>
    /// Pure day/time state. Shape from architecture.md's M11 row + §4's
    /// "save only at a day boundary" framing; the exact phase-transition
    /// rules are this pass's judgment call (not specified beyond that),
    /// documented here:
    ///
    /// <list type="bullet">
    /// <item>A day always starts in <see cref="DayPhase.Prep"/>.</item>
    /// <item><see cref="DayPhase.Prep"/> -&gt; <see cref="DayPhase.Open"/> only
    /// via explicit <see cref="OpenRestaurant"/> -- never automatic.</item>
    /// <item><see cref="DayPhase.Open"/> -&gt; <see cref="DayPhase.Closed"/> only
    /// via explicit <see cref="CloseRestaurant"/>.</item>
    /// <item><see cref="Tick"/> advances <see cref="TimeOfDaySeconds"/>
    /// regardless of phase (time still passes during Prep/Closed). Wrapping
    /// past <c>cfg.DayLengthSeconds</c> increments <see cref="Day"/>, carries
    /// the remainder into the new <see cref="TimeOfDaySeconds"/> (not a hard
    /// reset to 0 -- this is what makes many-small-ticks equivalent to one
    /// big tick), and resets <see cref="Phase"/> to <see cref="DayPhase.Prep"/>.
    /// The player must explicitly reopen next day; opening never happens
    /// automatically.</item>
    /// <item><see cref="OpenRestaurant"/> is a no-op if already Open; rejects
    /// (throws) from Closed -- reopening the same day without closing the
    /// books first is a bug, not a legitimate flow. Wait for the next day's
    /// Prep.</item>
    /// <item><see cref="CloseRestaurant"/> is a no-op if already Closed;
    /// rejects (throws) from Prep -- nothing to close.</item>
    /// </list>
    ///
    /// This class does not publish <c>DayEnded</c> itself -- that's a
    /// Pho.Core-side service's job (wiring DayClock to IEventBus), out of
    /// scope for this pure-logic pass.
    /// </summary>
    public sealed class DayClock
    {
        public int Day { get; private set; }
        public float TimeOfDaySeconds { get; private set; }
        public DayPhase Phase { get; private set; }

        public DayClock(int startDay, IBalanceConfig cfg)
        {
            Day = startDay;
            TimeOfDaySeconds = 0f;
            Phase = DayPhase.Prep;
        }

        public void Tick(float dt, IBalanceConfig cfg)
        {
            if (dt <= 0f)
                return;

            float dayLength = cfg.DayLengthSeconds > 0f ? cfg.DayLengthSeconds : 1f;
            TimeOfDaySeconds += dt;

            while (TimeOfDaySeconds >= dayLength)
            {
                TimeOfDaySeconds -= dayLength;
                Day += 1;
                Phase = DayPhase.Prep;
            }
        }

        /// <summary>
        /// Directly sets state from a save file, bypassing the normal
        /// Open/CloseRestaurant transition guards (a loaded save is not a
        /// gameplay transition -- it can legitimately jump straight into any
        /// phase, e.g. resuming mid-Open).
        /// </summary>
        public void RestoreState(int day, float timeOfDaySeconds, DayPhase phase)
        {
            Day = day;
            TimeOfDaySeconds = timeOfDaySeconds;
            Phase = phase;
        }

        public void OpenRestaurant()
        {
            if (Phase == DayPhase.Open)
                return;

            if (Phase == DayPhase.Closed)
                throw new InvalidOperationException("Cannot reopen after Closed -- wait for the next day's Prep phase.");

            Phase = DayPhase.Open;
        }

        public void CloseRestaurant()
        {
            if (Phase == DayPhase.Closed)
                return;

            if (Phase == DayPhase.Prep)
                throw new InvalidOperationException("Cannot close from Prep -- the restaurant was never opened today.");

            Phase = DayPhase.Closed;
        }
    }
}
