using System;
using System.IO;
using NUnit.Framework;
using Pho.Save.Io;

namespace Pho.Save.Tests
{
    // Exercises the real filesystem path -- the Tier-1 suites named in
    // architecture.md section 11 (SaveRoundTripTests/SaveMigrationTests/
    // SaveCorruptionTests) all use InMemorySaveStore, so nothing else
    // proves FileSaveStore's tmp -> flush -> backup rotate -> atomic move
    // sequence actually works against disk.
    [TestFixture]
    public class FileSaveStoreTests
    {
        string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "PhoSaveTests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Test]
        public void Write_ThenTryRead_RoundTripsExactJson()
        {
            var store = new FileSaveStore(_root);
            store.Write("slot0", "{\"schemaVersion\":1}");

            var ok = store.TryRead("slot0", out var json);

            Assert.That(ok, Is.True);
            Assert.That(json, Is.EqualTo("{\"schemaVersion\":1}"));
        }

        [Test]
        public void TryRead_NoFileWritten_ReturnsFalse()
        {
            var store = new FileSaveStore(_root);
            var ok = store.TryRead("slot0", out var json);

            Assert.That(ok, Is.False);
            Assert.That(json, Is.Null);
        }

        [Test]
        public void SecondWrite_RotatesFirstWriteIntoBackup()
        {
            var store = new FileSaveStore(_root);
            store.Write("slot0", "{\"v\":1}");
            store.Write("slot0", "{\"v\":2}");

            store.TryRead("slot0", out var primary);
            store.TryReadBackup("slot0", out var backup);

            Assert.That(primary, Is.EqualTo("{\"v\":2}"));
            Assert.That(backup, Is.EqualTo("{\"v\":1}"));
        }

        [Test]
        public void ThirdWrite_OverwritesBackup_KeepsOnlyOneGeneration()
        {
            var store = new FileSaveStore(_root);
            store.Write("slot0", "{\"v\":1}");
            store.Write("slot0", "{\"v\":2}");
            store.Write("slot0", "{\"v\":3}");

            store.TryRead("slot0", out var primary);
            store.TryReadBackup("slot0", out var backup);

            Assert.That(primary, Is.EqualTo("{\"v\":3}"));
            Assert.That(backup, Is.EqualTo("{\"v\":2}"));
        }

        [Test]
        public void Write_DoesNotLeaveTempFileBehind()
        {
            var store = new FileSaveStore(_root);
            store.Write("slot0", "{\"v\":1}");

            var tempPath = Path.Combine(_root, "slot0.sav.tmp");
            Assert.That(File.Exists(tempPath), Is.False);
        }

        [Test]
        public void Constructor_NullOrEmptyRootPath_Throws()
        {
            Assert.That(() => new FileSaveStore(null), Throws.ArgumentException);
            Assert.That(() => new FileSaveStore(string.Empty), Throws.ArgumentException);
        }
    }
}
