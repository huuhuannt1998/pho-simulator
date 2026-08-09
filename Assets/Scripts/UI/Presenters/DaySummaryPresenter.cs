using System;
using System.Collections.Generic;
using Pho.Domain.Events;

namespace Pho.UI.Presenters
{
    /// <summary>
    /// Plain-C# presenter for the end-of-day summary screen. Subscribes to
    /// the frozen <see cref="DayEnded"/> event and exposes the latest
    /// <see cref="DailyReport"/> as-is.
    ///
    /// DailyReport (EconomyEvents.cs) is explicitly documented as a M1
    /// placeholder shape whose fields get populated for real at M11
    /// (DayCycleService) -- this presenter deliberately does no extra
    /// formatting/derivation beyond exposing what's on the event, so it
    /// doesn't need to change when M11 fills the report in.
    /// </summary>
    public sealed class DaySummaryPresenter : IDisposable
    {
        readonly List<IDisposable> _subscriptions = new List<IDisposable>();

        /// <summary>False until the first DayEnded has been observed.</summary>
        public bool HasReport { get; private set; }

        /// <summary>Day number from the most recent DayEnded.</summary>
        public int Day { get; private set; }

        /// <summary>The most recently received DailyReport, or null before the first DayEnded.</summary>
        public DailyReport LatestReport { get; private set; }

        public void Bind(IEventBus events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            _subscriptions.Add(events.Subscribe<DayEnded>(OnDayEnded));
        }

        void OnDayEnded(DayEnded evt)
        {
            Day = evt.Day;
            LatestReport = evt.Report;
            HasReport = true;
        }

        public void Dispose()
        {
            foreach (var sub in _subscriptions) sub.Dispose();
            _subscriptions.Clear();
        }
    }
}
