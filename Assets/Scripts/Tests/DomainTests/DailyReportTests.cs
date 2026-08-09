using NUnit.Framework;
using Pho.Domain.Economy;
using Pho.Domain.Events;
using Pho.Domain.Tests.Fakes;

namespace Pho.Domain.Tests
{
    // Plain NUnit only (no UnityEngine.TestTools / [UnityTest]) -- this file
    // is compiled both by Unity's EditMode test runner and by
    // Tools/PhoDomain.Tests.csproj via `dotnet test`. Constraint-based
    // Assert.That(...) throughout, matching InventoryModelTests.cs style.
    //
    // Covers architecture.md §11's Tier-1 row for this suite:
    // "revenue - ingredient cost - rent - utilities = profit;
    //  rent only on day % 7".
    [TestFixture]
    public class DailyReportTests
    {
        static FakeBalanceConfig Config(decimal rent = 700m, int interval = 7)
            => new FakeBalanceConfig { RentAmount = rent, RentIntervalDays = interval };

        static DayLedgerTotals Totals(decimal revenue, decimal ingredientCost)
            => new DayLedgerTotals(revenue, ingredientCost);

        // ---------------------------------------------------------------
        // The profit identity -- the headline invariant.
        // ---------------------------------------------------------------

        [Test]
        public void Build_ProfitEqualsRevenueMinusAllCosts_OnANonRentDay()
        {
            var report = DailyReportBuilder.Build(3, Totals(300.00m, 80.00m), Config(), utilities: 12.50m);

            Assert.That(report.Day, Is.EqualTo(3));
            Assert.That(report.Revenue, Is.EqualTo(300.00m));
            Assert.That(report.IngredientCost, Is.EqualTo(80.00m));
            Assert.That(report.Rent, Is.EqualTo(0m), "day 3 is not a rent day");
            Assert.That(report.Utilities, Is.EqualTo(12.50m));
            Assert.That(report.Profit, Is.EqualTo(207.50m));
        }

        [Test]
        public void Build_ProfitEqualsRevenueMinusAllCosts_OnARentDay()
        {
            var report = DailyReportBuilder.Build(7, Totals(300.00m, 80.00m), Config(rent: 700m), utilities: 12.50m);

            Assert.That(report.Rent, Is.EqualTo(700.00m));
            Assert.That(report.Profit, Is.EqualTo(-492.50m), "rent day can legitimately post a loss");
        }

        [Test]
        public void Build_ProfitIdentityHolds_ForAssortedInputs()
        {
            var cfg = Config(rent: 137.77m);

            for (int day = 1; day <= 30; day++)
            {
                var revenue = 11.11m * day;
                var cost = 3.33m * day;
                var utilities = 0.07m * day;

                var report = DailyReportBuilder.Build(day, Totals(revenue, cost), cfg, utilities);

                Assert.That(
                    report.Profit,
                    Is.EqualTo(report.Revenue - report.IngredientCost - report.Rent - report.Utilities),
                    $"profit identity must hold exactly on day {day}");
            }
        }

        [Test]
        public void Build_UsesDecimalArithmetic_NoFloatingPointDrift()
        {
            // 0.1 + 0.2 style drift is exactly what architecture.md §4
            // decision 3 ("Float money produces $9.499999") forbids.
            var report = DailyReportBuilder.Build(1, Totals(0.30m, 0.10m), Config(), utilities: 0.20m);

            Assert.That(report.Profit, Is.EqualTo(0.00m));
            Assert.That(report.Profit.ToString(), Is.EqualTo("0.00"), "decimal must preserve scale, proving no double round-trip happened");
        }

        // ---------------------------------------------------------------
        // Rent only on day % 7.
        // ---------------------------------------------------------------

        [Test]
        public void IsRentDue_OnlyOnMultiplesOfTheInterval_FirstRentDayIsDay7()
        {
            // The documented boundary interpretation: day % 7 == 0, and
            // DayClock starts at day 1, so days 1-6 are rent-free and the
            // first rent day is day 7.
            for (int day = 1; day <= 6; day++)
            {
                Assert.That(DailyReportBuilder.IsRentDue(day, 7), Is.False, $"day {day} must be rent-free");
            }

            Assert.That(DailyReportBuilder.IsRentDue(7, 7), Is.True, "day 7 is the first rent day");
            Assert.That(DailyReportBuilder.IsRentDue(14, 7), Is.True);
            Assert.That(DailyReportBuilder.IsRentDue(21, 7), Is.True);
            Assert.That(DailyReportBuilder.IsRentDue(13, 7), Is.False);
            Assert.That(DailyReportBuilder.IsRentDue(15, 7), Is.False);
        }

