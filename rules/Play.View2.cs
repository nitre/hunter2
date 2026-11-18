namespace Hunter.Rules;

/// <summary>
/// ENHANCEMENT, NOT THE ORIGINAL (multiplayer piece mp_views): player 2's own picture. After the
/// normal pass has drawn player 1's picture, a second picture-only draw runs from player 2's
/// entity: the camera placed from his position ($374C's maths through Ground.Place), every record
/// placed for a picture only (Objects.PlaceForPicture - no collisions, no far write-back,
/// animations held), the ground and rows drawn into a second 320 x 200 view buffer - and then
/// every record and the camera put back, exactly the way Play.Smooth's in-between pictures do
/// (DemoTests.SmoothPicturesLeaveTheGameAlone proves that pattern moves nothing). Outdoors play
/// only; the panel is the shared one (the same soldier's panel on both halves).
/// </summary>
public sealed partial class Play
{
    /// <summary>Player 2's view buffer (the view's rows 0-139; Screen2 colours it, the panel rows
    /// stay the shared ones).</summary>
    public readonly View View2 = new();

    /// <summary>GameView turns it on with Settings.Players == 2: DrawSecondView draws each frame.
    /// Off - the default - nothing runs and the game is the original's byte for byte.</summary>
    public bool SecondView;

    /// <summary>The camera the last second picture used (its sky bands follow it, Screen2); null
    /// when none has run since the last pass. The test's writer clears it before hashing: it is
    /// the second picture's own footprint, not game state.</summary>
    public Camera? Picture2Camera { get; set; }

    /// <summary>A second picture was drawn THIS frame (GameView shows View2 in the right half;
    /// GameView clears it every frame, so a frame without a pass shows player 1's picture in both
    /// halves instead of a stale one).</summary>
    public bool HasSecondView { get; set; }

    readonly byte[][] _keep2 = Enumerable.Range(0, EntityTable.MaxCount).Select(_ => new byte[EntityTable.Size]).ToArray();

    /// <summary>Player 2's picture into View2, after the normal pass; every record and the camera
    /// restored after. A picture only - the game it leaves behind is the pass's own.</summary>
    public void DrawSecondView()
    {
        var s = State;
        if (!SecondView || s.Players < 2) return;
        var p2 = s.Entities.All[1];
        if (Quit || _interior || Mode != PlayMode.Play || s.InsideView || Waiting || !p2.Alive)
        {
            Picture2Camera = null;
            HasSecondView = false;
            return;
        }
        var camera = s.Camera;
        var raster = _raster.Save();                                       // the picture leaves the raster byte for byte (Raster.Save)
        for (var i = 0; i < s.Entities.Count; i++) Array.Copy(s.Entities.All[i].B, _keep2[i], EntityTable.Size);
        var cam = s.Ground.Place(p2.UW(0x4A), p2.W(0x4C), p2.UW(0x4E), s.Distance, s.Lift, s.ViewAngle2);
        s.Camera = cam;
        _objects.PlaceForPicture(s.Entities, cam);
        View2.Clear();
        s.Ground.Draw(View2, _raster, cam, row => _objects.DrawRow(View2, cam, s.Entities, row));
        if (s.MapMarks) s.Log.DrawMarks(View2, p2);
        Picture2Camera = cam;
        HasSecondView = true;
        _raster.Restore(raster);
        for (var i = 0; i < s.Entities.Count; i++) Array.Copy(_keep2[i], s.Entities.All[i].B, EntityTable.Size);
        s.Camera = camera;
        foreach (var k in _keep2) Array.Clear(k);          // nothing of the picture's own left behind: the records were the pass's
    }
}
