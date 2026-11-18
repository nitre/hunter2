namespace Hunter.Rules;

/// <summary>
/// a smooth picture for the demo. The original's demo moves the game
/// on only when its pace table says (a pass every 2-14 blanks), and the recording only replays right
/// at that pace (a pass every blank sends the soldier elsewhere within 1000 blanks), so the game
/// keeps the original's passes exactly. Only the picture is new: on every blank the view is drawn
/// with each object and the camera part way between where the last two passes left them, reaching
/// the newer one as the next pass runs - one pass behind, the way smoothed replays usually are. The
/// drawing touches no game state: the records and the camera are put back after each picture.
/// </summary>
public sealed partial class Play
{
    /// <summary>Draw the in-between pictures (GameView turns it on for the demo when the setting is on).</summary>
    public bool Smooth;

    // Pitch +$7C slides too (a flier's climb, $2804's steps; on the ground Place's $42A6 works it out again from the
    // in-between place). Roll +$80 is set at the spawn only and never moves.
    readonly record struct Pose(bool Alive, short Type, ushort X, short Y, ushort Z, byte Heading, byte Pitch);

    Pose[]? _posesBefore, _posesNow;
    bool _insideBefore;
    int _distanceBefore, _distanceNow, _passAt, _passBlanks = 1;
    readonly byte[][] _keep = Enumerable.Range(0, EntityTable.MaxCount).Select(_ => new byte[EntityTable.Size]).ToArray();
    readonly (ushort, ushort)[] _keepFine = new (ushort, ushort)[EntityTable.MaxCount];   // FINE ANGLES: put back with the bytes

    /// <summary>A move bigger than this between two passes is a jump (into a vehicle, a building, a
    /// respawn): the picture shows it at once instead of sliding across.</summary>
    const int JumpLimit = 0x800;

    Pose[] Poses() => State.Entities.All.Select(e =>
        new Pose(e.Alive, e.W(4), e.UW(0x4A), e.W(0x4C), e.UW(0x4E), e.B[0x7E], e.B[0x7C])).ToArray();

    // After a pass has drawn: its poses become the target; the picture slides there until the next
    // pass, which in the demo comes at the next pace word.
    void NoteSmoothPass()
    {
        _posesBefore = _insideBefore == State.InsideView ? _posesNow : null;   // in or out of a building: no slide across
        _insideBefore = State.InsideView;
        _distanceBefore = _distanceNow;
        _posesNow = Poses();
        _distanceNow = State.Distance;
        _passAt = State.BlankCount;
        var next = State.DemoMode ? (short)Demo.PaceWord(State.DemoBuffer, Math.Max((short)0, State.DemoPaceCursor)) : _passAt + 1;
        _passBlanks = Math.Max(1, next - _passAt);
    }

    /// <summary>SmoothPicture draws this blank: its guards, which the card's in-between records (WithInBetweenPoses)
    /// and GameView's HUNTER_CHECK_FEED go by too.</summary>
    public bool SmoothWillDraw => Smooth && _posesBefore is not null && _posesNow is not null
        && Mode == PlayMode.Play && !Quit && !(Waiting && !Demo.PaceHeld(State))
        && !Inside;                   // outdoors only. Indoors with the card (bf80363) kept the attract's man out of the
                                      // first hut - he lost the recording's way there (bisected 11-08); back as before it

    // in a building: the camera stays where entering placed it ($47D6) and the room is no ground to stand things on
    bool Inside => _interior || State.InsideView;

    /// <summary>The in-between picture for this blank into View (outdoors; indoors the card draws its own; otherwise the
    /// pass's own picture stays).</summary>
    public void SmoothPicture()
    {
        if (!SmoothWillDraw) return;
        var s = State;
        var t = Between();
        var camera = s.Camera;
        s.Camera = InBetweenCamera(t);
        if (!PaintView)
        {
            // split-draw (Play.PaintView): the card-built world draws this picture itself, reading
            // the records through WithInBetweenPoses - the in-between camera is all it needs from
            // here. No posing, no painting, nothing to put back.
            (_pictureCamera, _pictureAt) = (s.Camera, s.BlankCount);
            s.Camera = camera;
            return;
        }
        PoseInBetween(t);
        _objects.PlaceForPicture(s.Entities, s.Camera);
        View.Clear();
        s.Ground.Draw(View, _raster, s.Camera, row => _objects.DrawRow(View, s.Camera, s.Entities, row));
        if (s.MapMarks) s.Log.DrawMarks(View, s.Entities.All[0], s.Graves ? s.GraveStore : null);   // as Play.Draw: the graves too
        (_pictureCamera, _pictureAt) = (s.Camera, s.BlankCount);
        PutBack();
        s.Camera = camera;
    }

    /// <summary>How far into this pass's blanks the in-between picture is (0 at the pass, 1 at the
    /// next - the same fraction for the poses and the camera).</summary>
    double Between() => Math.Clamp((State.BlankCount - _passAt + SubBlank) / _passBlanks, 0, 1);

    /// <summary>Recording only (GameView's HUNTER_SLOWMO): how far past the last blank this picture is (0..1), so the
    /// pictures drawn between two blanks slide on in-between. Nothing but the pictures reads it.</summary>
    public double SubBlank;

