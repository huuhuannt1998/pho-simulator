namespace Pho.Save.Io
{
    /// <summary>
    /// Pure -- System.IO only, no UnityEngine. Write is expected to be
    /// crash-safe: tmp -> flush -> backup rotate -> atomic move. See
    /// FileSaveStore for the real implementation and InMemorySaveStore for
    /// the test double.
    /// </summary>
    public interface ISaveStore
    {
        bool TryRead(string slot, out string json);
        void Write(string slot, string json);
        bool TryReadBackup(string slot, out string json);
    }
}
