using System;
using System.Collections.Generic;
using NUnit.Framework;
using Pho.Domain.Events;
using Pho.Domain.Restaurant;

namespace Pho.Domain.Tests
{
    // Plain NUnit only (no UnityEngine.TestTools / [UnityTest]) -- this file
    // is compiled both by Unity's EditMode test runner and by
    // Tools/PhoDomain.Tests.csproj via `dotnet test`. Constraint-based
    // Assert.That(...) throughout, matching InventoryModelTests.cs style.
    // NEVER classic assertions (Assert.AreEqual/IsTrue): NUnit 4.x moved
    // those to a Legacy namespace Unity's bundled NUnit does not have.
    [TestFixture]
    public class CleanlinessTests
    {
        const string T1 = "table.1";
        const string T2 = "table.2";
        const string T3 = "table.3";
        const string T4 = "table.4";

        /// <summary>
        /// Collects published events so tests can assert on publication
        /// without a hand-rolled fake bus per test. Uses the real EventBus --
        /// it is pure, so it runs under `dotnet test` like everything else.
        /// </summary>
        sealed class Recorder
        {
            public readonly List<TableDirtied> Dirtied = new List<TableDirtied>();
            public readonly List<TableCleaned> Cleaned = new List<TableCleaned>();
            public readonly List<CleanlinessChanged> Changed = new List<CleanlinessChanged>();

            public Recorder(IEventBus bus)
            {
                bus.Subscribe<TableDirtied>(e => Dirtied.Add(e));
                bus.Subscribe<TableCleaned>(e => Cleaned.Add(e));
                bus.Subscribe<CleanlinessChanged>(e => Changed.Add(e));
            }
        }

        static CleanlinessModel WithBus(out Recorder recorder)
        {
            var bus = new EventBus();
            recorder = new Recorder(bus);
            return new CleanlinessModel(bus);
        }

        // ---------------------------------------------------------------
        // Derivation: clamp01(1 - dirty/total)
        // ---------------------------------------------------------------

        [Test]
        public void NewModel_HasNoTablesNoDirt_AndReadsSpotless()
        {
            var model = new CleanlinessModel();

            Assert.That(model.TotalTables, Is.EqualTo(0));
            Assert.That(model.DirtyTableCount, Is.EqualTo(0));
            Assert.That(model.Cleanliness01, Is.EqualTo(1f));
        }

        [Test]
        public void AllTablesClean_ReadsSpotless()
        {
            var model = new CleanlinessModel();
            model.SetTotalTables(4);

            Assert.That(model.Cleanliness01, Is.EqualTo(1f));
        }

        [Test]
        public void OneDirtyTableOfFour_ReadsThreeQuarters()
        {
            var model = new CleanlinessModel();
            model.SetTotalTables(4);

            model.MarkDirty(T1);

            Assert.That(model.Cleanliness01, Is.EqualTo(0.75f).Within(1e-5f));
        }