        [Test]
        public void IsRentDue_DayOne_IsNotARentDay()
        {
            // Explicitly pins the rejected alternative interpretation
            // (day % 7 == 1 -> rent on the opening morning).
            Assert.That(DailyReportBuilder.IsRentDue(1, 7), Is.False);
        }

        [Test]
        public void IsRentDue_NonPositiveDay_IsNeverRentDue()
        {
            // Day 0 satisfies 0 % 7 == 0 but is not a real play day.
            Assert.That(DailyReportBuilder.IsRentDue(0, 7), Is.False);
            Assert.That(DailyReportBuilder.IsRentDue(-7, 7), Is.False);
        }

        [Test]
        public void IsRentDue_NonPositiveInterval_IsNeverRentDue_AndDoesNotThrow()
        {
            // An unconfigured ScriptableObject leaves rentIntervalDays at 0;
            // this must be inert, not a DivideByZeroException.
            Assert.That(() => DailyReportBuilder.IsRentDue(7, 0), Throws.Nothing);
            Assert.That(DailyReportBuilder.IsRentDue(7, 0), Is.False);
            Assert.That(DailyReportBuilder.IsRentDue(7, -1), Is.False);
        }

        [Test]
        public void IsRentDue_HonoursANonDefaultInterval()
        {
            Assert.That(DailyReportBuilder.IsRentDue(5, 5), Is.True);
            Assert.That(DailyReportBuilder.IsRentDue(7, 5), Is.False);
            Assert.That(DailyReportBuilder.IsRentDue(10, 5), Is.True);
        }

        [Test]
        public void RentFor_NullConfig_IsZero_AndDoesNotThrow()
        {
            Assert.That(() => DailyReportBuilder.RentFor(7, null), Throws.Nothing);
            Assert.That(DailyReportBuilder.RentFor(7, null), Is.EqualTo(0m));
        }

        [Test]
        public void Build_NullConfig_StillProducesACoherentReport()
        {
            var report = DailyReportBuilder.Build(7, Totals(100m, 25m), null, utilities: 10m);

            Assert.That(report.Rent, Is.EqualTo(0m));
            Assert.That(report.Profit, Is.EqualTo(65m));
        }

        [Test]
        public void Build_NegativeUtilities_AreFlooredAtZero()
        {
            var report = DailyReportBuilder.Build(1, Totals(100m, 0m), Config(), utilities: -50m);

            Assert.That(report.Utilities, Is.EqualTo(0m));
            Assert.That(report.Profit, Is.EqualTo(100m), "a negative charge must never inflate profit above revenue");
        }

        [Test]
        public void Build_DefaultUtilitiesOverload_UsesTheDocumentedConstant()
        {
            var report = DailyReportBuilder.Build(1, Totals(100m, 0m), Config());

            Assert.That(report.Utilities, Is.EqualTo(DailyReportBuilder.DefaultDailyUtilities));
            Assert.That(report.Profit, Is.EqualTo(100m - DailyReportBuilder.DefaultDailyUtilities));
        }

        // ---------------------------------------------------------------
        // Zero-activity days.
        // ---------------------------------------------------------------

        [Test]
        public void Build_ZeroActivityNonRentDay_LosesExactlyTheUtilities()
        {
            var report = DailyReportBuilder.Build(2, DayLedgerTotals.Empty, Config(), utilities: 12.50m);

            Assert.That(report.Revenue, Is.EqualTo(0m));
            Assert.That(report.IngredientCost, Is.EqualTo(0m));
            Assert.That(report.Rent, Is.EqualTo(0m));
            Assert.That(report.Profit, Is.EqualTo(-12.50m), "a closed day still owes utilities");
        }

        [Test]
        public void Build_ZeroActivityRentDay_LosesRentPlusUtilities()
        {
            var report = DailyReportBuilder.Build(14, DayLedgerTotals.Empty, Config(rent: 700m), utilities: 12.50m);

            Assert.That(report.Profit, Is.EqualTo(-712.50m));
        }

        [Test]
        public void Build_ZeroActivityAndZeroCharges_IsAnAllZeroReport()
        {
            var report = DailyReportBuilder.Build(3, DayLedgerTotals.Empty, Config(rent: 0m), utilities: 0m);

            Assert.That(report.Revenue, Is.EqualTo(0m));
            Assert.That(report.IngredientCost, Is.EqualTo(0m));
            Assert.That(report.Rent, Is.EqualTo(0m));
            Assert.That(report.Utilities, Is.EqualTo(0m));
            Assert.That(report.Profit, Is.EqualTo(0m));
        }

        // ---------------------------------------------------------------
        // Multi-day sequence.
        // ---------------------------------------------------------------

