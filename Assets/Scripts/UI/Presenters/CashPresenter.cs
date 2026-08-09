using System;
using System.Collections.Generic;
using Pho.Domain.Events;

namespace Pho.UI.Presenters
{
    /// <summary>
    /// Plain-C# HUD presenter for the "Cash" element (GDD §43). Subscribes to
    /// the frozen <see cref="CashChanged"/> event and exposes the current
    /// balance plus the most recent delta/category for a future "+$9.50"
    /// popup. No MonoBehaviour dependency -- a future uGUI/UI Toolkit view
    /// polls these properties (or the integration layer wires a manual
    /// refresh) rather than this class touching any Unity UI type.
    /// </summary>
    public sealed class CashPresenter : IDisposable
    {
        readonly List<IDisposable> _subscriptions = new List<IDisposable>();

        /// <summary>Current known cash balance. Zero until the first CashChanged arrives.</summary>
        public decimal Cash { get; private set; }

        /// <summary>Signed delta from the most recent CashChanged (for a transient popup).</summary>
        public decimal LastDelta { get; private set; }

        /// <summary>Ledger category of the most recent change (Sale, Rent, Tip, ...).</summary>
        public LedgerCategory LastCategory { get; private set; }

        /// <summary>False until at least one CashChanged has been observed -- lets a view distinguish "no data yet" from "balance is 0".</summary>
        public bool HasReceivedUpdate { get; private set; }

        /// <summary>
        /// Subscribes to the event bus. Call once the bus is available;
        /// deliberately takes the interface directly (not GameContext) so
        /// this class stays testable with a bare EventBus and agnostic to
        /// however a future MonoBehaviour host resolves its dependencies.
        /// </summary>
        public void Bind(IEventBus events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            _subscriptions.Add(events.Subscribe<CashChanged>(OnCashChanged));
        }

        void OnCashChanged(CashChanged evt)
        {
            Cash = evt.NewBalance;
            LastDelta = evt.Delta;
            LastCategory = evt.Category;
            HasReceivedUpdate = true;
        }

        /// <summary>Unsubscribes from every event this presenter registered. Always dispose when the owning view goes away.</summary>
        public void Dispose()
        {
            foreach (var sub in _subscriptions) sub.Dispose();
            _subscriptions.Clear();
        }
    }
}
