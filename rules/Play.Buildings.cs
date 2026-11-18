namespace Hunter.Rules;

/// <summary>
/// VisitBuilding ($46F0) and the inside loop ($47A8): what the player sees entering a building
/// (re/formats/buildings.md). A store (+$90 negative) is a screen of its own (Store.cs); any other
/// building shows its inside: the building's record takes its inside model, the camera is placed
/// on it once and the room drawn into a backdrop ($48BE); every pass then copies the backdrop,
/// updates the panel, the records and the sort, and draws the rows' objects over it ($47BE-$47E2).
/// The fades: the visit waits FadeToAndWait $4758; the colours step in rules/Fade.cs ($69B2, run
/// from Play's blank).
/// </summary>
public sealed partial class Play
{
    bool _interior, _interiorBegun;
    /// <summary>The last pass drew the inside ($47A8's loop) - not the inside flag, which the visit sets ($46F0) after the
    /// pass drew the outside and before the fade: the picture on screen.</summary>
    public bool DrawnInside => _interior;

    // The two physical view buffers ($308FA, $385FA): a pass draws into the one SwapBuffers ($620E)
    // gave it (A on even passes from the first). Leaving a store sets $148 and $14C both to A, so the
    // copper goes on showing B: the picture the store pass drew if that was B, else the one before.
    bool _drawsA;
    byte[]? _forceShown;
    View? _backdrop;
    static bool Bit(int v, int n) => (v >> n & 1) != 0;

    /// <summary>The player visiting the building (ENHANCEMENT, NOT THE ORIGINAL): whose record
    /// EnterInteriorAndSetBounds ($47E6) marks, whose panel InteriorBegin ($47CE) draws, whose marks the way
    /// out ($478C) clears, whose weapon LeaveStore ($87DC) reloads. 0 is the original.</summary>
    public int VisitPlayer;

    /// <summary>VisitBuilding ($46F0), reached from EndOfPass when a door asked ($99 bit 1).</summary>
    void Visit(int n = 0)
    {
        var s = State;
        VisitPlayer = n;
        s.InsideView = true;                                                 // $46F0 bset #0,$99
        var b = s.Entities.All[s.BuildingSlot];
        if (Buildings.MissingItem(s, b)) { Refuse(); return; }               // $46FA bsr, $46FC bmi $432A
        var needs = b.W(0x94);
        if (Bit(needs, 12))                                                  // $4704 GuardedBuilding $4668 (returns to $432A)
        {
            if (Bit(needs, 15))                                              // $466C: the goal building
            {
                var rec = 0x9072 + s.Mission;
                if (s.EarlyHqBack && Games.Unmet(s, rec) && !Games.TimeUp(s, rec))   // ENHANCEMENT (EarlyHqBack): told off, back out
                    Messages.Show(s, 0x9612);                                // the debrief's "You have not yet completed your mission!"
                else Buildings.GameOver(s);
                Refuse();
                return;
            }
            if (Bit(needs, 14) && (s.Flags0A4 & 0x20) == 0 && s.Score >= 0)     // $4674-$4686: not disguised, score not negative
            {
                s.Status216 |= 0x20;                                         // $4688 bset #5,$216
                Buildings.GameOver(s);
                Refuse();
                return;
            }
        }
        Messages.Show(s, 0xB5D2, b.W(4) - 0x95);                             // $470E: "You have entered a ..."
        if (b.W(0x90) < 0) { VisitStore(b); return; }                        // $4724 bpl $4758
        // $4758 FadeToAndWait(0, 0): the flare's light out ($68B0 bclr #0,$A2; $68D2 clr.l $184), then the view
        // fades to black one step a blank ($194 = 0) and the visit waits for it. Those blanks count as play ($99
        // bit 7 stays set), so they are the inside's first time step - the one that carries the player past the
        // room's front edge ($42E6); without them he is still on the door line and put straight back out.
        // A lit flare ($A2 bit 0) first runs a one-step fade ($68B8-$68C8: $196 = 0 steps, done at its first blank).
        // After the fade, the A500 spends real time at $4760-$4772 (EnterInteriorAndSetBounds, SpawnInterior, the
        // room drawn into the backdrop) with the screen black; those blanks count too. The port has no CPU time,
        // so it waits them here: the original's enter_building recordings (all at 09:00, fade 14) take 31 / 30 / 34
        // ticks from the touching pass to the first inside pass (Hunter / Missions / Action) where the port took
        // 14 + 1 - RoomSetUpBlanks is Hunter's 16 (the rooms differ by a few blanks: 15 / 19 in the other two).
        _visitWait = FadeLevel + ((s.Flags0A2 & 1) != 0 ? 1 : 0) + RoomSetUpBlanks;
        s.Flags0A2 &= 0xFE;                                                // the port clears the flare itself (the +1 blank above is $68B8's
        s.Light184 = 0;                                                    // one-step flare fade, so the engine takes the plain branch)
        Fade.FlareOn = false;
        Fade.FadeToAndWait(0, 0);                                          // $4758: the view down to black, a step a blank, waited
        _visitInto = b;
        // tests replaying the original put its blanks in themselves (ManualResume): no wait on top of them
        if (_visitWait == 0 || ManualResume) EndVisitWait();
    }

