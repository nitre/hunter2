namespace Hunter.Rules;

/// <summary>
/// ENHANCEMENT, NOT THE ORIGINAL (LIMB DAMAGE, owner 2026-11-03, WorldState.LimbDamage, off by default; jobs/limb_damage).
/// A bullet ($52, tracer $5F) hitting a person ($1E-$2F) picks the part hit by where it is on the victim's box, which the
/// original's test gives (Collisions.Overlap: y from +$4C to +$4C + 2 * +$70; the shot's own +$4C is its height, clamped):
///   height 0 - 0.45 legs, 0.45 - 0.8 body, above that the head;
///   a leg: the side the shot is on (its sideways offset from the victim's middle, across the victim's heading +$7E);
///   an arm: a body hit at least half way out to the side (offset >= half of +$6E) - the people's part layout is not read,
///   so this is a chosen width, not the model's.
/// A leg or arm hit sets that limb's bit in Entity.Limbs and the hit does not kill (at least 1 energy stays); head and
/// body hits are the original's damage. Left / right: heading 0 = +z (south), $40 = +x (east); right is the victim's right
/// seen from above with z down. Nothing here reads or writes a record byte.
/// </summary>
public static class Limbs
{
    public const byte LegL = 1, LegR = 2, ArmL = 4, ArmR = 8;
    public enum Band { Legs, Body, Head }

    public const double LegsTop = 0.45, BodyTop = 0.8, ArmOut = 0.5;

    /// <summary>One hurt arm: the reload (+$2F, the fire timer's start value) takes this many times as long - a hurt arm
    /// works the gun slowly. Both arms hurt: no shot (Movement.FireTimer). Chosen, not the original's.</summary>
    public const int OneArmReload = 2;

    /// <summary>How many of a person's arms are hurt (0, 1 or 2); 0 for anything that is not a person.</summary>
    public static int HurtArms(Entity e) =>
        !Person(e) ? 0 : ((e.Limbs & ArmL) != 0 ? 1 : 0) + ((e.Limbs & ArmR) != 0 ? 1 : 0);

    /// <summary>The fire timer start for one hurt arm: the reload times OneArmReload, kept under 128 (the timer is a signed
    /// byte; 128 and up would read as loaded at once).</summary>
    public static byte Reload(byte reload) => (byte)Math.Min(reload * OneArmReload, 0x7F);

    /// <summary>Each hurt leg leaves this share of a person's step (one leg 3/4, both 9/16). Chosen, not the original's.</summary>
    public const int LegPaceNum = 3, LegPaceDen = 4;

    /// <summary>A person's step for this pass with its hurt legs (Movement.MoveAndSettleOnGround $2AE6); anything else as it was.</summary>
    public static short Pace(Entity e, short dist)
    {
        if (!Person(e)) return dist;
        var legs = ((e.Limbs & LegL) != 0 ? 1 : 0) + ((e.Limbs & LegR) != 0 ? 1 : 0);
        if (legs == 0) return dist;
        int num = legs == 1 ? LegPaceNum : LegPaceNum * LegPaceNum, den = legs == 1 ? LegPaceDen : LegPaceDen * LegPaceDen;
        var total = dist * num + e.PaceCarry;                               // whole units a pass: the rest is walked later, not lost
        e.PaceCarry = total % den;
        return (short)(total / den);
    }

    /// <summary>A bullet (the shot types $52 bullet and $5F tracer).</summary>
    public static bool Bullet(Entity e) => e.W(4) is 0x52 or 0x5F;

    /// <summary>A person: the soldier types $1E-$2F (the player is $1E).</summary>
    public static bool Person(Entity e) => e.Alive && e.W(4) is >= 0x1E and <= 0x2F;

    /// <summary>Where a shot's position (+$4C, the bullet's own height; its box above it is a big tolerance, half 248 against
    /// a soldier's 34) is on the victim: the height as a fraction of the victim's box (0 = feet, 1 = top; outside it, which
    /// the box test allows, clamped), the band, and the sideways offset in +$6E units (+ = right).</summary>
    public static (double Fraction, Band Band, double Side) Classify(Entity shot, Entity victim)
    {
        int cs = shot.W(0x4C), cv = (short)(victim.W(0x4C) + victim.W(0x70));
        int h = Math.Max((short)1, victim.W(0x70));
        var frac = Math.Clamp(0.5 + (double)(cs - cv) / (2 * h), 0, 1);
        var band = frac < LegsTop ? Band.Legs : frac < BodyTop ? Band.Body : Band.Head;
        var a = victim.B[0x7E] * 2 * Math.PI / 256;
        int dx = (short)(shot.W(0x4A) - victim.W(0x4A)), dz = (short)(shot.W(0x4E) - victim.W(0x4E));
        var lateral = -dx * Math.Cos(a) + dz * Math.Sin(a);                 // along the victim's right (-cos, sin)
        return (frac, band, lateral / Math.Max((short)1, victim.W(0x6E)));
    }

    /// <summary>The limb bit a bullet hitting this person hurts, 0 for a body or head hit (or not a bullet / person).</summary>
    public static byte Hit(Entity shot, Entity victim)
    {
        if (!Bullet(shot) || !Person(victim)) return 0;
        var (_, band, side) = Classify(shot, victim);
        return band switch
        {
            Band.Legs => side < 0 ? LegL : LegR,
            Band.Body when Math.Abs(side) >= ArmOut => side < 0 ? ArmL : ArmR,
            _ => 0,
        };
    }

    /// <summary>After the original's Damage: the limb is hurt, and the victim keeps at least 1 energy (when the hit did cost it).</summary>
    public static void Keep(Entity victim, byte limb, short energyBefore)
    {
        if (victim.W(0x2A) == energyBefore) return;                         // no damage was done ($455C refused)
        victim.Limbs |= limb;
        if (energyBefore >= 1 && victim.W(0x2A) < 1) victim.SetW(0x2A, 1);
    }
}
