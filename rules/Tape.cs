using System.IO.Compression;

namespace Hunter.Rules;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (the BOT's TAPES, docs/bot-plan.md): a run kept as what the pilot gave the game, blank by
/// blank - the stick, the keys and clicks, and the view angle (Hunt.TurnView writes ViewAngle1 as Q / E would). The game is
/// the same film for the same input, so a tape played back rebuilds every moment of the run exactly, whatever the bot's code
/// has become since: a SITUATION is a tape and a blank, a fresh bot handed the game there (rules/tests/Situations.cs).
/// A blank is one VerticalBlank (GameStep.Pass: one blank, one frame). Checks every CheckEvery blanks (his place, energy,
/// the things alive) find a tape the game no longer matches.</summary>
public sealed class Tape
{
    public const int CheckEvery = 500;
    public int Mission { get; init; }
    public bool BulletBox { get; init; } = true;
    public bool A500Pace { get; init; } = true;
    public string Note { get; set; } = "";
    public readonly List<Blank> Blanks = [];
    public readonly List<long> Checks = [];

    /// <summary>One blank: the keys and clicks given before the stick (IPilot.Act), then the stick and the view, when the
    /// stick was asked for (in play).</summary>
    public sealed class Blank
    {
        public readonly List<Input> Inputs = [];
        public bool Stick;
        public byte D6, View;
    }
    /// <summary>A key (Kind 0: A raw, B shift | ctrl << 1, C d7) or a click (Kind 1: A x, B y).</summary>
    public readonly record struct Input(byte Kind, int A, int B, int C);

    /// <summary>What the game is at a check: his place and energy, and how many things are alive.</summary>
    public static long Check(Play p)
    {
        var s = p.State;
        var me = s.Entities.All[0];
        var alive = 0;
        for (var i = 0; i < s.Entities.Count; i++) if (s.Entities.All[i].Alive) alive++;
        return (long)me.UW(0x4A) << 40 | (long)me.UW(0x4E) << 24 | (long)(s.Energy & 0xFFF) << 12 | (long)(alive & 0xFFF);
    }

    /// <summary>A new game for this tape: the mission set as MissionProbe sets it.</summary>
    public Play NewGame(GameData data)
    {
        var p = new Play(data, GameMode.Missions);
        var s = p.State;
        s.Mission = 0x10 * Mission;
        s.Log.NoteObjectives(s, 0x9072 + s.Mission); s.Flags0A4 = 0;
        p.BulletBox = BulletBox; p.A500Pace = A500Pace;
        return p;
    }

    public void Save(string path)
    {
        using var f = File.Create(path);
        using var z = new GZipStream(f, CompressionLevel.SmallestSize);
        using var w = new BinaryWriter(z);
        w.Write("HTAPE1"); w.Write(Mission); w.Write(BulletBox); w.Write(A500Pace); w.Write(Note);
        w.Write(Blanks.Count);
        foreach (var b in Blanks)
        {
            w.Write((byte)((b.Stick ? 1 : 0) | (b.Inputs.Count > 0 ? 2 : 0)));
            if (b.Stick) { w.Write(b.D6); w.Write(b.View); }
            if (b.Inputs.Count > 0)
            {
                w.Write((byte)b.Inputs.Count);
                foreach (var i in b.Inputs) { w.Write(i.Kind); w.Write(i.A); w.Write(i.B); w.Write(i.C); }
            }
        }
        w.Write(Checks.Count);
        foreach (var c in Checks) w.Write(c);
    }

    public static Tape Load(string path)
    {
        using var f = File.OpenRead(path);
        using var z = new GZipStream(f, CompressionMode.Decompress);
        using var r = new BinaryReader(z);
        if (r.ReadString() != "HTAPE1") throw new InvalidDataException($"{path}: not a tape");
        var t = new Tape { Mission = r.ReadInt32(), BulletBox = r.ReadBoolean(), A500Pace = r.ReadBoolean() };
        t.Note = r.ReadString();
        var n = r.ReadInt32();
        for (var k = 0; k < n; k++)
        {
            var b = new Blank();
            var fl = r.ReadByte();
            if ((fl & 1) != 0) { b.Stick = true; b.D6 = r.ReadByte(); b.View = r.ReadByte(); }
            if ((fl & 2) != 0)
                for (int m = r.ReadByte(), j = 0; j < m; j++) b.Inputs.Add(new(r.ReadByte(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
            t.Blanks.Add(b);
        }
        for (int m = r.ReadInt32(), j = 0; j < m; j++) t.Checks.Add(r.ReadInt64());
        return t;
    }
}

/// <summary>A pilot taped: everything it gives the game kept on the tape as it gives it (Play.Taping hears the keys and clicks).
/// Call Blank() after each GameStep.Pass, to close the blank and take the checks.</summary>
public sealed class TapeRecorder(IPilot inner, Tape tape) : IPilot
{
    Tape.Blank now = new();
    public Tape Tape => tape;

    public void Act(Play play) { play.Taping = this; inner.Act(play); }
    public Joystick Next(Play play)
    {
        var j = inner.Next(play);
        now.Stick = true; now.D6 = j.D6; now.View = play.State.ViewAngle1;   // D6 holds fire (bit 4)
        return j;
    }
    internal void Key(byte raw, bool shift, bool ctrl, int d7) => now.Inputs.Add(new(0, raw, (shift ? 1 : 0) | (ctrl ? 2 : 0), d7));
    internal void Click(int x, int y) => now.Inputs.Add(new(1, x, y, 0));

    /// <summary>The blank is over (after the pass's frame): kept, and a check every Tape.CheckEvery.</summary>
    public void Blank(Play play)
    {
        tape.Blanks.Add(now); now = new();
        if (tape.Blanks.Count % Tape.CheckEvery == 0) tape.Checks.Add(Tape.Check(play));
    }
}

/// <summary>A tape played back to blank `handAt`, then `next` plays on (a fresh brain: until then it only watches - Hunt.Watch,
/// what a player sees and the drone's maps - so it knows what he could know). Call Blank() after each GameStep.Pass: it checks
/// the game against the tape's checks and throws where they part (the game's rules changed since it was made).</summary>
public sealed class TapePlayer(Tape tape, int handAt, Hunt? next) : IPilot
{
    int at;
    readonly Bot? bot = next is null ? null : new Bot(next);
    public int At => at;
    public bool Handed => at >= handAt;

    public void Act(Play play)
    {
        if (Handed) { bot?.Act(play); return; }
        if (at >= tape.Blanks.Count) return;
        foreach (var i in tape.Blanks[at].Inputs)
            if (i.Kind == 0) play.KeyDown((byte)i.A, (i.B & 1) != 0, (i.B & 2) != 0, i.C);
            else play.Click(i.A, i.B);
        if (play.Mode == PlayMode.Screen) next?.Watch(play);
    }

    public Joystick Next(Play play)
    {
        if (Handed) return bot?.Next(play) ?? default;
        if (at >= tape.Blanks.Count) return default;
        var b = tape.Blanks[at];
        play.State.ViewAngle1 = b.View;
        next?.Watch(play);
        return Joystick.FromD6(b.D6);
    }

    public void Blank(Play play)
    {
        at++;
        if (at <= handAt && at % Tape.CheckEvery == 0 && at / Tape.CheckEvery - 1 is var k && k < tape.Checks.Count && Tape.Check(play) != tape.Checks[k])
            throw new InvalidOperationException($"the game no longer plays this tape (M{tape.Mission}): it parts by blank {at} - his place / energy / things alive differ");
    }
}
