using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Pho.Save.Migration
{
    /// <summary>
    /// Applies the registered ISaveMigration chain to a raw JSON tree until
    /// it reaches currentVersion, then stamps schemaVersion. Generic
    /// machinery only -- no concrete migrations ship here; schema version 1
    /// is the first version, so the production migration list is empty for
    /// this pass (see ISaveMigration doc comment for the "why raw JObject"
    /// rationale).
    ///
    /// Decision: a save with a MISSING schemaVersion field is treated as
    /// version 0 rather than rejected outright at this layer -- this keeps
    /// "no schemaVersion" and "an old, pre-migration schemaVersion" on the
    /// same code path. In practice, since no migration is registered for
    /// FromVersion 0 in this pass, a missing-schemaVersion save still fails
    /// cleanly (SaveMigrationMissingException) and SaveCoordinator treats
    /// that exactly like any other corrupt save: fall back to .bak, and if
    /// that also fails, report "no valid save" without throwing.
    /// </summary>
    public sealed class SaveMigrator
    {
        readonly Dictionary<int, ISaveMigration> _byFromVersion;

        public SaveMigrator(IEnumerable<ISaveMigration> migrations)
        {
            _byFromVersion = new Dictionary<int, ISaveMigration>();
            if (migrations == null) return;

            foreach (var migration in migrations)
            {
                if (migration == null) continue;
                _byFromVersion[migration.FromVersion] = migration;
            }
        }

        /// <summary>
        /// Reads root["schemaVersion"], applies the migration chain step by
        /// step until currentVersion is reached, and sets
        /// root["schemaVersion"] = currentVersion. Mutates and returns root.
        /// </summary>
        public JObject MigrateToCurrent(JObject root, int currentVersion)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            var version = ReadSchemaVersion(root);

            if (version > currentVersion)
            {
                throw new SaveVersionTooNewException(version, currentVersion);
            }

            var guard = 0;
            while (version < currentVersion)
            {
                if (!_byFromVersion.TryGetValue(version, out var migration))
                {
                    throw new SaveMigrationMissingException(version);
                }

                migration.Migrate(root);
                version++;
                root["schemaVersion"] = version;

                guard++;
                if (guard > 1000)
                {
                    // Defensive only -- a well-formed migration set can never
                    // hit this; it guards against a mis-registered migration
                    // whose FromVersion doesn't actually advance the chain.
                    throw new InvalidOperationException("Migration chain exceeded safety bound; check for a non-advancing migration.");
                }
            }

            root["schemaVersion"] = currentVersion;
            return root;
        }

        static int ReadSchemaVersion(JObject root)
        {
            var token = root["schemaVersion"];
            if (token == null || token.Type == JTokenType.Null)
            {
                return 0; // missing schemaVersion -> treated as version 0, see class doc comment
            }

            return token.Value<int>();
        }
    }
}
