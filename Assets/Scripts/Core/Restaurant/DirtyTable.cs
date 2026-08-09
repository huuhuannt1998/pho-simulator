using Pho.Core.Interaction;
using UnityEngine;

namespace Pho.Core.Restaurant
{
    /// <summary>
    /// The physical table the player walks up to and wipes -- GDD step 12
    /// ("player cleans"), reduced by architecture.md section 12 to the single
    /// dirty-tables system. A thin <see cref="IInteractable"/> wrapper around
    /// <see cref="CleanlinessService"/>; all rules live in the pure
    /// <c>CleanlinessModel</c> behind it.
    ///
    /// Modelled on <c>RestaurantSign</c>, the closest existing example.
    /// Like it, this does NOT take an explicit <c>Bind(...)</c> from a
    /// scene-building pass, because its dependency doesn't exist at
    /// edit-time -- <c>GameBootstrap.Awake()</c> runs the installer that
    /// creates CleanlinessService at runtime, after the scene is already
    /// built. It resolves lazily on first use via the one deliberate
    /// <c>GameBootstrap.Current</c> singleton exception (see GameBootstrap's
    /// own class doc comment for the caveats that seam carries: it is the
    /// single well-known access point for the ONE root composition object,
    /// not a general service locator, and nothing here adds static state).
    ///
    /// <b>INERT BUT NON-THROWING</b> until it resolves: before the bootstrap
    /// has run, in a scene with no GameBootstrap at all, or if
    /// CleanlinessService somehow isn't registered, every member returns the
    /// "nothing to do here" answer (empty prompt, CanInteract false,
    /// Interact no-op) rather than null-referencing at the player.
    ///
    /// <b>SELF-REGISTRATION:</b> this component is also how the service
    /// learns the dining room's table count (the denominator of the
    /// cleanliness fraction) -- see CleanlinessService's SCENE TABLE COUNT
    /// note. Registration is attempted in <c>Start</c> (guaranteed after
    /// every <c>Awake</c>, so after GameBootstrap has built the context) and
    /// retried on every later resolve, which covers a table instantiated
    /// mid-game as well as the undefined Awake/OnEnable ordering between
    /// this component and GameBootstrap.
    /// </summary>
    public sealed class DirtyTable : MonoBehaviour, IInteractable
    {
        [Header("Identity")]
        [Tooltip("Table identifier, e.g. \"table.3\". Must match the tableId on this table's TableRegistry seat slots, so a customer leaving that seat dirties this table. Purely a scene label -- not a Domain-typed ID (same convention as TableRegistry.SeatSlot.tableId).")]
        [SerializeField] string tableId = "table.unassigned";

        CleanlinessService _service;
        bool _registered;

        /// <summary>The composition root <see cref="_service"/> came from -- see the STALE-CONTEXT GUARD in TryResolveService.</summary>
        GameContext _ctx;

        /// <summary>The scene label this table is known by. Read-only at runtime.</summary>
        public string TableId => tableId;

        /// <summary>True once the service exists AND this table is marked dirty in it.</summary>
        public bool IsDirty => TryResolveService() && _service.IsDirty(tableId);

        void Start() => TryResolveService();

        void OnEnable() => TryResolveService();

        void OnDisable()
        {
            if (!_registered || _service == null) return;

            _service.UnregisterTable(tableId);
            _registered = false;
        }

        /// <summary>
        /// Resolves the service (lazily, once) and ensures this table is
        /// registered with it. Returns false while either step is still
        /// impossible -- callers must treat that as "inert", never as an
        /// error. Safe to call every frame: both halves are cheap and
        /// idempotent once satisfied.
        /// </summary>
        bool TryResolveService()
        {
            if (string.IsNullOrWhiteSpace(tableId)) return false;

            var ctx = GameBootstrap.Current;
            if (ctx == null) return false;

            // STALE-CONTEXT GUARD -- do not remove.
            //
            // GameBootstrap.Current is static and is NOT cleared when a scene
            // unloads, so during a scene RELOAD it still points at the
            // outgoing scene's context until the new GameBootstrap.Awake
            // replaces it. Unity gives no ordering guarantee between this
            // component's OnEnable and that Awake, so OnEnable can easily
            // resolve and register against the DEAD service, cache it, and
            // set _registered -- after which Start() short-circuits and the
            // table is never registered with the LIVE service. The table then
            // silently vanishes from the cleanliness denominator and can
            // never be dirtied or cleaned, with no error anywhere.
            //
            // Caught by the golden-path test failing only when it ran after
            // another test had already loaded the scene once; it passed in
            // isolation, which is exactly the shape this bug takes. It would
            // equally affect loading a save or starting a new day in the
            // shipped game.
            if (!ReferenceEquals(ctx, _ctx))
            {
                _ctx = ctx;
                _service = null;
                _registered = false;
            }

            if (_service == null && !ctx.TryGet(out _service)) return false;

            if (!_registered)
            {
                _service.RegisterTable(tableId);
                _registered = true;
            }

            return true;
        }

        /// <summary>
        /// Hook for whoever decides a table has become messy -- a customer
        /// finishing their meal (Pho.Customers already references Pho.Core).
        /// Callers that hold this component don't need to know the id string.
        /// No-op while inert. Returns true only if the table actually changed
        /// from clean to dirty.
        /// </summary>
        public bool MarkDirty() => TryResolveService() && _service.MarkDirty(tableId);

        public string GetInteractionText(in InteractionContext ctx)
        {
            if (!TryResolveService()) return string.Empty;

            // Nothing to say about a table that's already clean -- the
            // player shouldn't get a prompt for a no-op.
            return _service.IsDirty(tableId) ? "Press E to clean the table" : string.Empty;
        }

        public bool CanInteract(in InteractionContext ctx) =>
            TryResolveService() && _service.IsDirty(tableId);

        public void Interact(in InteractionContext ctx)
        {
            if (!TryResolveService()) return;

            // TryClean returning false (someone else wiped it between the
            // prompt and the keypress) is an ordinary outcome, not an error.
            _service.TryClean(tableId);
        }
    }
}
