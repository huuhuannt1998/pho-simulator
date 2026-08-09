using NUnit.Framework;
using Pho.Domain.Multiplayer;

namespace Pho.Domain.Tests
{
    /// <summary>
    /// The contention rules for shared carryables in co-op. These are the
    /// tests that let the netcode layer stay a thin adapter -- if these
    /// pass, "two players grabbed the same bowl" is already solved and the
    /// RPC above is just plumbing.
    /// </summary>
    [TestFixture]
    public class CarryRegistryTests
    {
        const ulong Bowl = 101;
        const ulong OtherBowl = 102;
        const ulong Alice = 1;
        const ulong Bob = 2;

        [Test]
        public void Claim_OnFreeItem_Succeeds()
        {
            var reg = new CarryRegistry();

            Assert.That(reg.TryClaim(Bowl, Alice), Is.True);
            Assert.That(reg.IsHeldBy(Bowl, Alice), Is.True);
            Assert.That(reg.HeldCount, Is.EqualTo(1));
        }

        [Test]
        public void TwoActorsClaimSameItem_ExactlyOneWins()
        {
            var reg = new CarryRegistry();

            // The whole point of the class: same item, back-to-back, as the
            // host would process two requests arriving in one frame.
            var aliceGot = reg.TryClaim(Bowl, Alice);
            var bobGot = reg.TryClaim(Bowl, Bob);

            Assert.That(aliceGot, Is.True);
            Assert.That(bobGot, Is.False, "the second claimant must be refused -- both succeeding would duplicate the bowl");
            Assert.That(reg.IsHeldBy(Bowl, Alice), Is.True);
            Assert.That(reg.IsHeldBy(Bowl, Bob), Is.False);
            Assert.That(reg.HeldCount, Is.EqualTo(1));
        }

        [Test]
        public void ReClaim_BySameHolder_IsIdempotent()
        {
            var reg = new CarryRegistry();
            reg.TryClaim(Bowl, Alice);

            // A duplicated/retried request over an unreliable channel must
            // not fail just because it arrived twice.
            Assert.That(reg.TryClaim(Bowl, Alice), Is.True);
            Assert.That(reg.HeldCount, Is.EqualTo(1));
        }

        [Test]
        public void Release_ThenAnotherActorCanClaim()
        {
            var reg = new CarryRegistry();
            reg.TryClaim(Bowl, Alice);

            Assert.That(reg.TryRelease(Bowl, Alice), Is.True);
            Assert.That(reg.IsHeld(Bowl), Is.False);
            Assert.That(reg.TryClaim(Bowl, Bob), Is.True);
        }

        [Test]
        public void Release_ByNonHolder_IsRefused_AndDoesNotDisturbTheRealHolder()
        {
            var reg = new CarryRegistry();
            reg.TryClaim(Bowl, Alice);

            // A late "I dropped it" from Bob must never knock the bowl out
            // of Alice's hands.
            Assert.That(reg.TryRelease(Bowl, Bob), Is.False);
            Assert.That(reg.IsHeldBy(Bowl, Alice), Is.True);
        }

        [Test]
        public void Release_OfUnheldItem_IsRefused_NotAnError()
        {
            var reg = new CarryRegistry();

            Assert.That(() => reg.TryRelease(Bowl, Alice), Throws.Nothing);
            Assert.That(reg.TryRelease(Bowl, Alice), Is.False);
        }

        [Test]
        public void ReleaseAll_DropsEverythingThatActorHeld_AndReportsIt()
        {
            var reg = new CarryRegistry();
            reg.TryClaim(Bowl, Alice);
            reg.TryClaim(OtherBowl, Alice);

            var released = reg.ReleaseAll(Alice);

            Assert.That(released, Is.EquivalentTo(new[] { Bowl, OtherBowl }));
            Assert.That(reg.HeldCount, Is.EqualTo(0));
        }

        [Test]
        public void ReleaseAll_OnDisconnect_LeavesOtherPlayersHoldingsAlone()
        {
            var reg = new CarryRegistry();
            reg.TryClaim(Bowl, Alice);
            reg.TryClaim(OtherBowl, Bob);

            reg.ReleaseAll(Alice);

            Assert.That(reg.IsHeldBy(OtherBowl, Bob), Is.True, "one player quitting must not drop everyone else's items");
            Assert.That(reg.IsHeld(Bowl), Is.False);
        }

        [Test]
        public void ReleaseAll_ThenTheItemIsClaimableAgain()
        {
            var reg = new CarryRegistry();
            reg.TryClaim(Bowl, Alice);

            // The disconnect path that matters: a player who rage-quits
            // mid-rush must not permanently lock the bowl they were holding.
            reg.ReleaseAll(Alice);

            Assert.That(reg.TryClaim(Bowl, Bob), Is.True);
        }

        [Test]
        public void Forget_RemovesAConsumedItemEntirely()
        {
            var reg = new CarryRegistry();
            reg.TryClaim(Bowl, Alice);

            reg.Forget(Bowl);

            Assert.That(reg.IsHeld(Bowl), Is.False);
            Assert.That(reg.HeldCount, Is.EqualTo(0));
        }

        [Test]
        public void DifferentItems_DoNotContendWithEachOther()
        {
            var reg = new CarryRegistry();

            Assert.That(reg.TryClaim(Bowl, Alice), Is.True);
            Assert.That(reg.TryClaim(OtherBowl, Bob), Is.True);
            Assert.That(reg.HeldCount, Is.EqualTo(2));
        }

        [Test]
        public void TryGetHolder_ReportsTheCurrentHolder()
        {
            var reg = new CarryRegistry();
            reg.TryClaim(Bowl, Alice);

            Assert.That(reg.TryGetHolder(Bowl, out var holder), Is.True);
            Assert.That(holder, Is.EqualTo(Alice));
            Assert.That(reg.TryGetHolder(OtherBowl, out _), Is.False);
        }
    }
}
