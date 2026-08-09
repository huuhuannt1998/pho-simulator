using System;
using System.Collections.Generic;
using Pho.Domain.Events;

namespace Pho.UI.Presenters
{
    /// <summary>
    /// Plain-C# presenter for the first-person interaction prompt (e.g.
    /// "Press E to fill pot"). Subscribes to the frozen
    /// <see cref="InteractionTargetChanged"/> event, published by
    /// PlayerInteractor's spherecast (Pho.Player, architecture §5). Per the
    /// architecture doc, "the UI never raycasts" -- this class only ever
    /// reads the event, it never touches Physics/Camera/etc.
    /// </summary>
    public sealed class InteractionPromptPresenter : IDisposable
    {
        readonly List<IDisposable> _subscriptions = new List<IDisposable>();

        /// <summary>Whether the prompt should currently be visible.</summary>
        public bool ShowPrompt { get; private set; }

        /// <summary>The prompt text to display, e.g. "Press E to fill pot". Empty when ShowPrompt is false.</summary>
        public string PromptText { get; private set; } = string.Empty;

        public void Bind(IEventBus events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            _subscriptions.Add(events.Subscribe<InteractionTargetChanged>(OnInteractionTargetChanged));
        }

        void OnInteractionTargetChanged(InteractionTargetChanged evt)
        {
            ShowPrompt = evt.HasTarget;
            PromptText = evt.PromptText;
        }

        public void Dispose()
        {
            foreach (var sub in _subscriptions) sub.Dispose();
            _subscriptions.Clear();
        }
    }
}
