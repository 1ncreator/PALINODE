using System.Collections.Generic;
using Palinode.Core;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>Elias's numbers after items (recomputed whenever the item list changes).</summary>
    public sealed class PlayerStats
    {
        public float Damage, FireRate, Range, ShotSpeed, Speed;
        public bool Pierce, DoubleShot, LetterShots;
        public float CopyDamage = 1f;
        public int StampEvery;
        public float StampMul = 1f, StampStun;
        public int AbsorbPerRoom;
        public float MicroSleepEvery, MicroSleep;

        public static PlayerStats Compute(JNode player, JNode items, IEnumerable<string> owned)
        {
            var s = new PlayerStats
            {
                Damage = player.Num("damage", 3.5f),
                FireRate = player.Num("fireRate", 2.7f),
                Range = player.Num("range", 6.5f),
                ShotSpeed = player.Num("shotSpeed", 8f),
                Speed = player.Num("speed", 4.5f)
            };
            foreach (var id in owned)
            {
                var it = items[id];
                if (it.IsNull) continue;
                s.FireRate *= it.Num("fireRateMul", 1f);
                // The stamp's "damageMul" applies to stamp shots only.
                if (!it.Has("every")) s.Damage *= it.Num("damageMul", 1f);
                s.Range += it.Num("rangeAdd", 0f);
                s.ShotSpeed *= it.Num("shotSpeedMul", 1f);
                s.Speed += it.Num("speedAdd", 0f);
                if (it.Bool("pierce")) s.Pierce = true;
                if (it.Bool("letterShots")) s.LetterShots = true;
                if (it.Bool("doubleShot")) { s.DoubleShot = true; s.CopyDamage = it.Num("copyDamage", 0.6f); }
                if (it.Has("every")) { s.StampEvery = it.Int("every", 5); s.StampMul = it.Num("damageMul", 3f); s.StampStun = it.Num("stun", 0.5f); }
                if (it.Has("absorbPerRoom")) s.AbsorbPerRoom += it.Int("absorbPerRoom", 1);
                if (it.Has("microSleepEvery")) { s.MicroSleepEvery = it.Num("microSleepEvery", 20f); s.MicroSleep = it.Num("microSleep", 0.3f); }
            }
            return s;
        }
    }

    /// <summary>
    /// One run of Floor I (a revision): seed, items, sheets, red pencil, visited rooms. Rebuilt on every new run.
    /// </summary>
    public sealed class RunState
    {
        public int Seed;
        public int Revision;
        public readonly List<string> Items = new List<string>();
        public int Sheets;
        public bool HasPencil;
        public int PencilCharge;              // cleared rooms since the last use (full at "charge")
        public bool TookPencil, LeftPencil;   // for the Archivist
        public bool MemoryHealed;
        public readonly List<int> VisitOrder = new List<int>();   // room indices in the order first entered ("pages")
        public readonly List<int> PagesThisRun = new List<int>();

        public int PageOf(int roomIndex)
        {
            int i = VisitOrder.IndexOf(roomIndex);
            return i >= 0 ? i + 1 : VisitOrder.Count + 1;
        }

        public void Visit(int roomIndex)
        {
            if (!VisitOrder.Contains(roomIndex)) VisitOrder.Add(roomIndex);
        }

        public static int NewSeed(int configured)
        {
            if (configured != 0) return configured;
            return unchecked((int)(System.DateTime.UtcNow.Ticks ^ (long)Random.Range(int.MinValue, int.MaxValue)));
        }
    }
}
