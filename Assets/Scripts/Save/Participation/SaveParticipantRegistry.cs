using System;
using System.Collections.Generic;

namespace Pho.Save.Participation
{
    /// <summary>
    /// Collects <see cref="ISaveParticipant"/> instances as each owning
    /// service's own installer registers itself -- see ISaveParticipant's
    /// class doc comment ("no shared save-wiring file"). Registering INTO
    /// this is a one-line call from each service's own installer, not an
    /// edit to a central per-participant list; the registry itself is
    /// shared infrastructure (like GameContext), installed once, early
    /// (InstallOrder.Save), before any participant's own installer runs.
    /// </summary>
    public sealed class SaveParticipantRegistry
    {
        readonly List<ISaveParticipant> _participants = new List<ISaveParticipant>();

        public void Register(ISaveParticipant participant)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            _participants.Add(participant);
        }

        public IReadOnlyList<ISaveParticipant> Participants => _participants;
    }
}
