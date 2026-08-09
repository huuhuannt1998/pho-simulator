using NUnit.Framework;
using Pho.Domain.Multiplayer;

namespace Pho.Domain.Tests
{
    /// <summary>
    /// Slot assignment, identity derivation and spawn placement for the
    /// four-player session. If these pass, NetworkPlayer above is only
    /// plumbing: it hands the allocator a client id and applies whatever
    /// comes back.
    /// </summary>
    [TestFixture]
    public class NetPlayerTests
    {
        const ulong Alice = 1;
        const ulong Bob = 2;
        const ulong Carol = 3;
        const ulong Dave = 4;
        const ulong Eve = 5;

        [Test]
        public void FirstFourPlayers_GetDistinctSlotsZeroToThree()
        {
            var alloc = new PlayerSlotAllocator();

            Assert.That(alloc.TryAssign(Alice, out var a), Is.True);
            Assert.That(alloc.TryAssign(Bob, out var b), Is.True);
            Assert.That(alloc.TryAssign(Carol, out var c), Is.True);
            Assert.That(alloc.TryAssign(Dave, out var d), Is.True);

            Assert.That(new[] { a, b, c, d }, Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
            Assert.That(alloc.AssignedCount, Is.EqualTo(4));
        }

        [Test]
        public void FifthPlayer_IsRefused_WithoutDisturbingTheOthers()
        {
            var alloc = new PlayerSlotAllocator();
            alloc.TryAssign(Alice, out _);
            alloc.TryAssign(Bob, out _);
            alloc.TryAssign(Carol, out _);
            alloc.TryAssign(Dave, out var daveSlot);

            Assert.That(alloc.TryAssign(Eve, out var eveSlot), Is.False);
            Assert.That(eveSlot, Is.EqualTo(-1));
            Assert.That(alloc.AssignedCount, Is.EqualTo(4));
            Assert.That(alloc.TryGetSlot(Dave, out var daveAgain), Is.True);
            Assert.That(daveAgain, Is.EqualTo(daveSlot));
        }

        [Test]
        public void ReAssigningTheSameActor_IsIdempotent_AndBurnsNoSeat()
        {
            var alloc = new PlayerSlotAllocator();
            alloc.TryAssign(Alice, out var first);

            Assert.That(alloc.TryAssign(Alice, out var second), Is.True);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(alloc.AssignedCount, Is.EqualTo(1));
        }

        [Test]
        public void ReleasedSlot_IsReusedByTheNextJoiner()
        {
            var alloc = new PlayerSlotAllocator();
            alloc.TryAssign(Alice, out _);
            alloc.TryAssign(Bob, out var bobSlot);
            alloc.TryAssign(Carol, out _);

            Assert.That(alloc.Release(Bob), Is.True);
            Assert.That(alloc.TryAssign(Dave, out var daveSlot), Is.True);

            // Lowest free slot -- the whole point, so a reconnect-heavy
            // session never runs off the end of the colour palette.
            Assert.That(daveSlot, Is.EqualTo(bobSlot));
        }

        [Test]
        public void DuplicateRelease_DoesNotFreeSomebodyElsesSlot()
        {
            var alloc = new PlayerSlotAllocator();
            alloc.TryAssign(Alice, out _);
            alloc.TryAssign(Bob, out var bobSlot);
            alloc.Release(Bob);
            alloc.TryAssign(Carol, out var carolSlot);

            Assert.That(carolSlot, Is.EqualTo(bobSlot));
            Assert.That(alloc.Release(Bob), Is.False);
            Assert.That(alloc.TryGetSlot(Carol, out _), Is.True);
            Assert.That(alloc.AssignedCount, Is.EqualTo(2));
        }

        [Test]
        public void CapacityIsRespected_WhenOverridden()
        {
            var alloc = new PlayerSlotAllocator(2);

            Assert.That(alloc.Capacity, Is.EqualTo(2));
            Assert.That(alloc.TryAssign(Alice, out _), Is.True);
            Assert.That(alloc.TryAssign(Bob, out _), Is.True);
            Assert.That(alloc.TryAssign(Carol, out _), Is.False);
        }

        [Test]
        public void SpawnOffsets_AreDistinctForEverySlot()
        {
            var offsets = new System.Collections.Generic.HashSet<Pho.Domain.Infra.Vec3>();

            for (int slot = 0; slot < PlayerSlotAllocator.MaxPlayers; slot++)
            {
                Assert.That(offsets.Add(PlayerSlotAllocator.SpawnOffset(slot)), Is.True,
                    $"slot {slot} produced a duplicate spawn offset -- players would interpenetrate");
            }
        }

        [Test]
        public void SpawnOffsets_StayFlat_AndScaleWithSpacing()
        {
            var wide = PlayerSlotAllocator.SpawnOffset(0, 4f);
            var narrow = PlayerSlotAllocator.SpawnOffset(0, 2f);

            Assert.That(wide.Y, Is.EqualTo(0f), "offsets must not lift players off the anchor's floor height");
            Assert.That(wide.X, Is.EqualTo(narrow.X * 2f).Within(1e-4f));
            Assert.That(wide.Z, Is.EqualTo(narrow.Z * 2f).Within(1e-4f));
        }

        [Test]
        public void SpawnOffset_ForAnOutOfRangeSlot_FallsBackToZeroRatherThanThrowing()
        {
            Assert.That(PlayerSlotAllocator.SpawnOffset(-1), Is.EqualTo(Pho.Domain.Infra.Vec3.Zero));
            Assert.That(PlayerSlotAllocator.SpawnOffset(PlayerSlotAllocator.MaxPlayers), Is.EqualTo(Pho.Domain.Infra.Vec3.Zero));
        }

        [Test]
        public void DefaultDisplayNames_AreOneBasedAndDistinct()
        {
            Assert.That(PlayerSlotAllocator.DefaultDisplayName(0), Is.EqualTo("Cook 1"));
            Assert.That(PlayerSlotAllocator.DefaultDisplayName(3), Is.EqualTo("Cook 4"));
            Assert.That(PlayerSlotAllocator.DefaultDisplayName(0),
                Is.Not.EqualTo(PlayerSlotAllocator.DefaultDisplayName(1)));
        }

        [Test]
        public void SanitizeDisplayName_FallsBackForEmptyInput()
        {
            Assert.That(PlayerSlotAllocator.SanitizeDisplayName(null, 0), Is.EqualTo("Cook 1"));
            Assert.That(PlayerSlotAllocator.SanitizeDisplayName("   ", 1), Is.EqualTo("Cook 2"));
        }

        [Test]
        public void SanitizeDisplayName_TrimsAndClamps()
        {
            Assert.That(PlayerSlotAllocator.SanitizeDisplayName("  Huan  ", 0), Is.EqualTo("Huan"));
            Assert.That(PlayerSlotAllocator.SanitizeDisplayName("ABCDEFGHIJKLMNOPQRSTUVWXYZ", 0, 8),
                Is.EqualTo("ABCDEFGH"));
        }

        [TestCase("")]
        [TestCase("A")]
        [TestCase("Cook 1")]
        [TestCase("abcdefgh")]          // exactly one 8-byte block
        [TestCase("abcdefghi")]         // spills into the second block
        [TestCase("12345678901234567890123456789012")] // exactly full
        public void PackedName_RoundTrips(string original)
        {
            AsciiNamePacker.Pack(original, out var a, out var b, out var c, out var d, out var length);

            Assert.That(AsciiNamePacker.Unpack(a, b, c, d, length), Is.EqualTo(original));
        }

        [Test]
        public void PackedName_TruncatesRatherThanOverflowing()
        {
            var tooLong = new string('x', AsciiNamePacker.MaxLength + 10);
            AsciiNamePacker.Pack(tooLong, out var a, out var b, out var c, out var d, out var length);

            Assert.That(length, Is.EqualTo(AsciiNamePacker.MaxLength));
            Assert.That(AsciiNamePacker.Unpack(a, b, c, d, length).Length, Is.EqualTo(AsciiNamePacker.MaxLength));
        }

        [Test]
        public void PackedName_SubstitutesNonAscii_RatherThanCorrupting()
        {
            AsciiNamePacker.Pack("Phở", out var a, out var b, out var c, out var d, out var length);

            // Visibly wrong beats subtly corrupt: a mid-codepoint truncation
            // would arrive as garbage on the other three machines.
            Assert.That(AsciiNamePacker.Unpack(a, b, c, d, length), Is.EqualTo("Ph?"));
        }

        [Test]
        public void PackedName_NullIsEmpty()
        {
            AsciiNamePacker.Pack(null, out var a, out var b, out var c, out var d, out var length);

            Assert.That(length, Is.EqualTo(0));
            Assert.That(AsciiNamePacker.Unpack(a, b, c, d, length), Is.EqualTo(string.Empty));
        }
    }
}