    // FadeToAndWait's blanks still to come, then the room's set-up time; then $4760-$4768: the way in
    int _visitWait;
    /// <summary>Blanks the A500 spent setting up a room after the fade (see Visit); measured from the
    /// original's enter_building_hunter recording.</summary>
    public const int RoomSetUpBlanks = 16;
    Entity? _visitInto;

    /// <summary>A visit is waiting for its fade to black ($4758, $6952 WaitFade): no pass runs, the blanks
    /// count. The frame loop (GameView, GameStep) runs only VerticalBlank meanwhile. In the demo, also
    /// while the pass waits for its pace word (Demo.PaceHeld), PlayStart's fade and a quit's StopPlay blanks.</summary>
    public bool Waiting => _visitInto is not null || _playStarting || _ending > 0 || Demo.PaceHeld(State);

    // one blank of the visit's wait: the engine steps FadeToAndWait's fade itself (Fade.Blank in
    // VerticalBlank, $69B2 at $194 = 0: a step every blank, the level down by one); the port counts
    // the room's set-up blanks on top of it here
    void VisitWaitBlank()
    {
        if (--_visitWait <= 0) EndVisitWait();
    }

    void EndVisitWait()
    {
        var b = _visitInto!;
        _visitInto = null;
        _visitWait = 0;
        EnterInterior(b);
    }

    // PlaceAtDoor ($432A) at the end of a refused visit; $99 bit 0 stays set until then (the blanks
    // the message takes count as inside: no camera step). Tests replaying the original's blanks
    // call FinishVisit where it reached $432A.
    void Refuse()
    {
        _refused = true;
        if (!ManualResume) FinishVisit();
    }
    bool _refused;

    public void FinishVisit()
    {
        if (!_refused) return;
        _refused = false;
        Buildings.PlaceAtDoor(State);
    }

