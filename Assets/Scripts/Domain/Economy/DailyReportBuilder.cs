using Pho.Domain.Contracts;
using Pho.Domain.Events;

namespace Pho.Domain.Economy
{
    /// <summary>
    /// Builds a real <see cref="DailyReport"/> from explicit inputs --
    /// architecture.md's M11 row ("DailyReport ... rent every 7 days",
    /// acceptance: "profit = revenue - costs to the cent") and §11's Tier-1
    /// invariant: <b>revenue - ingredient cost - rent - utilities = profit;
    /// rent only on day % 7</b>.
    ///
    /// Deliberately a static pure function of its arguments: no clock, no
    /// ambient state, no statics with memory. Everything time-varying
    /// (which day it is, what the day's ledger did) arrives as a parameter.
    /// That is exactly what makes it exercisable in the ~1s
    /// <c>dotnet test</c> loop instead of needing a running Unity scene.
    ///
    /// Money is <c>decimal</c> end to end (architecture.md §4 decision 3).
    ///
    /// -------------------------------------------------------------------
    /// RENT BOUNDARY -- the exact interpretation, stated because "day % 7"
    /// alone does not pin it down:
    ///
    /// <b>Rent is charged when <c>day % RentIntervalDays == 0</c>, so with
    /// the shipped interval of 7 the first rent day is day 7.</b>
    ///
    /// Justification:
    /// <list type="bullet">
    /// <item><c>DayClock</c> starts at day 1
    /// (<c>RestaurantStateService.StartDay = 1</c>), so days run 1, 2, 3...
    /// There is no day 0 in play.</item>
    /// <item>architecture.md §11 words the rule as "rent only on day % 7".
    /// Read as a predicate, the only natural reading of a bare "day % 7" is
    /// <c>day % 7 == 0</c>. Any other residue would have to be written out
    /// (e.g. "day % 7 == 1"), and it is not.</item>
    /// <item>Landing on day 7 also means rent falls at the END of the
    /// player's first full week rather than on the opening morning. The
    /// alternative (<c>day % 7 == 1</c>, first rent on day 1) bills the
    /// player before a single bowl has been sold, which is both worse as
    /// game design and harder to reconcile with M11's "profit = revenue -
    /// costs" acceptance framing on an opening day with no revenue.</item>
    /// <item>Consequence, spelled out so nobody has to re-derive it: rent
    /// days are 7, 14, 21, ... and days 1-6 are rent-free.</item>
    /// </list>
    ///
    /// -------------------------------------------------------------------
    /// RENT SOURCING -- rent is computed from the CONFIG SCHEDULE, not read
    /// back out of the ledger, and that is a decision rather than an
    /// oversight. Nothing in the codebase currently debits
    /// <see cref="LedgerCategory.Rent"/> (as of this pass the only cash
    /// movement anywhere is <c>CustomerAgent.Pay</c>'s <c>Sale</c> credit),
    /// so a ledger-sourced rent figure would be permanently 0m and the
    /// "rent only on day % 7" invariant would be untestable and unmet.
    /// <b>Hazard for whoever adds the rent auto-debit:</b> once
    /// <c>EconomyService.Debit(cfg.RentAmount, LedgerCategory.Rent)</c>
    /// exists, cash and this report must not both grow a rent line
    /// independently -- either keep this schedule-derived (and let the debit
    /// simply move the cash), which is the intended design, or switch this
    /// to ledger-sourced. Doing both is a double count.
    /// <c>DayLedgerAccumulator</c> already ignores the <c>Rent</c> category
    /// precisely so the auto-debit cannot silently start double-counting.
    ///
    /// -------------------------------------------------------------------
    /// UTILITIES GAP -- <see cref="IBalanceConfig"/> is frozen and has NO
    /// utilities field (it carries <c>RentAmount</c>/<c>RentIntervalDays</c>
    /// and nothing else economic), while <see cref="DailyReport"/> and
    /// <c>DailyReportDto</c> both DO have a utilities field. Rather than
    /// widen a frozen interface, utilities enter here as an explicit
    /// parameter defaulting to <see cref="DefaultDailyUtilities"/>. This is
    /// a real balance-config gap and should become an
    /// <c>IBalanceConfig.DailyUtilities</c> knob in a later additive pass
    /// (the interface's own doc comment says it "grows additively as
    /// milestones land", so this is a sanctioned kind of change -- just not
    /// one to make unilaterally from inside this module).
    /// </summary>
    public static class DailyReportBuilder
    {
        /// <summary>
        /// Flat per-day utilities charge used when no explicit figure is
        /// supplied. Placeholder constant, NOT a balance-tuned value -- see
        /// the class doc comment's UTILITIES GAP. Chosen to match the
        /// $12.50/day already used as the representative utilities figure in
        /// <c>SaveRoundTripTests</c>' fixture data, so the number a reader
        /// sees in a report matches the number they have seen elsewhere in
        /// the repo rather than being a third invented value.
        /// </summary>
        public const decimal DefaultDailyUtilities = 12.50m;

