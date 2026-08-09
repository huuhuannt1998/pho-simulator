using System;

namespace Pho.Domain.Infra
{
    /// <summary>
    /// Injected randomness -- never UnityEngine.Random from domain logic.
    /// Runtime uses SystemRandom; tests use SeededRandom for deterministic,
    /// reproducible customer behaviour (see CustomerBrainTests, M7).
    /// </summary>
    public interface IRandom
    {
        float NextFloat01();
        float NextFloat(float min, float max);
        int NextInt(int minInclusive, int maxExclusive);
        bool NextBool(float chance01);
    }

    public sealed class SystemRandom : IRandom
    {
        readonly Random _rng = new Random();

        public float NextFloat01() => (float)_rng.NextDouble();
        public float NextFloat(float min, float max) => min + (max - min) * NextFloat01();
        public int NextInt(int minInclusive, int maxExclusive) => _rng.Next(minInclusive, maxExclusive);
        public bool NextBool(float chance01) => NextFloat01() < chance01;
    }

    public sealed class SeededRandom : IRandom
    {
        readonly Random _rng;

        public SeededRandom(int seed)
        {
            _rng = new Random(seed);
        }

        public float NextFloat01() => (float)_rng.NextDouble();
        public float NextFloat(float min, float max) => min + (max - min) * NextFloat01();
        public int NextInt(int minInclusive, int maxExclusive) => _rng.Next(minInclusive, maxExclusive);
        public bool NextBool(float chance01) => NextFloat01() < chance01;
    }
}