        [Test]
        public void EveryTableDirty_ReadsZero()
        {
            var model = new CleanlinessModel();
            model.SetTotalTables(4);

            model.MarkDirty(T1);
            model.MarkDirty(T2);
            model.MarkDirty(T3);
            model.MarkDirty(T4);

            Assert.That(model.Cleanliness01, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void MoreDirtyIdsThanTables_ClampsAtZero_NeverNegative()
        {
            // Reachable in practice: a save taken with a bigger dining room
            // restored into a smaller one leaves stale dirty IDs behind.
            var model = new CleanlinessModel();
            model.SetTotalTables(2);

            model.MarkDirty(T1);
            model.MarkDirty(T2);
            model.MarkDirty(T3);
            model.MarkDirty(T4);

            Assert.That(model.Cleanliness01, Is.EqualTo(0f));
        }

        [Test]
        public void ZeroTables_ReadsSpotless_EvenWithDirtyIdsPresent()
        {
            // An unconfigured dining room is not a filthy one -- see the
            // model's ZERO-TABLE EDGE CASE doc comment. Reporting 0 here
            // would silently tax every customer for a content gap.
            var model = new CleanlinessModel();
            model.MarkDirty(T1);

            Assert.That(model.TotalTables, Is.EqualTo(0));
            Assert.That(model.DirtyTableCount, Is.EqualTo(1));
            Assert.That(model.Cleanliness01, Is.EqualTo(1f));
        }

        [Test]
        public void SetTotalTables_ChangesTheReading_WithoutTouchingTheDirtySet()
        {
            var model = new CleanlinessModel();
            model.SetTotalTables(2);
            model.MarkDirty(T1);
            Assert.That(model.Cleanliness01, Is.EqualTo(0.5f).Within(1e-5f));

            model.SetTotalTables(4);

            Assert.That(model.Cleanliness01, Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(model.DirtyTableCount, Is.EqualTo(1));
            Assert.That(model.IsDirty(T1), Is.True);
        }

        // ---------------------------------------------------------------
        // MarkDirty / TryClean / IsDirty
        // ---------------------------------------------------------------

        [Test]
        public void MarkDirty_NewTable_ReturnsTrue_AndMarksItDirty()
        {
            var model = new CleanlinessModel();
            model.SetTotalTables(4);

            Assert.That(model.MarkDirty(T1), Is.True);
            Assert.That(model.IsDirty(T1), Is.True);
            Assert.That(model.IsDirty(T2), Is.False);
            Assert.That(model.DirtyTableCount, Is.EqualTo(1));
        }

        [Test]
        public void MarkDirty_AlreadyDirtyTable_ReturnsFalse_DoesNotThrow_AndDoesNotDoubleCount()
        {
            var model = new CleanlinessModel();
            model.SetTotalTables(4);
            model.MarkDirty(T1);

            bool second = true;
            Assert.That(() => second = model.MarkDirty(T1), Throws.Nothing);

            Assert.That(second, Is.False, "re-dirtying a table is a normal runtime occurrence, not an error");
            Assert.That(model.DirtyTableCount, Is.EqualTo(1));
            Assert.That(model.Cleanliness01, Is.EqualTo(0.75f).Within(1e-5f));
        }

        [Test]
        public void TryClean_DirtyTable_ReturnsTrue_AndRestoresCleanliness()
        {
            var model = new CleanlinessModel();
            model.SetTotalTables(4);
            model.MarkDirty(T1);

            Assert.That(model.TryClean(T1), Is.True);
            Assert.That(model.IsDirty(T1), Is.False);
            Assert.That(model.DirtyTableCount, Is.EqualTo(0));
            Assert.That(model.Cleanliness01, Is.EqualTo(1f));
        }

        [Test]
        public void TryClean_AlreadyCleanTable_ReturnsFalse_DoesNotThrow()
        {
            var model = new CleanlinessModel();
            model.SetTotalTables(4);

            bool ok = true;
            Assert.That(() => ok = model.TryClean(T1), Throws.Nothing,
                "cleaning an already-clean table is a normal runtime occurrence -- must not throw");

            Assert.That(ok, Is.False);
            Assert.That(model.Cleanliness01, Is.EqualTo(1f));
        }

        [Test]
        public void TryClean_UnknownTableId_ReturnsFalse()
        {
            var model = new CleanlinessModel();
            model.SetTotalTables(4);
            model.MarkDirty(T1);

            Assert.That(model.TryClean("table.does_not_exist"), Is.False);
            Assert.That(model.DirtyTableCount, Is.EqualTo(1), "a failed clean must not touch the dirty set");
        }

        [Test]
        public void TableIdComparison_IsOrdinal_CaseSensitive()
        {
            var model = new CleanlinessModel();
            model.SetTotalTables(4);
            model.MarkDirty("table.1");

            Assert.That(model.IsDirty("TABLE.1"), Is.False);
            Assert.That(model.TryClean("TABLE.1"), Is.False);
            Assert.That(model.IsDirty("table.1"), Is.True);
        }

        [Test]
        public void DirtyTableIds_ReportsExactlyTheDirtyTables()
        {
            var model = new CleanlinessModel();
            model.SetTotalTables(4);
            model.MarkDirty(T1);
            model.MarkDirty(T3);

            Assert.That(model.DirtyTableIds, Is.EquivalentTo(new[] { T1, T3 }));
        }

        // ---------------------------------------------------------------
        // Programmer errors throw (matches InventoryModel/EconomyService)
        // ---------------------------------------------------------------

        [Test]
        public void MarkDirty_NullOrBlankTableId_Throws()
        {
            var model = new CleanlinessModel();

            Assert.That(() => model.MarkDirty(null), Throws.ArgumentException);
            Assert.That(() => model.MarkDirty(""), Throws.ArgumentException);
            Assert.That(() => model.MarkDirty("   "), Throws.ArgumentException);
        }

        [Test]
        public void TryClean_NullOrBlankTableId_Throws()
        {
            var model = new CleanlinessModel();

            Assert.That(() => model.TryClean(null), Throws.ArgumentException);
            Assert.That(() => model.TryClean(""), Throws.ArgumentException);
        }

        [Test]
        public void IsDirty_NullOrBlankTableId_Throws()
        {
            var model = new CleanlinessModel();

            Assert.That(() => model.IsDirty(null), Throws.ArgumentException);
        }

        [Test]
        public void SetTotalTables_Negative_Throws()
        {
            var model = new CleanlinessModel();

            Assert.That(() => model.SetTotalTables(-1), Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void SetTotalTables_Zero_IsLegal_DoesNotThrow()
        {
            var model = new CleanlinessModel();
            model.SetTotalTables(4);

            Assert.That(() => model.SetTotalTables(0), Throws.Nothing);
            Assert.That(model.TotalTables, Is.EqualTo(0));
        }

        // ---------------------------------------------------------------
        // Clear + save/load restore
        // ---------------------------------------------------------------

        [Test]
        public void Clear_RemovesAllDirt_ButKeepsTotalTables()
        {
            var model = new CleanlinessModel();
            model.SetTotalTables(4);
            model.MarkDirty(T1);
            model.MarkDirty(T2);

            model.Clear();

            Assert.That(model.DirtyTableCount, Is.EqualTo(0));
            Assert.That(model.Cleanliness01, Is.EqualTo(1f));
            Assert.That(model.TotalTables, Is.EqualTo(4), "table count is a property of the dining room, not of its mess");
        }

        [Test]
        public void RestoreDirtyTables_ReplacesTheDirtySetWholesale()
        {
            var model = new CleanlinessModel();
            model.SetTotalTables(4);
            model.MarkDirty(T1);

            model.RestoreDirtyTables(new[] { T2, T3 });

            Assert.That(model.IsDirty(T1), Is.False, "restore must replace, not merge");
            Assert.That(model.DirtyTableIds, Is.EquivalentTo(new[] { T2, T3 }));
            Assert.That(model.Cleanliness01, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void RestoreDirtyTables_Null_ClearsToSpotless_DoesNotThrow()
        {
            var model = new CleanlinessModel();
            model.SetTotalTables(4);
            model.MarkDirty(T1);

            Assert.That(() => model.RestoreDirtyTables(null), Throws.Nothing);

            Assert.That(model.DirtyTableCount, Is.EqualTo(0));
            Assert.That(model.Cleanliness01, Is.EqualTo(1f));
        }

        [Test]
        public void RestoreDirtyTables_SkipsBlankEntries_RatherThanThrowing()
        {
            // A save file is untrusted input (architecture.md section 4:
            // "a missing ID on load is logged and skipped, never a hard
            // crash") -- unlike a direct MarkDirty call from code.
            var model = new CleanlinessModel();
            model.SetTotalTables(4);

            Assert.That(() => model.RestoreDirtyTables(new[] { T1, null, "", "  ", T2 }), Throws.Nothing);

            Assert.That(model.DirtyTableIds, Is.EquivalentTo(new[] { T1, T2 }));
        }

        [Test]
        public void RestoreDirtyTables_IgnoresDuplicateIds()
        {
            var model = new CleanlinessModel();
            model.SetTotalTables(4);

            model.RestoreDirtyTables(new[] { T1, T1, T2 });

            Assert.That(model.DirtyTableCount, Is.EqualTo(2));
        }

        [Test]
        public void SaveRestoreRoundTrip_ReproducesTheSameReading()
        {
            var original = new CleanlinessModel();
            original.SetTotalTables(4);
            original.MarkDirty(T1);
            original.MarkDirty(T3);

            // Exactly what CleanlinessService.Capture/Restore does.
            var captured = new List<string>(original.DirtyTableIds);

            var loaded = new CleanlinessModel();
            loaded.SetTotalTables(4);
            loaded.RestoreDirtyTables(captured);

            Assert.That(loaded.Cleanliness01, Is.EqualTo(original.Cleanliness01).Within(1e-5f));
            Assert.That(loaded.DirtyTableIds, Is.EquivalentTo(original.DirtyTableIds));
        }

        // ---------------------------------------------------------------
        // Events
        // ---------------------------------------------------------------

        [Test]
        public void NullEventBus_PublishesNothing_AndNeverThrows()
        {
            var model = new CleanlinessModel(null);
            model.SetTotalTables(2);

            Assert.That(() =>
            {
                model.MarkDirty(T1);
                model.TryClean(T1);
                model.Clear();
                model.RestoreDirtyTables(new[] { T2 });
            }, Throws.Nothing);
        }

        [Test]
        public void MarkDirty_PublishesTableDirtiedThenCleanlinessChanged()
        {
            var model = WithBus(out var rec);
            model.SetTotalTables(4);
            rec.Changed.Clear(); // drop the SetTotalTables notification

            model.MarkDirty(T1);

            Assert.That(rec.Dirtied.Count, Is.EqualTo(1));
            Assert.That(rec.Dirtied[0].TableId, Is.EqualTo(T1));
            Assert.That(rec.Changed.Count, Is.EqualTo(1));
            Assert.That(rec.Changed[0].Cleanliness01, Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(rec.Changed[0].DirtyTableCount, Is.EqualTo(1));
            Assert.That(rec.Changed[0].TotalTables, Is.EqualTo(4));
        }

        [Test]
        public void TryClean_PublishesTableCleanedAndCleanlinessChanged()
        {
            var model = WithBus(out var rec);
            model.SetTotalTables(4);
            model.MarkDirty(T1);
            rec.Changed.Clear();

            model.TryClean(T1);

            Assert.That(rec.Cleaned.Count, Is.EqualTo(1));
            Assert.That(rec.Cleaned[0].TableId, Is.EqualTo(T1));
            Assert.That(rec.Changed.Count, Is.EqualTo(1));
            Assert.That(rec.Changed[0].Cleanliness01, Is.EqualTo(1f));
        }

        [Test]
        public void NoOpMarkDirtyAndTryClean_PublishNothing()
        {
            var model = WithBus(out var rec);
            model.SetTotalTables(4);
            model.MarkDirty(T1);
            rec.Dirtied.Clear();
            rec.Cleaned.Clear();
            rec.Changed.Clear();

            model.MarkDirty(T1);   // already dirty
            model.TryClean(T2);    // already clean

            Assert.That(rec.Dirtied, Is.Empty);
            Assert.That(rec.Cleaned, Is.Empty);
            Assert.That(rec.Changed, Is.Empty);
        }

        [Test]
        public void SetTotalTables_SameValue_PublishesNothing()
        {
            var model = WithBus(out var rec);
            model.SetTotalTables(4);
            rec.Changed.Clear();

            model.SetTotalTables(4);

            Assert.That(rec.Changed, Is.Empty);
        }

        [Test]
        public void Clear_PublishesOneCleanlinessChanged_AndNoPerTableEvents()
        {
            var model = WithBus(out var rec);
            model.SetTotalTables(4);
            model.MarkDirty(T1);
            model.MarkDirty(T2);
            rec.Cleaned.Clear();
            rec.Changed.Clear();

            model.Clear();

            Assert.That(rec.Changed.Count, Is.EqualTo(1));
            Assert.That(rec.Changed[0].Cleanliness01, Is.EqualTo(1f));
            Assert.That(rec.Cleaned, Is.Empty, "Clear is a bulk reset, not two individual wipes");
        }

        [Test]
        public void Clear_WhenAlreadySpotless_PublishesNothing()
        {
            var model = WithBus(out var rec);
            model.SetTotalTables(4);
            rec.Changed.Clear();

            model.Clear();

            Assert.That(rec.Changed, Is.Empty);
        }

        [Test]
        public void RestoreDirtyTables_PublishesOneCleanlinessChanged_AndNoTableDirtied()
        {
            // "A customer just left a mess" is false during a load, so the
            // per-table events must stay silent -- only the meter resyncs.
            var model = WithBus(out var rec);
            model.SetTotalTables(4);
            rec.Changed.Clear();

            model.RestoreDirtyTables(new[] { T1, T2 });

            Assert.That(rec.Dirtied, Is.Empty);
            Assert.That(rec.Changed.Count, Is.EqualTo(1));
            Assert.That(rec.Changed[0].Cleanliness01, Is.EqualTo(0.5f).Within(1e-5f));
        }
    }
}
