using System.Collections.Generic;
using Pho.Domain.Contracts;
using Pho.Domain.Identity;
using Pho.Domain.MathUtil;

namespace Pho.Domain.Cooking
{
    /// <summary>
    /// One ingredient that went into a bowl, recorded exactly as consumed --
    /// see architecture.md section 7. Quality01/Freshness01 come from the
    /// actual InventoryModel.TryConsume(...) -> ConsumedIngredient result for
    /// the lot that was used, not the ingredient's base/static stats, so a
    /// spoiled lot shows up in the score with zero extra plumbing.
    /// </summary>
    public readonly struct BowlComponent
    {
        public readonly ComponentSlot Slot;
        public readonly IngredientId Ingredient;
        public readonly float Amount;
        public readonly float Quality01;
        public readonly float Freshness01;
        public readonly float AddedAtGameSeconds;

        public BowlComponent(
            ComponentSlot slot,
            IngredientId ingredient,
            float amount,
            float quality01,
            float freshness01,
            float addedAtGameSeconds)
        {
            Slot = slot;
            Ingredient = ingredient;
            Amount = amount;
            Quality01 = quality01;
            Freshness01 = freshness01;
            AddedAtGameSeconds = addedAtGameSeconds;
        }
    }

    /// <summary>Outcome of BowlContents.Add -- success or the specific rejection reason.</summary>
    public enum AddResult
    {
        Ok,
        AlreadySealed,
        SlotOverCapacity,
    }

    /// <summary>
    /// Mutable runtime record of what has physically gone into a bowl. Pure
    /// (Pho.Domain, no engine references) per architecture.md section 7 --
    /// this directly satisfies GDD §75: a dish must record exactly what was
    /// placed in it, not fake completion via a single Cook button.
    /// </summary>
    public sealed class BowlContents
    {
        // TODO(balance): the values below (poured-broth temperature, ambient
        // temperature, cooling rate) belong in IBalanceConfig once it grows a
        // broth/serving-temperature section. IBalanceConfig as it exists today
        // (Domain/Contracts/DefInterfaces.cs) has no such fields and is frozen
        // for this pass, so these are clearly-marked placeholder constants
        // rather than a silent hardcoded balance value.
        public const float PouredBrothTemperatureC = 85f;
        public const float AmbientTemperatureC = 22f;

        // Fraction of the (current -> ambient) temperature gap closed per
        // simulated second while the bowl sits out -- an exponential decay
        // cooling curve, matching how a real bowl of soup cools.
        public const float CoolingRatePerSecond = 0.02f;

        /// <summary>
        /// Per-slot capacity: how many *separate* BowlComponent entries a slot
        /// accepts before Add() rejects further adds with SlotOverCapacity.
        /// Not specified numerically by architecture.md section 7 ("rejects if
        /// ... slot over-capacity" with no numbers given) -- this is a
        /// judgment call, documented here rather than scattered as a magic
        /// number: Broth and Noodle are a single pour/single serving by the
        /// nature of the dish (one ladle of broth, one nest of noodles) so
        /// capacity is 1. Protein allows a handful of distinct cuts (raw beef
        /// + well-done brisket + tendon, etc.) so capacity is 3. Aromatic /
        /// Herb / Garnish are cheap, multi-item toppings by design (onion,
        /// cilantro, lime, chili, bean sprouts, ...) so they get a more
        /// generous capacity.
        /// </summary>
        static int CapacityFor(ComponentSlot slot)
        {
            switch (slot)
            {
                case ComponentSlot.Broth: return 1;
                case ComponentSlot.Noodle: return 1;
                case ComponentSlot.Protein: return 3;
                case ComponentSlot.Aromatic: return 4;
                case ComponentSlot.Herb: return 5;
                case ComponentSlot.Garnish: return 5;
                default: return 1;
            }
        }

        readonly List<BowlComponent> _components = new List<BowlComponent>();

        public IReadOnlyList<BowlComponent> Components => _components;
        public float BrothTemperatureC { get; private set; }
        public float AssembledAtGameSeconds { get; private set; }
        public bool IsSealed { get; private set; }

        /// <summary>
        /// Rejects if the bowl is already sealed (placed on the pass) or the
        /// component's slot is already at capacity (see CapacityFor). Adding a
        /// Broth-slot component sets BrothTemperatureC to the "just poured"
        /// placeholder baseline -- BowlComponent carries no temperature field
        /// of its own (frozen shape, section 7), so Add() cannot read the
        /// pot's real BrothState.TemperatureC at pour time without an extra
        /// parameter the doc doesn't give it. A Kitchen adapter that has the
        /// real BrothState can be wired in later (post-M5) to set a more
        /// accurate value once that plumbing exists.
        /// </summary>
        public AddResult Add(in BowlComponent c)
        {
            if (IsSealed)
                return AddResult.AlreadySealed;

            int countInSlot = 0;
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i].Slot == c.Slot)
                    countInSlot++;
            }

            if (countInSlot >= CapacityFor(c.Slot))
                return AddResult.SlotOverCapacity;

            _components.Add(c);

            if (c.Slot == ComponentSlot.Broth)
                BrothTemperatureC = PouredBrothTemperatureC;

            return AddResult.Ok;
        }

        /// <summary>Marks the bowl as placed on the pass. One-way; sealing twice just re-stamps the time.</summary>
        public void Seal(float nowSeconds)
        {
            IsSealed = true;
            AssembledAtGameSeconds = nowSeconds;
        }

        /// <summary>
        /// Exponentially decays BrothTemperatureC toward ambient. `cfg` is
        /// accepted per the frozen BowlContents shape (section 7) for a future
        /// cooling-rate config surface; IBalanceConfig has no such field today
        /// (see the class-level TODO(balance) note), so the rate is a
        /// placeholder constant and cfg is currently unused.
        /// </summary>
        public void CoolTowardsAmbient(float dt, IBalanceConfig cfg)
        {
            if (dt <= 0f)
                return;

            float t = MathP.Clamp01(CoolingRatePerSecond * dt);
            BrothTemperatureC = MathP.Lerp(BrothTemperatureC, AmbientTemperatureC, t);
        }
    }
}