        [Test]
        public void MultiDaySequence_RentAppearsExactlyTwiceOverFourteenDays()
        {
            var cfg = Config(rent: 700m);
            var rentDays = new System.Collections.Generic.List<int>();
            decimal totalRent = 0m;

            for (int day = 1; day <= 14; day++)
            {
                var report = DailyReportBuilder.Build(day, Totals(200m, 50m), cfg, utilities: 10m);
                if (report.Rent > 0m)
                {
                    rentDays.Add(report.Day);
                    totalRent += report.Rent;
                }
            }

            Assert.That(rentDays, Is.EqualTo(new[] { 7, 14 }));
            Assert.That(totalRent, Is.EqualTo(1400m));
        }

        [Test]
        public void MultiDaySequence_SummedProfitEqualsSummedRevenueMinusSummedCosts()
        {
            var cfg = Config(rent: 700m);
            decimal revenue = 0m, cost = 0m, rent = 0m, utilities = 0m, profit = 0m;

            for (int day = 1; day <= 21; day++)
            {
                var report = DailyReportBuilder.Build(day, Totals(180.25m + day, 47.50m), cfg, utilities: 12.50m);
                revenue += report.Revenue;
                cost += report.IngredientCost;
                rent += report.Rent;
                utilities += report.Utilities;
                profit += report.Profit;
            }

            Assert.That(rent, Is.EqualTo(2100m), "three rent days across 21 days: 7, 14, 21");
            Assert.That(profit, Is.EqualTo(revenue - cost - rent - utilities));
        }

        // ---------------------------------------------------------------
        // Accumulator: category mapping.
        // ---------------------------------------------------------------

        [Test]
        public void Accumulator_StartsEmpty()
        {
            var acc = new DayLedgerAccumulator();

            Assert.That(acc.Revenue, Is.EqualTo(0m));
            Assert.That(acc.IngredientCost, Is.EqualTo(0m));
        }

        [Test]
        public void Accumulator_SaleAndTip_BothCountAsRevenue()
        {
            var acc = new DayLedgerAccumulator();

            acc.Record(9.50m, LedgerCategory.Sale);
            acc.Record(12.00m, LedgerCategory.Sale);
            acc.Record(2.25m, LedgerCategory.Tip);

            Assert.That(acc.Revenue, Is.EqualTo(23.75m));
            Assert.That(acc.IngredientCost, Is.EqualTo(0m));
        }

        [Test]
        public void Accumulator_IngredientPurchaseDebit_BecomesAPositiveCost()
        {
            var acc = new DayLedgerAccumulator();

            // EconomyService.Debit applies a NEGATIVE delta.
            acc.Record(-40.00m, LedgerCategory.IngredientPurchase);
            acc.Record(-15.50m, LedgerCategory.IngredientPurchase);

            Assert.That(acc.IngredientCost, Is.EqualTo(55.50m));
            Assert.That(acc.Revenue, Is.EqualTo(0m));
        }

        [Test]
        public void Accumulator_RefundAgainstSale_NetsRevenueBackDown()
        {
            var acc = new DayLedgerAccumulator();

            acc.Record(20.00m, LedgerCategory.Sale);
            acc.Record(-20.00m, LedgerCategory.Sale);

            Assert.That(acc.Revenue, Is.EqualTo(0m));
        }

        [Test]
        public void Accumulator_IgnoresRentEquipmentAndOther()
        {
            var acc = new DayLedgerAccumulator();

            // Rent: schedule-derived on the report, so counting it here
            // would double-count once a rent auto-debit lands.
            acc.Record(-700.00m, LedgerCategory.Rent);
            // Equipment: capex, no field on the frozen DailyReport shape.
            acc.Record(-400.00m, LedgerCategory.Equipment);
            // Other: EconomyService.Restore's save-reconciliation channel --
            // counting it would book a whole save file's cash as revenue.
            acc.Record(1500.00m, LedgerCategory.Other);

            Assert.That(acc.Revenue, Is.EqualTo(0m));
            Assert.That(acc.IngredientCost, Is.EqualTo(0m));
        }

        [Test]
        public void Accumulator_SnapshotDoesNotMutate_ResetClears()
        {
            var acc = new DayLedgerAccumulator();
            acc.Record(50m, LedgerCategory.Sale);
            acc.Record(-20m, LedgerCategory.IngredientPurchase);

            var first = acc.Snapshot();
            var second = acc.Snapshot();

            Assert.That(first.Revenue, Is.EqualTo(50m));
            Assert.That(second.Revenue, Is.EqualTo(50m), "Snapshot must be non-destructive");
            Assert.That(second.IngredientCost, Is.EqualTo(20m));

            acc.Reset();

            Assert.That(acc.Revenue, Is.EqualTo(0m));
            Assert.That(acc.IngredientCost, Is.EqualTo(0m));
            Assert.That(first.Revenue, Is.EqualTo(50m), "a previously taken snapshot must survive a Reset");
        }

