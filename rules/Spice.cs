namespace Hunter.Rules;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (picked by the harvester only; the spice just counted for now): the sand
/// face's harvest. Planting is the face's own (Plant, from its seed, at Planet.Build): fields of spice flowers
/// (ExtraKinds.SpiceFlower) on open sand, never on rock, and one empty harvester (ExtraKinds.Harvester) parked on the
/// sand nearest the edge the played world joins. Driving the harvester into a spice flower takes it (Touch): the flower
/// is gone, WorldState.Spice goes up one, the line says so. Nothing else picks them - on foot, in any other vehicle, a
/// shot: it goes through them as through any flower. Selling it, the HQ and the rest of Dune II's economy are later
/// (docs/extras.md, DUNE II EXTRAS).</summary>
public static class Spice
{
    /// <summary>Fields a face, flowers a field (first picks, to tune), and how far a field's flowers lie from its middle.</summary>
    public const int Fields = 10, PerField = 14, Spread = 1400;

    /// <summary>The sand face's harvest records: the spice fields and the harvester (16-byte map records, as Fill's).</summary>
    public static List<byte[]> Plant(SandZone zone, Ground ground, int seed, int edgeToWorld)
    {
        var recs = new List<byte[]>();
        var rng = new Random(seed ^ 0x5B1CE);
        var placed = new List<(int X, int Z)>();
        bool Free(int x, int z, int gap) => placed.All(p => Math.Max(Math.Abs(p.X - x), Math.Abs(p.Z - z)) >= gap);
        bool Open(int x, int z) => (uint)x < Faces.Width && (uint)z < Faces.Width && zone.IsSand(x, z)
                                   && zone.IsSand(x + 512, z) && zone.IsSand(x - 512, z) && zone.IsSand(x, z + 512) && zone.IsSand(x, z - 512);

        // the harvester first: on open sand, the nearest such point to the middle of the edge the played world joins
        var (hx, hz) = NearEdge(edgeToWorld, Open);
        if (hx >= 0)
        {
            recs.Add(Record(ExtraKinds.Harvester, hx, ground.Height((ushort)hx, (ushort)hz), hz, energy: 40, fuel: 0x0C, heading: 0));
            placed.Add((hx, hz));
        }
        for (int f = 0, tries = 0; f < Fields && tries < 4000; tries++)
        {
            int cx = rng.Next(4096, Faces.Width - 4096), cz = rng.Next(4096, Faces.Width - 4096);
            if (!Open(cx, cz) || !Free(cx, cz, 6 * 512)) continue;
            f++;
            for (int k = 0, misses = 0; k < PerField && misses < 60;)
            {
                int x = cx + rng.Next(-Spread, Spread + 1), z = cz + rng.Next(-Spread, Spread + 1);
                if (!zone.IsSand(x, z) || !Free(x, z, 300)) { misses++; continue; }
                recs.Add(Record(ExtraKinds.SpiceFlower, x, ground.Height((ushort)x, (ushort)z), z, energy: 2, fuel: 0, heading: 0));
                placed.Add((x, z));
                k++;
            }
        }
        return recs;
    }

    /// <summary>The open sand point nearest the middle of an edge (0 west x = 0, 1 east x = max, 2 north z = 0, 3 south
    /// z = max), searched inward in rings; (-1, -1) none.</summary>
    static (int X, int Z) NearEdge(int edge, Func<int, int, bool> open)
    {
        const int step = 512, mid = Faces.Width / 2;
        for (var depth = 2 * step; depth < Faces.Width / 2; depth += step)
            for (var side = 0; side < Faces.Width / 2; side += step)
                foreach (var along in new[] { mid + side, mid - side })
                {
                    var (x, z) = edge switch
                    {
                        0 => (depth, along), 1 => (Faces.Width - depth, along),
                        2 => (along, depth), _ => (along, Faces.Width - depth),
                    };
                    if (open(x, z)) return (x + step / 2, z + step / 2);              // the cell's middle
                }
        return (-1, -1);
    }

    /// <summary>The edge of the sand face the played world joins (NearEdge's numbering).</summary>
    public static int EdgeTo(int face, int other)
    {
        const int mid = Faces.Width / 2;
        var tries = new (int X, int Z)[] { (-1, mid), (Faces.Width, mid), (mid, -1), (mid, Faces.Width) };
        for (var e = 0; e < 4; e++) if (Faces.Cross(face, tries[e].X, tries[e].Z).Face == other) return e;
        return 0;
    }

    /// <summary>A map record of an extra type (16 bytes, as Fill.Add writes one; +$0D the fuel's high byte).</summary>
    public static byte[] Record(int type, int x, int y, int z, int energy, int fuel, int heading)
    {
        var rec = new byte[16];
        (rec[0], rec[1]) = ExtraKinds.RecordKind(type);
        rec[4] = (byte)(energy >> 8); rec[5] = (byte)energy;
        rec[6] = (byte)(x >> 8); rec[7] = (byte)x;
        rec[8] = (byte)(y >> 8); rec[9] = (byte)y;
        rec[10] = (byte)(z >> 8); rec[11] = (byte)z;
        rec[12] = (byte)heading;
        rec[13] = (byte)fuel;
        return rec;
    }

    public static bool IsFlower(Entity e) => e.Alive && e.W(4) == ExtraKinds.SpiceFlower;

    /// <summary>A touch with a spice flower (Collisions.Respond, before anything else it does). A harvester takes it -
    /// the flower's record freed, no wreck; Spice + 1 and the line says so. Anything else: nothing, it goes through.
    /// True when the pair was a spice flower's (the touch is handled).</summary>
    public static bool Touch(WorldState s, Entity a, Entity b)
    {
        var (flower, other) = IsFlower(a) ? (a, b) : IsFlower(b) ? (b, a) : (null, null);
        if (flower is null) return false;
        if (other!.W(4) != ExtraKinds.Harvester) return true;                // through it, as through any flower
        flower.B[0] = 0;
        s.Spice++;
        s.Panel.Text.Print(System.Text.Encoding.ASCII.GetBytes($"]You have {s.Spice} spice*"));   // a new line, as the game's own ("]" is table 0's first)
        return true;
    }
}
