using System;
using System.Collections.Generic;
using System.Linq;

namespace Hunter.Rules;

/// <summary>Recording only (HUNTER_CONVOY, a reel): land vehicles that follow the player's way, each a set distance behind the
/// one before - real live records, so the rules sort, collide and draw them as any other and a tank's tracks roll with its
/// movement. Spaced by distance along his way, not by time: when he stops they stop behind him, and a turn is taken where he
/// took it. Before he has gone far enough, his way is taken as a straight line behind him along his heading.
/// Run after every record has moved for the pass (Play.UpdateEntities), before the camera and the sort.</summary>
public sealed class Convoy
{
    /// <summary>World units between one follower and the next (a tank is about 600 long).</summary>
    public const int Gap = 800;
    /// <summary>A new point of his way each time he is this far from the last one.</summary>
    const int PointEvery = 24;

    readonly int[] models;
    readonly int[] slots;                                    // each follower's entity slot, -1 not yet made or gone
    readonly List<(double X, double Z)> way = [];            // his way, oldest first, the newest point his place at the last pass
    bool seeded;

    /// <summary>models: the followers' model numbers in order behind him ($15 tank, $22 jeep, $12 truck ...). escorts: flying
    /// machines (model, the follower k (0 the first behind him) they fly over) - a helicopter $17 or $19 kept Above over it.</summary>
    public Convoy(IEnumerable<int> models, IEnumerable<(int Model, int Over)>? escorts = null)
    {
        this.models = models.ToArray();
        slots = Enumerable.Repeat(-1, this.models.Length).ToArray();
        made = new bool[this.models.Length];
        this.escorts = (escorts ?? []).ToArray();
        escortSlots = Enumerable.Repeat(-1, this.escorts.Length).ToArray();
        escortMade = new bool[this.escorts.Length];
        escortPasses = new int[this.escorts.Length];
    }
    readonly int[] escortPasses;

    /// <summary>Is this entity slot one of the escorts? The rules do not move them (no gravity, landing or crash: a helicopter
    /// with no one in it falls) - Step places them over their vehicles each pass.</summary>
    public bool Holds(int slot) => slot >= 0 && Array.IndexOf(escortSlots, slot) >= 0;
    readonly bool[] made;
    readonly (int Model, int Over)[] escorts;
    readonly int[] escortSlots;
    readonly bool[] escortMade;
    /// <summary>How high an escort flies over its vehicle's ground, world units (a helicopter is about 200 tall).</summary>
    public const int Above = 350;
    /// <summary>What was made, for the run's console (GameView prints and clears it).</summary>
    public List<string> Log { get; } = [];

