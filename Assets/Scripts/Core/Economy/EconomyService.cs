using System;
using System.Collections.Generic;
using Pho.Core;
using Pho.Domain.Contracts;
using Pho.Domain.Events;
using Pho.Save.Dto;
using Pho.Save.Participation;

namespace Pho.Core.Economy
{
    /// <summary>
    /// One row of the running transaction log -- a much simpler cousin of
    /// the (not-yet-built) architecture.md Ledger, sized for this pass:
    /// enough to answer "what happened to the cash" without a query API.
    /// </summary>
    public readonly struct LedgerEntry
    {
        public readonly decimal Delta;
        public readonly LedgerCategory Category;
        public readonly decimal BalanceAfter;

        public LedgerEntry(decimal delta, LedgerCategory category, decimal balanceAfter)
        {
            Delta = delta;
            Category = category;
            BalanceAfter = balanceAfter;
        }
    }

    /// <summary>
    /// Owns the restaurant's running cash balance + a simple transaction
    /// log. Registered into GameContext as a concrete EconomyService --
    /// no interface, mirroring InventoryService's judgment call: nothing
    /// frozen calls for one.
    ///
    /// DEBIT-GOING-NEGATIVE DECISION (judgment call the brief explicitly
    /// asks to be explicit about): <see cref="Debit"/> NEVER rejects for
    /// insufficient funds -- Cash is allowed to go negative. Reasoning:
    /// <list type="bullet">
    /// <item>A struggling restaurant going into debt (rent due, a bad week,
    /// restocking before the till has recovered) is a legitimate scenario
    /// for a game about running a small business, not an error state.</item>
    /// <item>A later wave's rent auto-debit (M11, every 7 days) and upgrade
    /// purchases (M13) need Debit to always apply so the books stay
    /// consistent -- a Debit that silently no-ops or clamps at 0 on
    /// insufficient funds would break the ledger's core arithmetic
    /// invariant (sum of all deltas == current Cash) and would need its own
    /// separate "did it actually happen" return-value contract that nothing
    /// downstream currently expects.</item>
    /// <item>A future bankruptcy / game-over check, if one is ever added,
    /// should read <see cref="IsInsolvent"/> (or <c>Cash &lt; 0</c> directly)
    /// rather than Debit refusing to go there -- that keeps "can this
    /// transaction happen" (always yes) and "is the restaurant in trouble"
    /// (a separate, higher-level judgment) as two different questions.</item>
    /// </list>
    /// The only thing <see cref="Credit"/>/<see cref="Debit"/> reject is a
    /// non-positive <c>amount</c> argument -- mirrors
    /// <c>InventoryModel.Add</c> treating a non-positive amount as a
    /// programmer error (call the other method, or don't call at all, for a
    /// zero-amount transaction), never a legitimate runtime occurrence.
    /// </summary>
    [AutoInstall]
    public sealed class EconomyService : IInstaller, ISaveParticipant
    {
        /// <summary>GDD's "$1,500 equivalent" starting cash example.</summary>
        public const decimal StartingCash = 1500m;

        readonly List<LedgerEntry> _log = new List<LedgerEntry>();
        IEventBus _events;
        bool _bound;

        public decimal Cash { get; private set; }

        /// <summary>Convenience read for a future bankruptcy/game-over check -- see the DEBIT-GOING-NEGATIVE decision above. Not consumed by anything yet.</summary>
        public bool IsInsolvent => Cash < 0m;

        public IReadOnlyList<LedgerEntry> TransactionLog => _log;

        public int Order => InstallOrder.Economy;

        public void Install(GameContext ctx)
        {
            Bind(ctx.Events, StartingCash);
            ctx.Register<EconomyService>(this);

            if (ctx.TryGet<SaveParticipantRegistry>(out var saveRegistry))
            {
                saveRegistry.Register(this);
            }
        }

        /// <summary>ISaveParticipant. RestoreOrder mirrors InstallOrder -- no cross-participant ordering dependency exists today, but reusing the same constant avoids inventing a second, parallel ordering policy.</summary>
        public int RestoreOrder => InstallOrder.Economy;

        public void Capture(SaveFile save) => save.economy.cash = Cash;

        /// <summary>
        /// Routed through Apply (not a bare setter) so a restore still logs
        /// a LedgerEntry and publishes CashChanged like any other cash
        /// change -- UI (CashPresenter) picks it up the same way it would a
        /// normal transaction, no special-casing needed downstream.
        /// </summary>
        public void Restore(SaveFile save, IGameDatabase db) => Apply(save.economy.cash - Cash, LedgerCategory.Other);

        /// <summary>
        /// Injection seam separated from Install so this can be constructed
        /// and bound without a full GameContext (mirrors the Kitchen
        /// station Bind(...) pattern) -- e.g. for a future standalone test.
        /// </summary>
        public void Bind(IEventBus events, decimal startingCash)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
            Cash = startingCash;
            _bound = true;
        }

        /// <summary>Adds money (a sale, etc.). Publishes CashChanged. Throws if amount is not positive.</summary>
        public void Credit(decimal amount, LedgerCategory category)
        {
            if (amount <= 0m) throw new ArgumentException("Credit amount must be positive.", nameof(amount));
            Apply(amount, category);
        }

        /// <summary>
        /// Removes money (rent, ingredient cost, ...). Publishes
        /// CashChanged. Always applies -- see the class doc comment's
        /// DEBIT-GOING-NEGATIVE decision; Cash may go negative. Throws if
        /// amount is not positive.
        /// </summary>
        public void Debit(decimal amount, LedgerCategory category)
        {
            if (amount <= 0m) throw new ArgumentException("Debit amount must be positive.", nameof(amount));
            Apply(-amount, category);
        }

        void Apply(decimal delta, LedgerCategory category)
        {
            Cash += delta;
            _log.Add(new LedgerEntry(delta, category, Cash));

            if (_bound)
            {
                _events.Publish(new CashChanged(Cash, delta, category));
            }
        }
    }
}