        // ---------------------------------------------------------------
        // Day-boundary off-by-one. These are the regression guards for the
        // hazard in RestaurantStateServiceBehaviour.Update: the report must
        // cover the day that ENDED, and the accumulator must reset so the
        // next day starts clean.
        // ---------------------------------------------------------------

        /// <summary>
        /// Reproduces the exact day-boundary sequence
        /// RestaurantStateServiceBehaviour.Update performs, without Unity:
        /// snapshot the ended day, build, reset, advance the day cursor.
        /// </summary>
        static DailyReport CloseDay(int endedDay, DayLedgerAccumulator acc, FakeBalanceConfig cfg)
        {
            var report = DailyReportBuilder.Build(endedDay, acc.Snapshot(), cfg, utilities: 10m);
            acc.Reset();
            return report;
        }

        [Test]
        public void DayBoundary_ReportCoversTheDayThatEnded_NotTheNewDay()
        {
            var cfg = Config(rent: 700m);
            var acc = new DayLedgerAccumulator();
            acc.Record(120m, LedgerCategory.Sale);

            // The clock has just ticked from day 6 to day 7. The report must
            // describe day 6 -- and therefore must NOT charge rent, because
            // day 7 has not been lived yet.
            const int endedDay = 6;
            const int newDay = 7;

            var report = CloseDay(endedDay, acc, cfg);

            Assert.That(report.Day, Is.EqualTo(endedDay), "the report must be stamped with the day that ended");
            Assert.That(report.Day, Is.Not.EqualTo(newDay), "stamping the NEW day is the off-by-one this test exists to catch");
            Assert.That(report.Rent, Is.EqualTo(0m), "using the new day (7) here would charge rent a full day early");
        }

        [Test]
        public void DayBoundary_RentLandsOnTheReportForDay7_WhenDay7Ends()
        {
            var cfg = Config(rent: 700m);
            var acc = new DayLedgerAccumulator();
            acc.Record(500m, LedgerCategory.Sale);

            // Clock ticked from day 7 to day 8; the ended day is 7.
            var report = CloseDay(7, acc, cfg);

            Assert.That(report.Day, Is.EqualTo(7));
            Assert.That(report.Rent, Is.EqualTo(700m), "rent must be billed on the report FOR day 7, i.e. when day 7 ends");
        }

        [Test]
        public void DayBoundary_AccumulatorResets_SoTheNextDayDoesNotInheritRevenue()
        {
            var cfg = Config();
            var acc = new DayLedgerAccumulator();

            acc.Record(100m, LedgerCategory.Sale);
            acc.Record(-30m, LedgerCategory.IngredientPurchase);
            var day1 = CloseDay(1, acc, cfg);

            // Day 2: no activity at all.
            var day2 = CloseDay(2, acc, cfg);

            Assert.That(day1.Revenue, Is.EqualTo(100m));
            Assert.That(day1.IngredientCost, Is.EqualTo(30m));
            Assert.That(day2.Revenue, Is.EqualTo(0m), "day 2 must not inherit day 1's revenue");
            Assert.That(day2.IngredientCost, Is.EqualTo(0m), "day 2 must not inherit day 1's ingredient cost");
        }

        [Test]
        public void DayBoundary_CashMovedByADayEndedHandler_BelongsToTheNewDay()
        {
            // Update() resets BEFORE publishing DayEnded precisely so a
            // handler-driven transaction lands in the new day's bucket.
            var cfg = Config();
            var acc = new DayLedgerAccumulator();
            acc.Record(200m, LedgerCategory.Sale);

            var day1 = CloseDay(1, acc, cfg);           // snapshot + reset
            acc.Record(-25m, LedgerCategory.IngredientPurchase); // "handler" fires after the reset

            var day2 = CloseDay(2, acc, cfg);

            Assert.That(day1.Revenue, Is.EqualTo(200m));
            Assert.That(day1.IngredientCost, Is.EqualTo(0m));
            Assert.That(day2.IngredientCost, Is.EqualTo(25m), "post-reset activity must be booked to the new day, not lost");
        }

        [Test]
        public void DayBoundary_FourteenDayRun_ChargesRentOnExactlyTheEndedDays7And14()
        {
            var cfg = Config(rent: 700m);
            var acc = new DayLedgerAccumulator();
            var rentDays = new System.Collections.Generic.List<int>();

            for (int endedDay = 1; endedDay <= 14; endedDay++)
            {
                acc.Record(100m, LedgerCategory.Sale);
                var report = CloseDay(endedDay, acc, cfg);

                Assert.That(report.Revenue, Is.EqualTo(100m), $"day {endedDay} should see exactly its own single sale");
                if (report.Rent > 0m) rentDays.Add(report.Day);
            }

            Assert.That(rentDays, Is.EqualTo(new[] { 7, 14 }));
        }
    }
}