    /// <summary>The followers, spawned at the first pass he is alive, then placed on his way.</summary>
    public void Step(GameData d, WorldState s)
    {
        var me = s.Entities.All[0];
        if (!me.Alive) return;
        double px = me.UW(0x4A), pz = me.UW(0x4E);
        if (!seeded)
        {
            // his way so far: a straight line behind him along his heading (0 north, growing east: x = sin, z = cos)
            var h = me.B[0x7E] * Math.PI / 128;
            var back = Gap * (models.Length + 1);
            for (var t = back; t > 0; t -= PointEvery) way.Add((px - Math.Sin(h) * t, pz - Math.Cos(h) * t));
            seeded = true;
        }
        // on land (on the ground, not over the sea): his way grows; in the air or the water the followers hold at its last point
        var ground = s.Ground.Height((ushort)px, (ushort)pz);
        var onLand = ground > 0 && Math.Abs(me.W(0x4C) - ground) < 160;
        if (onLand && Dist(way[^1], (px, pz)) >= PointEvery) way.Add((px, pz));
        var from = onLand ? (px, pz) : way[^1];
        for (var k = 0; k < models.Length; k++)
        {
            var (at, ahead) = Along(from, Gap * (k + 1));
            var x = (int)Math.Round(at.X) & 0xFFFF;
            var z = (int)Math.Round(at.Z) & 0xFFFF;
            var g = s.Ground.Height((ushort)x, (ushort)z);
            if (!made[k])                                                    // each follower made once; one lost (wrecked, or written back to
            {                                                                // the map out of view) is not made again - a copy would pile up
                made[k] = true;
                var type = TypeOf(d, models[k]);
                if (type < 0) continue;
                var e = StartState.SpawnInFreeSlot(s, x, g, z, KindOf(d, type) << 16 | type, 0, 0);
                if (e is null) continue;
                slots[k] = Array.IndexOf(s.Entities.All, e);
            }
            if (slots[k] < 0 || !s.Entities.All[slots[k]].Alive || s.Entities.All[slots[k]].B[0x25] != models[k]) { slots[k] = -1; continue; }
            var f = s.Entities.All[slots[k]];
            // where it was, then where it is now: the in-between pictures and the tracks' roll read the step between
            f.SetW(0x50, f.W(0x4A)); f.SetW(0x52, f.W(0x4C)); f.SetW(0x54, f.W(0x4E));
            var moved = Math.Sqrt(Math.Pow(x - f.UW(0x4A), 2) + Math.Pow(z - f.UW(0x4E), 2));
            f.SetW(0x4A, x); f.SetW(0x4C, g); f.SetW(0x4E, z); f.SetW(0x74, g);
            f.SetW(0x3E, moved > 0.5 && onLand ? me.W(0x3E) : 0);        // under way as he is, still when he is
            // facing along his way toward the next follower's place (or his): the fine heading, the byte from it
            var dx = ahead.X - at.X; var dz = ahead.Z - at.Z;
            if (dx * dx + dz * dz > 1)
            {
                var fine = (ushort)((int)Math.Round(Math.Atan2(dx, dz) * 32768 / Math.PI) & 0xFFFF);
                f.HeadingFine = fine;
                f.B[0x7E] = (byte)(fine >> 8);
            }
        }
        // the escorts: each over its vehicle, Above its ground, facing as it does; its fall cleared every pass, so the
        // game's gravity never brings it down
        for (var j = 0; j < escorts.Length; j++)
        {
            var (model, over) = escorts[j];
            if (over < 0 || over >= models.Length || slots[over] < 0) continue;
            var v = s.Entities.All[slots[over]];
            int x = v.UW(0x4A), z = v.UW(0x4E), y = v.W(0x74) + Above;
            if (!escortMade[j])
            {
                escortMade[j] = true;
                var type = TypeOf(d, model);
                if (type < 0) continue;
                var e = StartState.SpawnInFreeSlot(s, x, y, z, KindOf(d, type) << 16 | type, 0, 0);
                if (e is null) { Log.Add($"escort {j}: no free slot"); continue; }
                escortSlots[j] = Array.IndexOf(s.Entities.All, e);
                Log.Add($"escort {j}: model {model:X2} type {type} slot {escortSlots[j]} at {x},{y},{z} (ground {v.W(0x74)}), +25 = {e.B[0x25]:X2}");
            }
            if (escortSlots[j] < 0) continue;
            if (!s.Entities.All[escortSlots[j]].Alive || s.Entities.All[escortSlots[j]].B[0x25] != model)
            {
                Log.Add($"escort {j}: lost after {escortPasses[j]} passes (slot {escortSlots[j]} alive {s.Entities.All[escortSlots[j]].Alive}, +25 {s.Entities.All[escortSlots[j]].B[0x25]:X2}, +04 {s.Entities.All[escortSlots[j]].W(4):X2})");
                escortSlots[j] = -1;
                continue;
            }
            var h = s.Entities.All[escortSlots[j]];
            escortPasses[j]++;
            h.SetW(0x50, h.W(0x4A)); h.SetW(0x52, h.W(0x4C)); h.SetW(0x54, h.W(0x4E));
            h.SetW(0x4A, x); h.SetW(0x4C, y); h.SetW(0x4E, z);
            h.SetW(0x74, s.Ground.Height((ushort)x, (ushort)z));
            h.SetW(0x76, 0);                                                 // no fall
            // under way as its vehicle is, and never 0: a machine with flag 27 and no speed holds its parts still ($56D0) - the
            // rotor turns while it flies (the rules do not move an escort, Holds, so the speed only drives the rotor)
            h.SetW(0x3E, Math.Max(1, (int)v.W(0x3E)));
            h.HeadingFine = v.HeadingFine;
            h.B[0x7E] = v.B[0x7E];
        }
    }

    /// <summary>The point that far back along his way from where he is now, and a point a little ahead of it (its heading).</summary>
    ((double X, double Z) At, (double X, double Z) Ahead) Along((double X, double Z) from, double back)
    {
        var cur = from;
        var left = back;
        for (var i = way.Count - 1; i >= 0; i--)
        {
            var p = way[i];
            var len = Dist(cur, p);
            if (len >= left && len > 0)
            {
                var t = left / len;
                var at = (cur.X + (p.X - cur.X) * t, cur.Z + (p.Z - cur.Z) * t);
                return (at, cur);
            }
            left -= len;
            cur = p;
        }
        return (cur, way.Count > 1 ? way[1] : from);
    }

    static double Dist((double X, double Z) a, (double X, double Z) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>The first type whose kind record draws this model (+5 of the record: the live record's +$25), or -1.</summary>
    public static int TypeOf(GameData d, int model)
    {
        for (var t = 0; t < ExtraKinds.End && (t + 1) * ExtraKinds.Size <= d.Kinds.Length; t++)
            if (d.Kinds[t * ExtraKinds.Size + 5] == model) return t;
        return -1;
    }

    /// <summary>A grid kind word for the type (class byte low, the offset into the class high: type = high + TypeBase(low)).</summary>
    static int KindOf(GameData d, int type)
    {
        for (var cls = 0; cls < 32; cls++)
        {
            var off = type - ExtraKinds.TypeBase(d.Main, cls);
            if (off is >= 0 and < 256) return off << 8 | cls;
        }
        return type << 8;
    }
}
