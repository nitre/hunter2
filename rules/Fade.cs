namespace Hunter.Rules;

/// <summary>The game's one fade, ported whole from $68AC-$6AC0 (findings\fade_code.md maps every
/// routine). The state is the original's globals: the target word $18E (its low byte is $18F, the
/// daylight level), level $190, wait count/reload $194/$195 (bytes: a word store of d1 puts 0 in the
/// count and d1 in the reload, so the first step comes at once and then every (d1+1)th blank), steps
/// left/total $196/$198, delta $19A, the delayed-start counter $19C, and the gate bit 5 of $95 that
/// FadeStep runs under from the vertical blank ($C722). Blank() is one blank: FadeStep while active,
/// then the pass-end counter step ($6234-$6250) that raises the gate 2 blanks after
/// FadeToLight/StartFadeTo. The colour maths is the original's: level k of a bank shows (k*n)&amp;15
/// per channel (StepRGB $6A36); the remake's drawing reads FadeLevel (Play.Faded) instead of the
/// colour registers, so WriteFadedColours ($6A4A) needs no port. The original's blocking WaitFade
/// spins ($6952) become the caller watching FadeActive: the blanks it spins through are real blanks
/// there, and this port's callers stage them the same way.</summary>
public sealed class Fade
{
    // the fade's colour memory: accumulators $652C, target nibbles $65EC (read at +$C0 by $6AF0)
    readonly byte[] acc = new byte[16 * 3];
    readonly byte[] delta = new byte[16 * 3];

    // the original's globals (re\vars.txt)
    public int FadeTarget;         // word $18E; its low byte is $18F, the daylight level
    public int FadeLevel;          // $190
    int waitCount, waitReload;     // bytes $194 (countdown) and $195 (reload)
    int stepsLeft, stepsTotal;     // $196, $198
    int fadeDelta;                 // word $19A, the signed step
    public int FadeCounter;        // $19C: FadeToLight/StartFadeTo start 2 blanks later ($6250)
    public bool FadeActive;        // $95 bit 5: FadeStep runs while it is set ($C722)
    bool bounce;                   // $95 bit 4: reverse and run the steps back when they run out
    public bool FlareOn;           // $A2 bit 0: the flare's own flag, read by FadeDirection ($68E6)
    public int FlareLight;         // $184: the flare's light mask (WriteFadedColours reads it)
    (int Wait, int Target)? pendingStart;    // FadeToAndWait's StartFadeTo, run once the flare's fade ended

    public int Level => FadeLevel;
    public bool Busy => FadeActive;

    /// <summary>One vertical blank for ManualPage's own two instances: FadeStep only ($C722).</summary>
    public void Tick() { if (FadeActive) FadeStep(); }

    /// <summary>Current COLOR00-15 as $0RGB (PackRGB $6AAC: the high nibble of each channel byte).</summary>
    public ushort Colour(int i) => (ushort)(((acc[3 * i] >> 4) << 8) | ((acc[3 * i + 1] >> 4) << 4) | (acc[3 * i + 2] >> 4));

    /// <summary>$6BBC SetBankNoAndLoad with d5 = 0 (SetPaletteBank $6BC0 stores word 15 at $18E):
    /// load the bank's colours, start black, target 15.</summary>
    public void SetBank(ushort[] bank)
    {
        for (var i = 0; i < 16; i++)
        {
            delta[3 * i] = (byte)((bank[i] >> 8) & 15);
            delta[3 * i + 1] = (byte)((bank[i] >> 4) & 15);
            delta[3 * i + 2] = (byte)(bank[i] & 15);
        }
        Array.Clear(acc);
        FadeLevel = 0;
        stepsLeft = 0;
        FadeTarget = 15;
    }

    /// <summary>$698E: steps left = total = D0, word $194 = D1 (count 0, reload D1), $19A = D2.</summary>
    void SetFadeParams(int steps, int wait, int step)
    {
        stepsLeft = stepsTotal = steps;
        waitCount = (wait >> 8) & 0xFF;
        waitReload = wait & 0xFF;
        fadeDelta = step;
    }

