using System;
using NUnit.Framework;
using Pho.Save.Participation;
using Pho.Save.Tests.Fakes;

namespace Pho.Save.Tests
{
    [TestFixture]
    public class SaveParticipantRegistryTests
    {
        [Test]
        public void Register_ThenParticipants_ReturnsRegisteredInstancesInOrderAdded()
        {
            var registry = new SaveParticipantRegistry();
            var a = new FakeSaveParticipant("a", restoreOrder: 0, callLog: null, onCapture: null, onRestore: null);
            var b = new FakeSaveParticipant("b", restoreOrder: 1, callLog: null, onCapture: null, onRestore: null);

            registry.Register(a);
            registry.Register(b);

            Assert.That(registry.Participants, Is.EqualTo(new ISaveParticipant[] { a, b }));
        }

        [Test]
        public void Register_Null_Throws()
        {
            var registry = new SaveParticipantRegistry();

            Assert.That(() => registry.Register(null), Throws.ArgumentNullException);
        }

        [Test]
        public void NewRegistry_HasNoParticipants()
        {
            var registry = new SaveParticipantRegistry();

            Assert.That(registry.Participants, Is.Empty);
        }
    }
}
