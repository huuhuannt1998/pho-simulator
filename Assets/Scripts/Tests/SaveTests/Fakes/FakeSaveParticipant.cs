using System;
using System.Collections.Generic;
using Pho.Domain.Contracts;
using Pho.Save.Dto;
using Pho.Save.Participation;

namespace Pho.Save.Tests.Fakes
{
    /// <summary>
    /// Records Capture/Restore calls (and their order relative to sibling
    /// participants, via the shared CallLog) so SaveRoundTripTests can prove
    /// SaveCoordinator's wiring -- not just DTO serialization -- works.
    /// </summary>
    sealed class FakeSaveParticipant : ISaveParticipant
    {
        readonly string _name;
        readonly Action<SaveFile> _onCapture;
        readonly Action<SaveFile, IGameDatabase> _onRestore;

        public int RestoreOrder { get; }
        public List<string> CallLog { get; }

        public FakeSaveParticipant(
            string name,
            int restoreOrder,
            List<string> callLog,
            Action<SaveFile> onCapture = null,
            Action<SaveFile, IGameDatabase> onRestore = null)
        {
            _name = name;
            RestoreOrder = restoreOrder;
            CallLog = callLog;
            _onCapture = onCapture;
            _onRestore = onRestore;
        }

        public void Capture(SaveFile save)
        {
            CallLog.Add($"capture:{_name}");
            _onCapture?.Invoke(save);
        }

        public void Restore(SaveFile save, IGameDatabase db)
        {
            CallLog.Add($"restore:{_name}");
            _onRestore?.Invoke(save, db);
        }
    }
}
