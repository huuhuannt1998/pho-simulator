using Pho.Domain.Events;

namespace Pho.Domain.Economy
{
    /// <summary>
    /// Immutable snapshot of one day's ledger activity, reduced to exactly
    /// the two flow figures <see cref="DailyReport"/> has fields for.
    /// This is the input boundary of <see cref="DailyReportBuilder"/>: the
    /// builder never sees individual transactions, only these totals, which
    /// is what keeps it a pure function of trivially-constructible inputs.
    /// </summary>
    public readonly struct DayLedgerTotals
    {
        /// <summary>Net cash in from sales/tips for the day (refunds net it back down).</summary>
        public readonly decimal Revenue;

        /// <summary>Net cash out for ingredients for the day, expressed as a POSITIVE cost.</summary>
        public readonly decimal IngredientCost;

        public DayLedgerTotals(decimal revenue, decimal ingredientCost)
        {
            Revenue = revenue;
            IngredientCost = ingredientCost;
        }

        public static DayLedgerTotals Empty => new DayLedgerTotals(0m, 0m);
    }

    /// <summary>
    /// Pure per-day accumulator over the same signed <c>(delta, category)</c>
    /// pairs <c>Pho.Core.Economy.EconomyService</c> writes into its
    /// <c>TransactionLog</c> and broadcasts as <see cref="CashChanged"/>.
    ///
    /// WHY THIS TYPE EXISTS AT ALL: the report's revenue and ingredient-cost
    /// numbers have to come from real transactions, but <c>LedgerEntry</c>
    /// lives in <c>Pho.Core</c> and Domain cannot reference Core (the
    /// dependency graph is strictly downward -- architecture.md §1). So the
    /// Core-side host feeds this accumulator the <see cref="CashChanged"/>
    /// deltas it already receives, and the arithmetic stays here, in the
    /// assembly that <c>dotnet test</c> can exercise in ~1s.
    ///
    /// Money is <c>decimal</c> throughout, never float/double -- the
    /// project-wide convention (architecture.md §4 decision 3: "Float money
    /// produces $9.499999 in the daily report"), matching
    /// <c>EconomyService.Cash</c>, <c>EconomyDto.cash</c> and
    /// <c>DailyReportDto</c>.
    ///
    /// CATEGORY MAPPING -- deliberate, and the exclusions matter as much as
    /// the inclusions:
    /// <list type="bullet">
    /// <item><see cref="LedgerCategory.Sale"/> and
    /// <see cref="LedgerCategory.Tip"/> both add to <see cref="Revenue"/>.
    /// Tips are cash that genuinely entered the till; leaving them out would
    /// make them vanish from the books and understate profit. (Today
    /// <c>CustomerAgent.Pay</c> already credits price+tip as a single
    /// <c>Sale</c>; <c>Tip</c> is handled here so a future split-out
    /// categorisation doesn't silently start losing money.)</item>
    /// <item><see cref="LedgerCategory.IngredientPurchase"/> adds to
    /// <see cref="IngredientCost"/>, sign-flipped so a debit (negative delta)
    /// becomes a positive cost.</item>
    /// <item><see cref="LedgerCategory.Rent"/> is IGNORED here. Rent on the
    /// report is derived from the config's schedule, not from the ledger --
    /// see <see cref="DailyReportBuilder"/>'s RENT SOURCING note, which also
    /// explains the double-count hazard to watch for when a rent auto-debit
    /// eventually lands.</item>
    /// <item><see cref="LedgerCategory.Equipment"/> is IGNORED: an upgrade
    /// purchase is capital expenditure, and <see cref="DailyReport"/>'s
    /// frozen shape has no field for it. Folding it into ingredient cost
    /// would misreport a one-off $400 burner as a day's food spend.</item>
    /// <item><see cref="LedgerCategory.Other"/> is IGNORED, and this one is
    /// load-bearing rather than merely tidy: <c>EconomyService.Restore</c>
    /// reconciles a loaded save by applying
    /// <c>(save.economy.cash - Cash)</c> as an <c>Other</c> transaction.
    /// Counting that would book an entire save file's cash as one day's
    /// revenue.</item>
    /// </list>
    /// </summary>
    public sealed class DayLedgerAccumulator
    {
        /// <summary>Net revenue recorded so far this day. Signed deltas are summed, so a refund booked against <see cref="LedgerCategory.Sale"/> correctly reduces it.</summary>
        public decimal Revenue { get; private set; }

        /// <summary>Net ingredient spend so far this day, as a positive cost. A supplier refund (positive delta) correctly reduces it.</summary>
        public decimal IngredientCost { get; private set; }

        /// <summary>
        /// Folds one cash movement into the day's running totals.
        /// <paramref name="delta"/> uses EconomyService's sign convention:
        /// positive is money in, negative is money out.
        /// Unrecognised/excluded categories are silently ignored by design --
        /// see the class doc comment's CATEGORY MAPPING.
        /// </summary>
        public void Record(decimal delta, LedgerCategory category)
        {
            switch (category)
            {
                case LedgerCategory.Sale:
                case LedgerCategory.Tip:
                    Revenue += delta;
                    break;

                case LedgerCategory.IngredientPurchase:
                    // Cost is an outflow: a -12.00 debit is a +12.00 cost.
                    IngredientCost -= delta;
                    break;
            }
        }

        /// <summary>Captures the current totals without mutating anything. Call before <see cref="Reset"/>.</summary>
        public DayLedgerTotals Snapshot() => new DayLedgerTotals(Revenue, IngredientCost);

        /// <summary>Clears the day's totals, ready for the next day. Called at a day boundary, after the ended day's snapshot has been taken.</summary>
        public void Reset()
        {
            Revenue = 0m;
            IngredientCost = 0m;
        }
    }
}
