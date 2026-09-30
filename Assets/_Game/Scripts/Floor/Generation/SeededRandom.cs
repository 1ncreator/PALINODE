using System.Collections.Generic;

namespace Palinode.Floor
{
    /// <summary>
    /// Small deterministic RNG (SplitMix64, from RogueDungeon). Unlike System.Random its sequence is fixed across
    /// runtimes, so a seed always produces the same floor.
    /// </summary>
    public sealed class SeededRandom
    {
        private ulong _state;

        public SeededRandom(int seed)
        {
            // The seed is hashed first: with a plain "seed × γ" start, neighbouring seeds gave the same sequence
            // shifted by one step (and often the very same floor).
            ulong z = (ulong)(uint)seed + 0x632BE59BD9B4E019UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            _state = z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            ulong z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public int NextInt() => (int)(NextULong() >> 33);

        /// <summary>Integer in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextULong() % (ulong)(maxExclusive - minInclusive));
        }

        /// <summary>Float in [0, 1).</summary>
        public float Value() => (NextULong() >> 40) / (float)(1UL << 24);

        public float Range(float min, float max) => min + (max - min) * Value();

        public bool Chance(float probability) => Value() < probability;

        public T Pick<T>(IList<T> list) => list[Range(0, list.Count)];

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>Index picked by weight; -1 if all weights are 0.</summary>
        public int Weighted(IList<float> weights)
        {
            float total = 0f;
            foreach (float w in weights) total += w > 0f ? w : 0f;
            if (total <= 0f) return -1;
            float roll = Value() * total;
            int last = -1;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0f) continue;
                last = i;
                roll -= weights[i];
                if (roll < 0f) return i;
            }
            return last;
        }

        public static int Mix(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 0x85EBCA6Bu ^ (uint)salt * 0xC2B2AE35u;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                return (int)h;
            }
        }
    }
}