    // $4758-$4768: the way in
    void EnterInterior(Entity b)
    {
        var s = State;
        s.SavedDistance = s.Distance;                                        // $4760 move.l $1526,-(sp)
        var p = s.Entities.All[VisitPlayer];
        p.B[0x20] = 0xFF;                                                    // $47E6 st $270
        p.B[9] |= 0x20;                                                      // $47EA bset #5,$259
        b.B[6] |= 0x40;                                                      // $47F4 bset #$1E,(6,A4)
        s.SavedModel155C = b.W(0x24);                                        // $47FA
        b.SetW(0x24, b.W(0x90));                                             // $4804: the inside model
        Buildings.ModelHalfSizes(s, b);                                      // $4808
        var t = 0x4862 - 0x800 + (short)((b.W(0x24) - 0xAE) << 2);           // $480C-$481A: 4 bytes a model: horizon, distance
        s.Horizon1518 = (short)(s.Data.Main[t] << 8 | s.Data.Main[t + 1]);
        s.Distance = (short)(s.Data.Main[t + 2] << 8 | s.Data.Main[t + 3]);  // $4824 ext.l
        InteriorView(b);                                                     // $482E
        // $4836-$485A: the bounds, from the record's position and the new half sizes
        int x = b.W(0x4A) - b.W(0x6E), y = b.W(0x4C) - b.W(0x70), z = b.W(0x4E) - b.W(0x72);
        s.Bounds[0] = (short)x; s.Bounds[1] = (short)y; s.Bounds[2] = (short)z;
        s.Bounds[3] = (short)(x + 2 * b.W(0x6E)); s.Bounds[4] = (short)(y + 2 * b.W(0x70)); s.Bounds[5] = (short)(z + 2 * b.W(0x72));
        s.ContainerAt = Buildings.FindInterior(s, b);                        // $4972: the block of records
        StartState.SpawnInterior(s, s.ContainerAt);
        Fade.FadeToLight(0);                                                 // $476C-$476E: up from black, a step every blank
        _lastFadeTarget = FadeTarget;                                        // issued here: no re-issue at the next blank
    }

    // $48BE InteriorView: the camera on the building (horizon from $1518 + its ground), the room
    // (ground, then its own model) drawn into the backdrop
    void InteriorView(Entity b)
    {
        var s = State;
        var horizon = (short)(s.Horizon1518 + b.W(0x74));                    // $48D8
        s.Camera = s.Ground.Place(b.UW(0x4A), b.W(0x4C), b.UW(0x4E), s.Distance, s.Lift);   // $48E0 $3750
        b.SetW(0x52, b.W(0x4C));                                             // $3760
        s.Camera.Height0E = horizon;                                         // $37E4: inside, $3750 leaves $0E alone
        LoadEntityPos(b);                                                    // $48E4 $32FC
        _backdrop = new View();                                              // $48E8 DrawBuffer = the backdrop, $48EE clear
        _backdrop.Clear();
        s.Ground.Draw(_backdrop, _raster, s.Camera, _ => { });               // $48F4 DrawView (the row lists are empty)
        _objects.DrawBuildingModel(_backdrop, s.Camera, b);                  // $48F8-$491E
    }

    // $32FC LoadEntityPos: on the ground there, old place = place, view position from the camera
    void LoadEntityPos(Entity e)
    {
        var s = State;
        int x = e.W(0x4A), y = e.W(0x4C), z = e.W(0x4E);
        e.B[0x1E] = 0xFF;
        e.SetW(0x50, x); e.SetW(0x52, y); e.SetW(0x54, z);
        var h = s.Ground.Height((ushort)x, (ushort)z);
        e.SetW(0x74, h);
        e.B[0x1E] = (byte)(h >= y ? 0xFF : 0);
        e.SetW(0x56, (x & 0xFFFF) * 2 - s.Camera.G1548);
        e.SetW(0x58, y);
        e.SetW(0x5A, (z & 0xFFFF) * 2 - s.Camera.G1544);
    }

    /// <summary>The start of an inside pass: CopyBackdropToView ($47C8: the buffer on show first
    /// kept for the item table), UpdatePanel ($47CE).</summary>
    public void InteriorBegin()
    {
        _interiorBegun = true;
        Array.Copy(View.Pixels, _shown, _shown.Length);
        Array.Copy(_backdrop!.Pixels, View.Pixels, View.Pixels.Length);
        State.Panel.Update(State, State.Entities.All[VisitPlayer], VisitPlayer);
    }

