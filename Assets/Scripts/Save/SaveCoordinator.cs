using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pho.Domain.Contracts;
using Pho.Domain.Events;
using Pho.Save.Dto;
using Pho.Save.Events;
using Pho.Save.Io;
using Pho.Save.Migration;
using Pho.Save.Participation;

namespace Pho.Save
{
    /// <summary>
    /// Ties ISaveStore + SaveMigrator + the ISaveParticipant list together.
    /// Not named in architecture.md (which only specifies the pieces it
    /// wires); this is the small orchestration class that fills that gap.
    ///
    /// Save: builds a SaveFile, calls Capture on each participant (in
    /// RestoreOrder, for determinism -- order doesn't matter for Capture
    /// today, but sharing one order avoids a second policy to keep in sync),
    /// serializes with Newtonsoft, and writes it via the store.
    ///
    /// Load: reads via store.TryRead, falling back to store.TryReadBackup
    /// on ANY failure (missing file, invalid JSON, unmigratable/future
    /// schema version, or a Restore participant throwing). Deserializes to
    /// a JObject first, not straight to SaveFile, so SaveMigrator can run on
    /// the raw tree before the final ToObject&lt;SaveFile&gt;(). If both
    /// primary and backup fail, TryLoad returns false and publishes a single
    /// SaveCorrupted event -- this is never a hard crash, per
    /// architecture.md section 4.
    /// </summary>
    public sealed class SaveCoordinator
    {
        public const int CurrentSchemaVersion = 1;

        readonly ISaveStore _store;
        readonly SaveMigrator _migrator;
        readonly IReadOnlyList<ISaveParticipant> _participants;
        readonly IEventBus _eventBus;
        readonly string _gameVersion;

        public SaveCoordinator(
            ISaveStore store,
            SaveMigrator migrator,
            IEnumerable<ISaveParticipant> participants,
            IEventBus eventBus = null,
            string gameVersion = "0.0.0")
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _migrator = migrator ?? throw new ArgumentNullException(nameof(migrator));
            _participants = (participants ?? Array.Empty<ISaveParticipant>())
                .OrderBy(p => p.RestoreOrder)
                .ToList();
            _eventBus = eventBus;
            _gameVersion = gameVersion ?? "0.0.0";
        }

        /// <summary>
        /// Builds a SaveFile from the registered participants and writes it
        /// to the given slot. savedAtUnixSeconds is supplied by the caller
        /// (Pho.Core, via real wall-clock time) rather than read here, so
        /// this class stays pure and deterministic in tests.
        /// </summary>
        public SaveFile Save(string slot, long savedAtUnixSeconds)
        {
            var save = NewEmptySaveFile(savedAtUnixSeconds, _gameVersion);

            foreach (var participant in _participants)
            {
                participant.Capture(save);
            }

            var json = JsonConvert.SerializeObject(save, Formatting.Indented);
            _store.Write(slot, json);
            return save;
        }

        /// <summary>
        /// Attempts to load and restore a save. Never throws: any failure
        /// on the primary slot falls back to the backup; if the backup also
        /// fails, this returns false (and publishes SaveCorrupted) so the
        /// caller can start a new game.
        /// </summary>
        public bool TryLoad(string slot, IGameDatabase db, out SaveFile save)
        {
            if (TryLoadFrom(slot, primary: true, db, out save, out var primaryError))
            {
                return true;
            }

            if (TryLoadFrom(slot, primary: false, db, out save, out var backupError))
            {
                return true;
            }

            save = null;
            var reason = backupError?.Message ?? primaryError?.Message ?? "no valid save found";
            _eventBus?.Publish(new SaveCorrupted(slot, reason));
            return false;
        }

        bool TryLoadFrom(string slot, bool primary, IGameDatabase db, out SaveFile save, out Exception error)
        {
            save = null;
            error = null;

            string json;
            var has = primary ? _store.TryRead(slot, out json) : _store.TryReadBackup(slot, out json);
            if (!has || string.IsNullOrWhiteSpace(json))
            {
                error = new InvalidOperationException(primary ? "Primary slot missing or empty." : "Backup slot missing or empty.");
                return false;
            }

            try
            {
                var root = JObject.Parse(json);
                var migrated = _migrator.MigrateToCurrent(root, CurrentSchemaVersion);
                var loaded = migrated.ToObject<SaveFile>();
                if (loaded == null)
                {
                    error = new InvalidOperationException("Deserialized save was null.");
                    return false;
                }

                foreach (var participant in _participants)
                {
                    participant.Restore(loaded, db);
                }

                save = loaded;
                return true;
            }
            catch (Exception ex)
            {
                // Any exception during parse/migrate/deserialize/restore is
                // treated as corruption -- see architecture.md section 4:
                // "primary -> on any exception, .bak".
                error = ex;
                return false;
            }
        }

        static SaveFile NewEmptySaveFile(long savedAtUnixSeconds, string gameVersion) => new SaveFile
        {
            schemaVersion = CurrentSchemaVersion,
            gameVersion = gameVersion,
            savedAtUnixSeconds = savedAtUnixSeconds,
            world = new WorldDto(),
            economy = new EconomyDto { recentDays = new List<DailyReportDto>() },
            inventory = new InventoryDto { lots = new List<LotDto>() },
            progression = new ProgressionDto
            {
                unlockedRecipeIds = new List<string>(),
                ownedEquipmentIds = new List<string>(),
                menuPrices = new Dictionary<string, decimal>(),
                flags = new Dictionary<string, bool>()
            },
            restaurant = new RestaurantDto { dirtyTableIds = new List<string>() },
            player = new PlayerDto { position = new Vec3Dto() }
        };
    }
}
