namespace Hunter.Rules;

/// <summary>68000 arithmetic the ported routines depend on, bit for bit.</summary>
public static class M68
{
    /// <summary>Tests only (FinerGroundLimitTests): how many divs.w overflowed (the quotient past 16 bits, the
    /// dividend kept) on this thread. Counting changes nothing the game sees.</summary>
    [ThreadStatic] public static int DivsOverflows;

    /// <summary>divs.w: 32/16 signed divide. Result register = remainder:quotient; on quotient
    /// overflow the 68000 leaves the dividend untouched (V set), which some routines rely on.</summary>
    public static int Divs(int dividend, int divisor)
    {
        var d = (short)divisor;
        if (d == 0) throw new DivideByZeroException("divs.w by zero");
        var q = (long)dividend / d;
        if (q < short.MinValue || q > short.MaxValue) { DivsOverflows++; return dividend; }
        var r = (long)dividend % d;
        return (int)(((r & 0xFFFF) << 16) | (q & 0xFFFF));
    }

    /// <summary>muls.w: signed 16 x 16 -> 32.</summary>
    public static int Muls(int a, int b) => (short)a * (short)b;

    /// <summary>lsr.l by a register count (0-63): counts of 32 or more give 0.</summary>
    public static uint Lsr(uint v, int n) => n >= 32 ? 0 : v >> n;

    /// <summary>asr.l by a register count (taken mod 64): counts of 32 or more give 0 or -1.</summary>
    public static int Asr(int v, int n) => (n & 63) >= 32 ? v >> 31 : v >> (n & 63);

    /// <summary>Replace the low word of a register.</summary>
    public static int SetW(int reg, int w) => (int)((uint)reg & 0xFFFF0000) | (w & 0xFFFF);
}