    /// <summary>After an inside pass's EndOfPass ($47A8): $A5 bit 5 (GameOver) puts the player out; the
    /// player out ($99 bit 0 clear) ends the loop: the way out ($4774-$47A4).</summary>
    void AfterInteriorPass()
    {
        var s = State;
        if ((s.Flags0A5 & 0x20) != 0) Buildings.PlaceAtDoor(s);              // $47A8-$47B0
        if (s.InsideView) return;                                            // $47B4 bne: the loop goes on
        var b = s.Entities.All[s.BuildingSlot];                              // $4774
        b.SetW(0x24, s.SavedModel155C);                                      // $4778
        Buildings.ModelHalfSizes(s, b);                                      // $477E
        b.B[6] &= 0xBF;                                                      // $4782 bclr #$1E,(6,A4)
        b.B[0x20] = 0;                                                       // $4788
        var p = s.Entities.All[VisitPlayer];
        p.B[0x20] = 0;                                                       // $478C sf $270
        p.B[9] &= 0xDF;                                                      // $4790 bclr #5,$259
        s.Distance = s.SavedDistance;                                        // $4796
        Buildings.SaveInterior(s);                                           // $47A0 $4982
        _interior = false;
        if (!ManualResume) ResumeAfterBuilding();                            // $47A4 bra $252A
    }

    /// <summary>Tests replaying the original put its blanks between the way out and $252A (the fade
    /// out) themselves and call ResumeAfterBuilding at the blank the original reached $2530.</summary>
    public bool ManualResume;

    /// <summary>$252A-$2560 after the way out (like a screen's return): a fresh blank, then the time
    /// step is 1 and play counts from there.</summary>
    public void ResumeAfterBuilding()
    {
        State.InPlay = true;
        View.Clear();                                                        // $2540 ClearViewWait: the buffer the last inside pass drew
        Blanks = 1;
        LastTicks = 1;
        Fade.SnapTo(0);                                                      // $479C FadeOut, waited - compressed (Fade.cs's SnapTo doc)
        _lastFadeTarget = -1;                                                // $252A -> $255A: FadeToLight(2) at the next blank
    }

    // $4726-$4738: LeavePlay, StoreOpen(k), StopPlay (ResetChannels $B186: the four channels
    // silenced; FlareOff $689A: $A2 bit 0, the light $184), then the store's screen
    void VisitStore(Entity b)
    {
        var s = State;
        s.StoreK = -b.W(0x90) - 1;                                           // $472A-$472E
        Store.Open(s, s.StoreK);
        for (var ch = 3; ch >= 0; ch--) s.Channels.Set(1, ch, 0);
        s.Flags0A2 &= 0xFE;
        s.Light184 = 0;
        _opening = 0x9A78;
        Mode = PlayMode.Screen;
        Channels.Silence();                                                  // $4726 LeavePlay -> $2E12
        _leaving = true;
    }

    // $473C-$475A after END: PlayStart ($24FA: the marks bitmap, the animations, the panel set up
    // again and its clock; then $252A like any screen's return), PlaceAtDoor, StoreClose(k),
    // $99 bit 7, CarriedThenLoad ($A2A4: the masks, then the weapon in hand)
    void LeaveStore()
    {
        var s = State;
        Screens.CloseStore();
        _forceShown = _drawsA ? (byte[])_shown.Clone() : (byte[])View.Pixels.Clone();
        _drawsA = false;                                                     // the next pass draws into A
        s.Log.BuildMarks(s.Terrain);
        Objects.InitialAnim(_d.Main).CopyTo(s.Anim, 0);
        s.Panel.SetUp(s);
        Buildings.PlaceAtDoor(s);
        Store.Close(s, s.StoreK);
        Items.Carried(s);
        Vehicles.LoadWeapon(s, s.Entities.All[VisitPlayer]);
        Mode = PlayMode.Play;
        PlayStartFade();                                                     // $251A-$2560: play begins once it is over
    }
}