        /// <summary>
        /// True iff rent falls due on <paramref name="day"/> -- see the
        /// class doc comment's RENT BOUNDARY section for the exact
        /// interpretation and why.
        ///
        /// Guards, both inert-not-throwing to match the codebase's
        /// missing-dependency convention: a non-positive
        /// <paramref name="rentIntervalDays"/> means "no rent schedule
        /// configured" (and avoids a DivideByZeroException on an
        /// unconfigured ScriptableObject whose int field defaults to 0),
        /// and a non-positive <paramref name="day"/> is not a real play day
        /// (day 0 would otherwise satisfy <c>0 % 7 == 0</c> and bill rent on
        /// a day that never happens).
        /// </summary>
        public static bool IsRentDue(int day, int rentIntervalDays)
        {
            if (rentIntervalDays <= 0) return false;
            if (day <= 0) return false;
            return day % rentIntervalDays == 0;
        }

        /// <summary>
        /// Rent charged on <paramref name="day"/>: the configured amount on
        /// a rent day, 0m otherwise. A null config or a non-positive
        /// configured amount yields 0m rather than throwing, matching the
        /// project's inert-when-unbound convention.
        /// </summary>
        public static decimal RentFor(int day, IBalanceConfig cfg)
        {
            if (cfg == null) return 0m;
            if (!IsRentDue(day, cfg.RentIntervalDays)) return 0m;

            // IBalanceConfig.RentAmount is already `decimal` at the Domain
            // contract, so no cast is needed HERE -- but note where the
            // number comes from: Pho.Data's GameBalanceConfig stores it as a
            // serialized `double` and exposes `(decimal)rentAmount`, because
            // Unity's ScriptableObject serializer cannot persist `decimal`
            // at all (it silently drops the field, it isn't merely awkward
            // in the Inspector). That deliberate narrowing happens once, at
            // the Data boundary; everything from this interface inward is
            // decimal, so no float error can creep into the report's
            // arithmetic.
            var rent = cfg.RentAmount;
            return rent > 0m ? rent : 0m;
        }

        /// <summary>
        /// Builds the report for <paramref name="day"/> using
        /// <see cref="DefaultDailyUtilities"/>.
        /// </summary>
        public static DailyReport Build(int day, in DayLedgerTotals totals, IBalanceConfig cfg)
            => Build(day, totals, cfg, DefaultDailyUtilities);

        /// <summary>
        /// Builds the report for <paramref name="day"/>.
        ///
        /// <paramref name="day"/> MUST be the day the report covers -- i.e.
        /// the day that just ENDED, not the newly-started one. Passing the
        /// new day is the classic off-by-one here and would shift every
        /// rent charge by one day.
        ///
        /// The returned report always satisfies
        /// <c>Profit == Revenue - IngredientCost - Rent - Utilities</c>
        /// exactly (decimal arithmetic, no rounding step), which is
        /// architecture.md §11's Tier-1 invariant for this suite.
        /// </summary>
        public static DailyReport Build(int day, in DayLedgerTotals totals, IBalanceConfig cfg, decimal utilities)
        {
            var revenue = totals.Revenue;
            var ingredientCost = totals.IngredientCost;
            var rent = RentFor(day, cfg);

            // A negative utilities charge is nonsense (it would read as the
            // power company paying the restaurant) and would push profit
            // above revenue. Floor it, consistent with RentFor's flooring.
            var utilitiesCharged = utilities > 0m ? utilities : 0m;

            return new DailyReport
            {
                Day = day,
                Revenue = revenue,
                IngredientCost = ingredientCost,
                Rent = rent,
                Utilities = utilitiesCharged,
                Profit = revenue - ingredientCost - rent - utilitiesCharged
            };
        }
    }
}
