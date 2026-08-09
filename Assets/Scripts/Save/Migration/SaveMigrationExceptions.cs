using System;

namespace Pho.Save.Migration
{
    /// <summary>
    /// The save file declares a schemaVersion newer than anything this build
    /// understands. This can happen if a player rolls back to an older game
    /// build after saving with a newer one. We must never guess our way
    /// through an unknown future format -- reject cleanly instead of
    /// mis-migrating. Callers (SaveCoordinator) catch this and fall back to
    /// the backup slot / a fresh game, never crash.
    /// </summary>
    public sealed class SaveVersionTooNewException : Exception
    {
        public int FoundVersion { get; }
        public int CurrentVersion { get; }

        public SaveVersionTooNewException(int foundVersion, int currentVersion)
            : base($"Save schemaVersion {foundVersion} is newer than the supported current version {currentVersion}.")
        {
            FoundVersion = foundVersion;
            CurrentVersion = currentVersion;
        }
    }

    /// <summary>
    /// No registered ISaveMigration starts at the save's (possibly
    /// zero-defaulted, see SaveMigrator) schema version, so the chain to
    /// the current version cannot be completed. Treated as corruption by
    /// SaveCoordinator.
    /// </summary>
    public sealed class SaveMigrationMissingException : Exception
    {
        public int FromVersion { get; }

        public SaveMigrationMissingException(int fromVersion)
            : base($"No migration registered starting at schemaVersion {fromVersion}.")
        {
            FromVersion = fromVersion;
        }
    }
}
