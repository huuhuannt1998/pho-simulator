using System.Collections.Generic;

namespace Pho.Save.Io
{
    /// <summary>
    /// Dictionary-backed ISaveStore for tests. Write() rotates the previous
    /// primary contents into the backup slot, mirroring FileSaveStore's
    /// tmp -> flush -> backup rotate -> atomic move sequence closely enough
    /// that SaveCoordinator behaves identically against either store.
    /// The SetRaw*/Remove* helpers exist purely so SaveCorruptionTests can
    /// stage truncated/invalid/missing content without touching disk.
    /// </summary>
    public sealed class InMemorySaveStore : ISaveStore
    {
        readonly Dictionary<string, string> _primary = new Dictionary<string, string>();
        readonly Dictionary<string, string> _backup = new Dictionary<string, string>();

        public bool TryRead(string slot, out string json) => _primary.TryGetValue(slot, out json);

        public bool TryReadBackup(string slot, out string json) => _backup.TryGetValue(slot, out json);

        public void Write(string slot, string json)
        {
            if (_primary.TryGetValue(slot, out var existing))
            {
                _backup[slot] = existing;
            }

            _primary[slot] = json;
        }

        // --- Test-only staging helpers (corruption / missing-file scenarios) ---

        public void SetRaw(string slot, string json) => _primary[slot] = json;

        public void SetRawBackup(string slot, string json) => _backup[slot] = json;

        public void RemovePrimary(string slot) => _primary.Remove(slot);

        public void RemoveBackup(string slot) => _backup.Remove(slot);
    }
}
