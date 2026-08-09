using System;
using System.IO;
using Pho.Domain.Contracts;
using Pho.Domain.Events;
using Pho.Save;
using Pho.Save.Dto;
using Pho.Save.Io;
using Pho.Save.Migration;
using Pho.Save.Participation;
using UnityEngine;

namespace Pho.Core.Save
{
    /// <summary>
    /// Runtime facade over SaveCoordinator + FileSaveStore --
    /// Pho.Core-side, since Pho.Save cannot reference UnityEngine (see
    /// FileSaveStore's own doc comment: "Pho.Core is responsible for
    /// constructing this with Application.persistentDataPath").
    ///
    /// A fresh SaveCoordinator is built on every Save/TryLoad call rather
    /// than once at Install time -- SaveCoordinator's constructor eagerly
    /// snapshots its participant list, but participants self-register into
    /// SaveParticipantRegistry from THEIR OWN installers, most of which run
    /// after GameSaveInstaller (InstallOrder.Save is deliberately the
    /// lowest slot). Building the coordinator lazily, from whatever the
    /// registry currently holds, sidesteps that ordering dependency
    /// entirely instead of requiring this to be the last installer to run.
    /// </summary>
    public sealed class GameSaveService
    {
        public const string DefaultSlot = "default";

        readonly ISaveStore _store;
        readonly SaveMigrator _migrator;
        readonly SaveParticipantRegistry _registry;
        readonly IEventBus _events;

        public GameSaveService(ISaveStore store, SaveMigrator migrator, SaveParticipantRegistry registry, IEventBus events)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _migrator = migrator ?? throw new ArgumentNullException(nameof(migrator));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _events = events;
        }

        public SaveFile Save(string slot = DefaultSlot)
        {
            var coordinator = BuildCoordinator();
            return coordinator.Save(slot, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }

        public bool TryLoad(IGameDatabase db, out SaveFile save, string slot = DefaultSlot)
        {
            var coordinator = BuildCoordinator();
            return coordinator.TryLoad(slot, db, out save);
        }

        SaveCoordinator BuildCoordinator() =>
            new SaveCoordinator(_store, _migrator, _registry.Participants, _events, Application.version);
    }

    /// <summary>
    /// [AutoInstall] at InstallOrder.Save (the lowest slot -- see
    /// InstallOrder.cs) so SaveParticipantRegistry exists in GameContext
    /// before every later installer that wants to self-register into it
    /// (Economy=300, Inventory=400, DayCycle=450).
    /// </summary>
    [AutoInstall]
    public sealed class GameSaveInstaller : IInstaller
    {
        public int Order => InstallOrder.Save;

        public void Install(GameContext ctx)
        {
            var registry = new SaveParticipantRegistry();
            ctx.Register<SaveParticipantRegistry>(registry);

            var rootPath = Path.Combine(Application.persistentDataPath, "Saves");
            var store = new FileSaveStore(rootPath);
            var migrator = new SaveMigrator(Array.Empty<ISaveMigration>());

            var service = new GameSaveService(store, migrator, registry, ctx.Events);
            ctx.Register<GameSaveService>(service);
        }
    }
}
