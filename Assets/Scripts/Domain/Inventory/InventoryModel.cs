using System;
using System.Collections.Generic;
using System.Linq;
using Pho.Domain.Contracts;
using Pho.Domain.Identity;
using Pho.Domain.MathUtil;

namespace Pho.Domain.Inventory
{
    /// <summary>
    /// One FIFO stack of a single ingredient in the fridge/pantry. Mutable --
    /// Quantity drains on consume, Freshness01 decays over time via
    /// InventoryModel.AdvanceSpoilage. See architecture.md section 2.3.
    /// </summary>
    public sealed class IngredientLot
    {
        public IngredientId Ingredient;
        public float Quantity;
        public float QualityAtPurchase01;
        public float Freshness01;
        public int PurchasedOnDay;
        public StorageRequirement StoredIn;
    }

    /// <summary>
    /// What actually came out of a TryConsume call -- carries the true
    /// quality/freshness of the specific lot(s) consumed (possibly a
    /// weighted blend across lots), not just the ingredient's base stats.
    /// Feeds BowlComponent at M5.
    /// </summary>
    public readonly struct ConsumedIngredient
    {
        public readonly IngredientId Id;
        public readonly float Amount;
        public readonly float Quality01;
        public readonly float Freshness01;

        public ConsumedIngredient(IngredientId id, float amount, float quality01, float freshness01)
        {
            Id = id;
            Amount = amount;
            Quality01 = quality01;
            Freshness01 = freshness01;
        }
    }

    /// <summary>
    /// Pure runtime inventory: a bag of IngredientLot stacks. Consumption is
    /// FIFO by PurchasedOnDay (oldest lot first). If the oldest lot can't
    /// cover the full requested amount, TryConsume draws across successive
    /// lots in FIFO order and returns a ConsumedIngredient whose
    /// Quality01/Freshness01 are weighted-averaged by the amount contributed
    /// from each lot -- so "2kg of beef" against a 1.5kg oldest lot + a
    /// fresher second lot correctly blends both rather than failing.
    ///
    /// InventoryModel does not itself enforce StorageRequirement matching --
    /// Add accepts any StorageRequirement and Lots reports it back verbatim.
    /// Checking "does this ingredient belong in the fridge vs. the freezer"
    /// is placement logic that belongs upstream of Add, not inside the
    /// runtime stock model.
    /// </summary>
    public sealed class InventoryModel
    {
        readonly List<IngredientLot> _lots = new List<IngredientLot>();

        public IReadOnlyList<IngredientLot> Lots => _lots;

        /// <summary>
        /// Adds a new lot. Zero/negative amount is a programming error (never
        /// a legitimate runtime occurrence), so this throws rather than
        /// returning a bool.
        /// </summary>
        public void Add(IngredientId id, float amount, float quality01, int day, StorageRequirement storage)
        {
            if (amount <= 0f)
                throw new ArgumentException("Add amount must be positive.", nameof(amount));

            _lots.Add(new IngredientLot
            {
                Ingredient = id,
                Quantity = amount,
                QualityAtPurchase01 = quality01,
                Freshness01 = 1f,
                PurchasedOnDay = day,
                StoredIn = storage,
            });
        }

        /// <summary>Removes every lot. Save/load restore point -- see AddLot.</summary>
        public void Clear() => _lots.Clear();

        /// <summary>
        /// Adds a lot with an explicit Freshness01, unlike <see cref="Add"/>
        /// (which always pins a freshly-purchased lot's freshness to 1).
        /// Exists for save/load restore, where a lot's freshness at the
        /// moment of saving must round-trip exactly rather than being reset
        /// to "just bought."
        /// </summary>
        public void AddLot(IngredientId id, float amount, float quality01, float freshness01, int day, StorageRequirement storage)
        {
            if (amount <= 0f)
                throw new ArgumentException("Add amount must be positive.", nameof(amount));

            _lots.Add(new IngredientLot
            {
                Ingredient = id,
                Quantity = amount,
                QualityAtPurchase01 = quality01,
                Freshness01 = MathP.Clamp01(freshness01),
                PurchasedOnDay = day,
                StoredIn = storage,
            });
        }

        public float TotalQuantity(IngredientId id)
        {
            float total = 0f;
            for (int i = 0; i < _lots.Count; i++)
            {
                if (_lots[i].Ingredient.Equals(id))
                    total += _lots[i].Quantity;
            }
            return total;
        }

        /// <summary>
        /// Zero/negative amount and insufficient total stock are both normal
        /// runtime occurrences (fat-fingered input, out of stock) -- both
        /// return false rather than throwing, and never partially consume.
        /// </summary>
        public bool TryConsume(IngredientId id, float amount, out ConsumedIngredient result)
        {
            result = default;

            if (amount <= 0f)
                return false;

            if (TotalQuantity(id) < amount)
                return false;

            // Oldest lot first; LINQ OrderBy is a stable sort, so lots
            // purchased on the same day keep their original (insertion) order.
            var matching = _lots
                .Select((lot, index) => index)
                .Where(index => _lots[index].Ingredient.Equals(id))
                .OrderBy(index => _lots[index].PurchasedOnDay)
                .ToList();

            float remaining = amount;
            float qualitySum = 0f;
            float freshnessSum = 0f;

            foreach (var index in matching)
            {
                if (remaining <= 0f)
                    break;

                var lot = _lots[index];
                float take = Math.Min(lot.Quantity, remaining);

                qualitySum += lot.QualityAtPurchase01 * take;
                freshnessSum += lot.Freshness01 * take;

                lot.Quantity -= take;
                remaining -= take;
            }

            _lots.RemoveAll(l => l.Quantity <= 0f);

            result = new ConsumedIngredient(id, amount, qualitySum / amount, freshnessSum / amount);
            return true;
        }

        /// <summary>
        /// Pure: decays Freshness01 on every lot by cfg.SpoilageRatePerHour *
        /// gameHours, clamped to [0,1]. Fully-spoiled lots are left in place
        /// (waste/health-inspection handling is a later milestone).
        /// </summary>
        public void AdvanceSpoilage(float gameHours, IBalanceConfig cfg)
        {
            for (int i = 0; i < _lots.Count; i++)
            {
                var lot = _lots[i];
                lot.Freshness01 = MathP.Clamp01(lot.Freshness01 - cfg.SpoilageRatePerHour * gameHours);
            }
        }
    }
}