    /// <summary>CalcFadeDistAndSign ($68F0): the steps from FadeLevel to the target and their sign; d3 = 2
    /// (the delayed start) when a fade is needed, 0 when none.</summary>
    (int Steps, int Step, int Delay) Distance(int target)
    {
        var steps = target - FadeLevel;
        if (steps == 0) return (0, 1, 0);
        return steps < 0 ? (-steps, -1, 2) : (steps, 1, 2);
    }

    /// <summary>StartFadeTo / FadeToLight's tail, FadeCounterThenParams ($698A): the word $19C = d3, then
    /// SetFadeParams. The gate bit is NOT set: the counter starts the fade, 2 blanks later.</summary>
    void StartDelayed(int target, int wait)
    {
        var (steps, step, delay) = Distance(target);
        FadeCounter = delay;
        SetFadeParams(steps, wait, step);
    }

    /// <summary>StartFadeToLevel ($6936, FadeIn / FadeToBlack / FadeInBank): FadeDirection ($68E2) with the word
    /// $18E the target for the moment - so 15 while the flare burns ($68E4 btst #0,$A2), whatever was asked -
    /// then StartFadeThenWait ($6950 SetFadeActive): the fade starts now. The counter $19C is not touched (no
    /// $698A on this way).</summary>
    void StartFadeToLevel(int target, int wait)
    {
        var (steps, step, _) = Distance(FlareOn ? 15 : target);
        SetFadeActive(steps, wait, step);
    }

    /// <summary>$68D0 StartFadeTo (speed D0, target D1): the flare's light mask off, then the delayed
    /// start toward the target (straight to $68F0: the flare is not looked at). The gate bit is raised
    /// by the counter, 2 blanks later.</summary>
    public void StartFadeTo(int wait, int target)
    {
        FlareLight = 0;
        StartDelayed(target, wait);
    }

    /// <summary>$68DC FadeToLight (speed D0): toward 15 while a flare burns, else toward the word
    /// $18E (the daylight byte). Not waited. PanelClock calls it ($C47C) only from play's restart
    /// ($255A), the flare's fire ($A708) and the hour's change ($C430) - not every pass.</summary>
    public void FadeToLight(int wait)
    {
        StartDelayed(FlareOn ? 15 : FadeTarget, wait);
    }

    /// <summary>$6918 FadeIn (D1 = 15, D0 = 0): start now and the caller waits on FadeActive.</summary>
    public void FadeIn(int wait = 0) => StartFadeToLevel(15, wait);

    /// <summary>$691E FadeToBlack (D1 = 0, D0 = 0). While a flare burns the target is 15 ($68E2): no fade.</summary>
    public void FadeToBlack(int wait = 0) => StartFadeToLevel(0, wait);

    /// <summary>$6948 FadeOut (speed D0): from FadeLevel down, one level per step.</summary>
    public void FadeOut(int wait) => SetFadeActive(FadeLevel, wait, -1);

    /// <summary>$6924 FadeInBank (bank D0, speed D1): load the bank black ($6BBC/$6BC0: word $18E = 15)
    /// and fade it in at the speed.</summary>
    public void FadeInBank(ushort[] bank, int wait)
    {
        SetBank(bank);
        FadeIn(wait);
    }

    /// <summary>The old surface kept for ManualPage: FadeIn/FadeToBlack generalised - start now.</summary>
    public void FadeTo(int level, int wait = 0) => StartFadeToLevel(level, wait);

    /// <summary>$695C RunFadeAndWait: gate on at once, counter off (WaitFade itself is the caller's).</summary>
    public void RunFadeAndWait() { FadeActive = true; FadeCounter = 0; }

    /// <summary>$696E SetFadeActive (d0 steps, d1 wait, d2 step): steps != 0 -> SetFadeParams and the gate on;
    /// steps == 0 -> StopFade ($697C: bits 5 and 4 off, the params left as they were).</summary>
    void SetFadeActive(int steps, int wait, int step)
    {
        if (steps != 0) { SetFadeParams(steps, wait, step); FadeActive = true; }
        else { FadeActive = false; bounce = false; }
    }