    /// <summary>Record i part way (t) between the last two passes' poses, from the kept poses alone; null when
    /// it does not slide (dead, changed type, or a jump) and the pass's own place shows.</summary>
    (ushort X, short Y, ushort Z, byte Heading, byte Pitch)? InBetween(int i, double t)
    {
        var (a, b) = (_posesBefore![i], _posesNow![i]);
        if (!State.Entities.All[i].Alive || !a.Alive || a.Type != b.Type) return null;
        int dx = (short)(b.X - a.X), dy = b.Y - a.Y, dz = (short)(b.Z - a.Z);
        if (Math.Abs(dx) > JumpLimit || Math.Abs(dz) > JumpLimit || Math.Abs(dy) > JumpLimit) return null;
        var dh = (sbyte)(b.Heading - a.Heading);
        var dp = (sbyte)(b.Pitch - a.Pitch);
        return ((ushort)(a.X + (int)Math.Round(dx * t)), (short)(a.Y + (int)Math.Round(dy * t)),
            (ushort)(a.Z + (int)Math.Round(dz * t)), (byte)(a.Heading + (int)Math.Round(dh * t)), (byte)(a.Pitch + (int)Math.Round(dp * t)));
    }

    // Keeps every record, then moves each one part way between the last two passes' poses.
    void PoseInBetween(double t)
    {
        var s = State;
        for (var i = 0; i < s.Entities.Count; i++)
        {
            var k = s.Entities.All[i];
            Array.Copy(k.B, _keep[i], EntityTable.Size);
            _keepFine[i] = (k.PitchFine, k.HeadingFine);
        }
        for (var i = 0; i < s.Entities.Count; i++)
        {
            if (InBetween(i, t) is not { } at) continue;
            var (x, y, z, h, pitch) = at;
            var e = s.Entities.All[i];
            e.SetW(0x4A, x);
            e.SetW(0x4C, y);
            e.SetW(0x4E, z);
            e.B[0x7E] = h;
            e.B[0x7C] = pitch;
            var (a, b) = (_posesBefore![i], _posesNow![i]);                  // FINE ANGLES: the in-between angle before its rounding (PutBack restores)
            e.HeadingFine = (ushort)((a.Heading << 8) + (int)Math.Round((sbyte)(b.Heading - a.Heading) * t * 256));
            e.PitchFine = (ushort)((a.Pitch << 8) + (int)Math.Round((sbyte)(b.Pitch - a.Pitch) * t * 256));
            if (!Inside) e.SetW(0x74, s.Ground.Sample(x, z, Ground.IsShot(e)).Height);   // the ground under the halfway place, movement's own sampler ($2B0E); PutBack restores
        }
    }

    void PutBack()
    {
        for (var i = 0; i < State.Entities.Count; i++)
        {
            Array.Copy(_keep[i], State.Entities.All[i].B, EntityTable.Size);
            (State.Entities.All[i].PitchFine, State.Entities.All[i].HeadingFine) = _keepFine[i];
        }
    }

    /// <summary>The camera the in-between picture at t (0..1) draws with: player 1's in-between pose
    /// and the view distance part way too. SmoothPicture and WithInBetweenPoses place with the same
    /// one - one maths. The pose comes from the kept poses (InBetween), so no record needs moving
    /// for it: the skipped picture (PaintView off) has the same camera.</summary>
    Camera InBetweenCamera(double t)
    {
        var s = State;
        if (Inside) return s.Camera;                                       // indoors the camera is fixed ($47D6)
        var p = s.Entities.All[0];
        var (x, y, z) = InBetween(0, t) is { } at ? (at.X, at.Y, at.Z)
            : (p.UW(0x4A), p.W(0x4C), p.UW(0x4E));                  // the pass's place: the player jumped or died
        var distance = (int)Math.Round(_distanceBefore + (_distanceNow - _distanceBefore) * t);
        return s.Ground.Place(x, y, z, distance, s.Lift);
    }


    /// <summary>Runs draw with every record at its in-between place for this blank, as SmoothPicture draws them
    /// (renderer 3d with the card: it reads the records itself): PoseInBetween's halfway ground heights, and
    /// Place's in-view mark +$1C and camera copy from the same in-between camera (InBetweenCamera) - so the card
    /// sees things at their halfway places on the right ground and in view exactly to this picture's edge. The
    /// records and the camera go back afterwards; without an in-between picture draw sees the pass's own.</summary>
    public void WithInBetweenPoses(Action draw)
    {
        if (!SmoothWillDraw) { draw(); return; }
        var s = State;
        var t = Between();
        var camera = s.Camera;
        PoseInBetween(t);
        s.Camera = _pictureCamera is { } c && _pictureAt == s.BlankCount ? c : InBetweenCamera(t);   // this blank's SmoothPicture already worked it out
        _objects.PlaceForPicture(s.Entities, s.Camera);
        try { draw(); }
        finally
        {
            PutBack();
            foreach (var row in _objects.Rows) row.Clear();   // no draw walks these buckets here (DrawRow drains them); the next picture must start from empty ones
            s.Camera = camera;
        }
    }

    /// <summary>The camera the last in-between picture used (its sky bands follow it); null when the
    /// pass's own picture is showing.</summary>
    Camera? _pictureCamera;
    int _pictureAt = -1;                                    // the blank (BlankCount) it was worked out on

    /// <summary>The camera the on-screen picture was drawn with: the in-between camera while a smooth
    /// picture shows, null for the pass's own - Play.ScreenOf's sky-band rule, which the card's band rows and
    /// camera follow too (godot/GameView.cs BandRows, Real3DFrame). Read-only; no rule reads it.</summary>
    public Camera? PictureCamera => _pictureCamera;
}
