using Newtonsoft.Json.Linq;

namespace Pho.Save.Migration
{
    /// <summary>
    /// One step in the save-format upgrade chain. Migrations operate on the
    /// raw JSON tree (not the DTO classes) so we never have to keep N
    /// generations of SaveFile POCOs alive -- see architecture.md section 4.
    /// </summary>
    public interface ISaveMigration
    {
        /// <summary>Migrates a save at this schema version to FromVersion + 1.</summary>
        int FromVersion { get; }

        /// <summary>Mutates the raw JSON tree in place.</summary>
        void Migrate(JObject root);
    }
}