    /// <summary>$68AC FadeToAndWait (speed D0, target D1). With a flare burning: the flare flag off,
    /// the running fade ended with 0 steps left (so its own next blank finishes it - and applies one
    /// more step of ITS delta), the gate on, and the real fade started the moment that ends, as the
    /// original's WaitFade spin does between the blanks. Without a flare: StartFadeTo, gate on at once.</summary>
    public void FadeToAndWait(int wait, int target)
    {
        if (!FlareOn)
        {
            StartFadeTo(wait, target);
            RunFadeAndWait();
            return;
        }
        FlareOn = false;                    // bclr $A2 bit 0 ($68B0)
        var step = Math.Sign(FadeTarget - FadeLevel);
        SetFadeParams(0, wait, step < 0 ? -1 : 1);   // $68B4-$68C0: FadeDirection, 0 steps
        RunFadeAndWait();
        pendingStart = (wait, target);      // StartFadeTo once the wait is over
    }

    /// <summary>$69B2 FadeStep, from the vertical blank ($C722) while the gate bit is set.</summary>
    public void FadeStep()
    {
        if (--waitCount >= 0) return;               // subq.b + bpl: the byte countdown
        waitCount = waitReload;                     // $69BA
        var d5 = fadeDelta;
        if (--stepsLeft <= 0)                       // subq.w + bgt to the apply
        {
            var wasBounce = bounce;
            bounce = false;                         // bclr #4
            if (!wasBounce) FadeActive = false;     // FinishFade $69DE: bclr #5
            else { fadeDelta = -fadeDelta; stepsLeft = stepsTotal; }    // $69D2-$69D6
        }
        StepFadeColours(d5);                        // $6A14 + WriteFadedColours $6A4A
    }

    /// <summary>$6A14 StepFadeColours: the level moves by the step, each channel byte by ± its target
    /// nibble (the 16 base colours; the copper's extras beyond $192 are the remake's copper).</summary>
    void StepFadeColours(int d5)
    {
        FadeLevel += d5;
        var down = d5 < 0;
        for (var i = 0; i < acc.Length; i++)
            acc[i] = (byte)(acc[i] + (down ? -delta[i] : delta[i]));    // add.b wraps mod 256
    }

    /// <summary>$69E8 JumpFadeLevel (signed steps): all at once, the next level clamped to 1..15
    /// ($6A04 ble / $6A0A bgt). The original's leading WaitFade spin is the caller's here - no caller
    /// reaches it with a fade still running.</summary>
    public void JumpFadeLevel(int steps)
    {
        if (steps == 0) return;
        var step = steps < 0 ? -1 : 1;
        var n = Math.Abs(steps) - 1;
        for (var k = 0; k <= n; k++)                // dbf: |steps| rounds
        {
            var next = FadeLevel + step;
            if (next <= 0 || next > 15) break;
            StepFadeColours(step);
        }
    }

    /// <summary>This port's compression of a *waited* fade at a screen change ($479C FadeOut after a
    /// building, the play-start pair $251A/$2526): the original spins WaitFade while its blanks run,
    /// this port's callers run between the shell's blanks, so the level is put where the fade would
    /// have ended and the fade flags are cleared.</summary>
    public void SnapTo(int level)
    {
        FadeLevel = level;
        stepsLeft = 0;
        FadeActive = false;
        bounce = false;
        FadeCounter = 0;
        pendingStart = null;
    }

    /// <summary>One vertical blank, both halves: $C722's FadeStep while the gate bit is set, then the
    /// pass-end counter step ($6234-$6250) that raises the gate when a delayed start counts out.</summary>
    public void Blank()
    {
        if (FadeActive) FadeStep();
        if (FadeCounter != 0 && --FadeCounter == 0) FadeActive = true;    // $6250 bset
        if (pendingStart is { } p && !FadeActive)
        {
            pendingStart = null;
            StartFadeTo(p.Wait, p.Target);
            RunFadeAndWait();
        }
    }
}
