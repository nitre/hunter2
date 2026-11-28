namespace Hunter.Rules;

/// <summary>STUNTIN': the original's road lookup, ported routine by routine from the original program (as ScrCar's physics):
/// RoadFrame = processGameFrame's game part (calculateCameraTransform, the grid lookup, applyTrackSegmentGeometry,
/// checkRoadBoundaries, lastValidSegmentIndex) and WheelHeights = updateWheelHeightsFromTrack (the height under each
/// contact point, the road's edge lip and the floor). They read the track's own tables (LoadTrack). The data registers
/// are kept between routines as the 68000's, so byte and word moves leave the upper bits as the original does.</summary>
public sealed partial class ScrCar
{
    public const int
        trackViewOffsetX = 0x1BB04, trackViewOffsetY = 0x1BB06, subGridOffsetX = 0x1BB07, subGridOffsetZ = 0x1BB09,
        lbW00D410 = 0x1BB10, tempByte1 = 0x1BB18, tempByte2 = 0x1BB19, playerSegmentIndex = 0x1BB1C,
        baseCoordinateX = 0x1BB22, cameraGridOffsetXLow = 0x1BB23, baseCoordinateY = 0x1BB26, cameraGridOffsetZLow = 0x1BB27,
        cameraGridOffsetXHigh = 0x1BB2E, cameraGridOffsetZHigh = 0x1BB32, restartTimerCountdown = 0x1BB41,
        trackSegmentLimitDoubled = 0x1BB59, maxSegmentIndexDoubled = 0x1BB5A, wheelOutOfLateralBoundsFlag = 0x1BB65,
        trackModeParameter = 0x1BB6A, geometryFormatFlag = 0x1BB79, segmentOrientationPrimary = 0x1BB7B,
        currentSegmentIndex = 0x1BB85, trackGeometryTypeIndex = 0x1BB86, trackSegmentLimit = 0x1BB97, maxSegmentIndex = 0x1BB98,
        lastValidSegmentIndex = 0x1BB9B, playerLateralPosition = 0x1BBA1, segmentDataIndexScaled = 0x1BBA3,
        offRoadSideFlags = 0x1BBD1, gridLookupX = 0x1BBD5, gridLookupY = 0x1BBD6, segmentOrientationAlternate = 0x1BBD9,
        wheelBouncePhaseAccumulator = 0x1BBDA, segmentAlternateFlag = 0x1BBDC, trackSideIndicator = 0x1BBE1,
        trackHeightDifference = 0x1BBF2, tempWord2 = 0x1BBF8, wheelDataOffset = 0x1BBF9, cameraHeightBaseline = 0x1BBFA,
        splineControlPoint1 = 0x1BC02, splineControlPoint2 = 0x1BC04, splineControlPoint3 = 0x1BC06, splineControlPoint4 = 0x1BC08,
        segmentBezierOffset1 = 0x1BC0E, segmentBezierOffset2 = 0x1BC10, lbW00D51C = 0x1BC1C, lateralPositionOutOfBounds = 0x1BC22,
        cameraYawPerspectiveOffset = 0x1BC2E, cameraRotationFlags = 0x1BC30, perspectiveDepthDivisor = 0x1BC36,
        forwardPositionIntermediate = 0x1BC40, segmentBlendParam = 0x1BC41, segmentSlopeFlags = 0x1BC4A,
        lateralTrackPosition = 0x1BC4D, offsetFromRoadCenter = 0x1BC5C, lateralRoadPosition = 0x1BC5E,
        primaryGeometryOffset = 0x1BC8C, alternateGeometryOffset = 0x1BC90, geometryDatabaseOffset = 0x1BCBC,
        rotatedCameraX = 0x1BCCC, carHeightPosition = 0x1BCD0, rotatedCameraZ = 0x1BCD4,
        trackSegmentGrid = 0x1C280, debrisParticleYPositions = 0x1C3C0, segmentGeometryIndices = 0x1C4C0,
        segmentAlternateGeometryIndices = 0x1C524, trackSegmentCoordinates = 0x1C588, trackSegmentPropertiesTable = 0x1C5EC,
        segmentInterpolationPoint1 = 0x1C650, segmentInterpolationPoint2 = 0x1C718, numTrackSegments = 0x1CA1A,
        reciprocalTable = 0x1CC46, arctanTable = 0x1DC46, trackGeometryDatabase = 0x1EF82, segmentGeometryOffsetTable = 0x1EFA2,
        lbL04DFB8 = 0x5C6B8, transformedGridX = 0x65EC0, transformedGridY = 0x65EC2,
        trackProgressionByte = 0x1BB0A, trackProgressionOffset = 0x1BB0E, segmentDepthCounter = 0x1BB58,
        renderingLoopIndex = 0x1BB60, lbB00D4E5 = 0x1BBE5, prevSegmentDepthCounter = 0x1BB9E,
        prevTrackProgressionOffset = 0x1BB9F, visibilityAccumulator = 0x1BC26, prevVisibilityAccumulator = 0x1BC28;

    // the 68000's data registers between routines (byte and word moves keep the upper bits)
    uint d0, d1, d2, d3, d4, d5, d7;
    int a4, a5;

    static void SB(ref uint d, int v) => d = (d & 0xFFFFFF00) | (byte)v;
    static void SW(ref uint d, int v) => d = (d & 0xFFFF0000) | (ushort)v;
    static int Bs(uint d) => (sbyte)d;
    static int Ws(uint d) => (short)d;
    /// <summary>(t ASL.W #7, BCLR #15) MULS, ASL.L #1, SWAP: the word times t / 256 (t a byte as a fraction).</summary>
    static int Frac(int word, int t) => (int)(((long)(short)word * ((t << 7) & 0x7FFF) << 1) >> 16);
    int GeometryAt(int offsetWord) => (((ushort)((offsetWord << 8) | ((ushort)offsetWord >> 8)) - 0xB100) & 0xFFFF) + trackGeometryDatabase;

    /// <summary>A track's tables from game_data/scr/track_N.bin (the original's loader's output):
    /// 4-byte address, 2-byte length, the bytes - repeated.</summary>
    public void LoadTrack(byte[] f)
    {
        for (var k = 0; k + 6 <= f.Length;)
        {
            int at = f[k] << 24 | f[k + 1] << 16 | f[k + 2] << 8 | f[k + 3], n = f[k + 4] << 8 | f[k + 5];
            Array.Copy(f, k + 6, Ram, at, n);
            k += 6 + n;
        }
    }

    // ---------------------------------------------------------------- processGameFrame's game part ($5E8E4)
    /// <summary>After a pass: the piece under the car, its place across the road and the steering's target for the next
    /// pass, the off-track flags, the last piece on the track. Returns false off the grid.</summary>
    public bool RoadFrame()
    {
        CameraTransform();
        B(gridLookupX, 0); B(gridLookupY, 0);
        var found = SegmentAtGrid();
        if (found && (byte)d0 == 0xFF)
        {
            SB(ref d0, B(subGridOffsetX)); SB(ref d2, B(subGridOffsetZ));
            SubGridAdjust();
            found = (byte)d0 != 0xFF;
        }
        if (!found)
        {
            B(offTrackStateFlags, 0xC0);
            B(trackProgressionOffset, B(prevTrackProgressionOffset));
            W(visibilityAccumulator, W(prevVisibilityAccumulator));
            B(segmentDepthCounter, B(prevSegmentDepthCounter));
            return false;
        }
        B(currentSegmentIndex, (byte)d0);
        ApplySegmentGeometry();
        RoadBoundaries();
        SB(ref d0, B(currentSegmentIndex));
        B(playerSegmentIndex, (byte)d0);
        if ((B(offTrackStateFlags) & 0x40) == 0) B(lastValidSegmentIndex, (byte)d0);
        TrackPosition();
        B(prevTrackProgressionOffset, B(trackProgressionOffset));
        W(prevVisibilityAccumulator, W(visibilityAccumulator));
        B(prevSegmentDepthCounter, B(segmentDepthCounter));
        return true;
    }

    /// <summary>calculateTrackPosition ($5C138): how far along its piece the car is (trackProgressionByte - the steering's
    /// look at the piece's end - and the drawing's counts).</summary>
    void TrackPosition()
    {
        SW(ref d0, W(lbW00D410));
        SB(ref d4, B(trackModeParameter));
        SW(ref d4, (ushort)d4 << 8);
        if ((B(reverseDirectionFlag) & 0x80) != 0) SW(ref d0, -(Ws(d0) - Ws(d4)));
        W(trackProgressionByte, (ushort)d0);
        SW(ref d0, (ushort)d0 + 0x40);
        W(trackProgressionOffset, (ushort)d0);
        if (Ws(d0) >= Ws(d4)) { B(trackProgressionOffset, 0x80); SW(ref d0, 0); }
        SW(ref d0, (ushort)d0 >> 8);
        SB(ref d0, ((byte)d0 + 1) << 1);
        B(lbB00D4E5, (byte)d0);
        SB(ref d0, (byte)d0 << 1);
        B(renderingLoopIndex, (byte)d0);
        if ((B(reverseDirectionFlag) & 0x80) != 0) { SW(ref d0, (ushort)d4 - W(lbW00D410)); SW(ref d0, (ushort)d0 >> 8); }
        else SB(ref d0, B(lbW00D410));
        SB(ref d3, 0x20 - (byte)d0);
        if ((B(trackProgressionOffset) & 0x80) != 0) SB(ref d3, (byte)d3 + B(trackModeParameter));
        B(segmentDepthCounter, (byte)d3);
        SW(ref d3, (byte)d3);
        W(visibilityAccumulator, (ushort)d3);
    }

    /// <summary>calculateCameraTransform ($600F4) with convertCameraToGridSpace.</summary>
    void CameraTransform()
    {
        SW(ref d0, (W(cameraAngleY) + 0x2000) & 0xC000);
        W(cameraRotationFlags, (ushort)d0);
        CameraToGrid();
        d0 = (uint)L(cameraWorldY) >> 11;
        W(carHeightPosition, (ushort)d0);
        SW(ref d3, 0x0780);
        SW(ref d0, W(averageWheelVelocity));
        if ((ushort)d0 >= 0x0500) { SW(ref d0, (ushort)d0 << 1); SW(ref d3, 0x0280); }
        SW(ref d0, (ushort)d0 + (ushort)d3);
        SW(ref d3, W(cameraAngleX));
        if (Ws(d3) < 0) { SW(ref d3, Ws(d3) >> 1); SW(ref d0, (ushort)d0 - (ushort)d3); }
        SW(ref d0, Ws(d0) >> 4);
        SW(ref d0, (ushort)d0 + W(carHeightPosition));
        W(cameraHeightBaseline, (ushort)d0);
        d0 = (uint)L(rotatedCameraX) >> 12;
        SW(ref d0, -((ushort)d0 & 0x7FF));
        B(cameraGridOffsetXLow, (byte)d0); SW(ref d0, (ushort)d0 >> 8); B(cameraGridOffsetXHigh, (byte)d0);
        d0 = (uint)L(rotatedCameraZ) >> 12;
        SW(ref d0, -((ushort)d0 & 0x7FF));
        B(cameraGridOffsetZLow, (byte)d0); SW(ref d0, (ushort)d0 >> 8); B(cameraGridOffsetZHigh, (byte)d0);
        SW(ref d0, ((W(cameraAngleY) + 0x2000) & 0x3FFE) - 0x2000);
        W(cameraYawPerspectiveOffset, (ushort)d0);
    }

    void CameraToGrid()
    {
        SW(ref d0, (ushort)d0 >> 8);
        B(tempByte2, (byte)d0);
        foreach (var (world, k) in new[] { (cameraWorldX, 0), (cameraWorldZ, 2) })
        {
            if (W(world + 2) == 0) W(world + 2, 1);
            d0 = (uint)L(world) << 1;
            d0 = d0 >> 16 | d0 << 16;
            B(subGridOffsetX + k, (byte)d0);
            SW(ref d0, (ushort)d0 >> 8);
            B(trackViewOffsetX + k, (byte)d0);
        }
        var t = B(tempByte2);
        int x = L(cameraWorldX), z = L(cameraWorldZ);
        if ((t & 0x80) == 0)
        {
            if ((t & 0x40) == 0) { L(rotatedCameraX, x); L(rotatedCameraZ, z); }
            else { L(rotatedCameraZ, x); L(rotatedCameraX, 0x08000000 - z); d0 = (uint)(0x08000000 - z); }
        }
        else if ((t & 0x40) == 0) { L(rotatedCameraX, 0x08000000 - x); L(rotatedCameraZ, 0x08000000 - z); d0 = (uint)(0x08000000 - z); }
        else { L(rotatedCameraZ, 0x08000000 - x); L(rotatedCameraX, z); d0 = (uint)(0x08000000 - x); }
    }

    /// <summary>getSegmentAtGridCoordinate ($5FDDA): false (carry) off the 16 x 16 grid; d0 the piece ($FF none), d1 the cell.</summary>
    bool SegmentAtGrid()
    {
        SB(ref d0, B(trackViewOffsetY) + B(gridLookupY));
        if ((byte)d0 >= 0x10) return false;
        SB(ref d0, (byte)d0 << 4);
        B(tempByte4, (byte)d0);
        SB(ref d0, B(trackViewOffsetX) + B(gridLookupX));
        if ((byte)d0 >= 0x10) return false;
        SB(ref d0, ((byte)d0 & 0x0F) | B(tempByte4));
        SB(ref d1, (byte)d0);
        SB(ref d0, B(trackSegmentGrid + Ws(d1)));
        return true;
    }

    /// <summary>findSegmentBySubGridAdjustment ($5FF14): the neighbour cell the car's place inside its cell leans to.</summary>
    void SubGridAdjust()
    {
        B(tempByte4, (byte)d0); B(tempByte3, (byte)d2);
        int x = (byte)d0, z = (byte)d2, cell = (byte)d1;
        var carry = x + z > 0xFF;
        if (z < x)
        {
            if (carry) { if ((cell & 0x0F) == 0x0F) { SB(ref d0, 0xFF); return; } cell += 1; }
            else { if ((cell & 0xF0) == 0) { SB(ref d0, 0xFF); return; } cell -= 0x10; }
        }
        else if (carry) { if ((cell & 0xF0) == 0xF0) { SB(ref d0, 0xFF); return; } cell += 0x10; }
        else { if ((cell & 0x0F) == 0) { SB(ref d0, 0xFF); return; } cell -= 1; }
        SB(ref d1, cell);
        SB(ref d0, B(trackSegmentGrid + Ws(d1)));
    }

    /// <summary>loadTrackSegmentConfiguration ($5FE56): the piece d1's geometry and steering values.</summary>
    void LoadSegment()
    {
        int seg = Ws(d1);
        SB(ref d2, B(segmentGeometryIndices + seg));
        B(geometryFormatFlag, (byte)d2);
        SB(ref d2, (byte)d2 << 1);
        W(primaryGeometryOffset, W(segmentGeometryOffsetTable + Ws(d2)));
        SB(ref d2, B(segmentAlternateGeometryIndices + seg));
        var x = ((byte)d2 & 0x80) != 0;
        SB(ref d2, (byte)d2 << 1);
        SB(ref d0, x ? 2 : 0);
        B(segmentAlternateFlag, (byte)d0);
        SW(ref d0, W(segmentGeometryOffsetTable + Ws(d2)));
        W(alternateGeometryOffset, (ushort)d0);
        SB(ref d1, (byte)d1 << 1);
        W(segmentBezierOffset1, W(segmentInterpolationPoint1 + Ws(d1)));
        W(segmentBezierOffset2, W(segmentInterpolationPoint2 + Ws(d1)));
        SB(ref d1, (byte)d1 >> 1);
        var p = B(trackSegmentPropertiesTable + Ws(d1));
        B(segmentSlopeFlags, p & 0xC0);
        B(reverseDirectionFlag, (p & 0x10) << 3);
        B(trackGeometryTypeIndex, p & 0x0F);
        SB(ref d0, (p & 0x0F) << 1);
        SB(ref d2, (byte)d0);
        W(geometryDatabaseOffset, W(trackGeometryDatabase + Ws(d2)));
        var a0 = GeometryAt(W(geometryDatabaseOffset));
        B(segmentSteeringFlags, B(a0 + 1));
        SB(ref d2, B(a0));
        SB(ref d0, B(a0 + Ws(d2))); SB(ref d2, (byte)d2 + 1);
        int lim = (byte)d0;
        B(trackSegmentLimit, lim); B(trackSegmentLimitDoubled, lim << 1);
        B(maxSegmentIndex, lim - 2); B(maxSegmentIndexDoubled, (lim - 2) << 1);
        B(trackModeParameter, (lim >> 1) - 1);
        SB(ref d3, lim << 1);
        SB(ref d0, B(a0 + Ws(d2))); SB(ref d2, (byte)d2 + 1);
        SB(ref d0, ((byte)d0 & 1) << 7);
        B(trackDirectionInversionFlag, (byte)d0);
        B(segmentOrientationPrimary, B(a0 + Ws(d2))); SB(ref d2, (byte)d2 + 1);
        B(segmentOrientationAlternate, B(a0 + Ws(d2))); SB(ref d2, (byte)d2 + 3);
        B(segmentBaseSteeringOffset, B(a0 + Ws(d2))); SB(ref d2, (byte)d2 + 1);
    }

    /// <summary>transformSegmentToViewSpace ($5FF94): piece d0's grid cell relative to the car's, turned with the view.</summary>
    void SegmentToView()
    {
        SW(ref d0, (ushort)d0 & 0xFF);
        int c = B(trackSegmentCoordinates + Ws(d0));
        int x = (c & 0x0F) - B(trackViewOffsetX), y = (c >> 4) - B(trackViewOffsetY);
        var f = B(cameraRotationFlags);
        if ((f & 0x80) == 0) { if ((f & 0x40) != 0) (x, y) = (-y, x); }
        else if ((f & 0x40) == 0) { x = -x; y = -y; }
        else (x, y) = (y, -x);
        SB(ref d0, x); SB(ref d3, y);
        B(transformedGridX, x); B(transformedGridY, y);
        SB(ref d0, ((byte)x << 3) + B(cameraGridOffsetXHigh)); B(baseCoordinateX, (byte)d0);
        SB(ref d3, ((byte)y << 3) + B(cameraGridOffsetZHigh)); B(baseCoordinateY, (byte)d3);
    }

    /// <summary>applyTrackDirectionTransform ($5C3DA): the car from the piece's cell corner, in the piece's own turn.</summary>
    void DirectionTransform()
    {
        var t = B(trackHeightDifference);
        int bx = W(baseCoordinateX), by = W(baseCoordinateY);
        int w1, w2;
        if ((t & 0x80) == 0) { if ((t & 0x40) == 0) { w1 = -bx; w2 = -by; } else { w1 = -by; w2 = 0x800 + bx; } }
        else if ((t & 0x40) == 0) { w1 = 0x800 + bx; w2 = 0x800 + by; }
        else { w1 = 0x800 + by; w2 = -bx; }
        W(tempWord1, w1); W(tempWord2, w2);
        SW(ref d0, w2);
    }

    /// <summary>applyTrackSegmentGeometry ($5BE44): on the current piece, the car's place across (lateralRoadPosition) and
    /// along (lbW00D410), and the steering's target (straight: the piece's way; diagonal: plus its offset; a corner: the
    /// tangent at the car's angle round the corner's centre).</summary>
    void ApplySegmentGeometry()
    {
        while (true)
        {
            SB(ref d1, B(currentSegmentIndex));
            LoadSegment();
            a5 = GeometryAt(W(geometryDatabaseOffset));
            SW(ref d0, (ushort)d1);
            SegmentToView();
            W(trackHeightDifference, W(cameraRotationFlags) - W(segmentSlopeFlags));
            var steer = B(segmentSteeringFlags);
            int Word(int k) => B(a5 + k + 1) << 8 | B(a5 + k);
            if ((steer & 0x80) == 0)
            {
                if ((steer & 0x40) == 0)
                {
                    DirectionTransform();
                    SW(ref d3, Word(2));
                    SW(ref d0, W(tempWord1) - Ws(d3));
                    W(lateralRoadPosition, (ushort)d0);
                    W(lbW00D410, W(tempWord2));
                    W(segmentTargetAngle, W(segmentSlopeFlags));
                    return;
                }
                DirectionTransform();
                B(tempByte3, 0xB5);
                SW(ref d0, Frac(W(tempWord1) - W(tempWord2), 0xB5));
                SW(ref d3, Word(2));
                SW(ref d0, Ws(d0) - Ws(d3));
                W(lateralRoadPosition, (ushort)d0);
                B(tempByte3, B(a5 + 7));
                SW(ref d0, Frac(W(tempWord1) + W(tempWord2), B(a5 + 7)));
                W(lbW00D410, (ushort)d0);
                SW(ref d3, Word(4) + W(segmentSlopeFlags));
                W(segmentTargetAngle, (ushort)d3);
                return;
            }
            // a corner
            SB(ref d2, 2);
            CornerCentre();
            SW(ref d0, W(tempWord1)); SW(ref d3, W(tempWord2));
            Division();
            var angle = (ushort)d0;
            VectorMagnitude();
            W(perspectiveDepthDivisor, (ushort)d0);
            SW(ref d0, angle);
            SW(ref d0, (ushort)d0 + W(cameraRotationFlags));
            SW(ref d0, Ws(d0) >= 0 ? (ushort)d0 - 0x8000 : (ushort)d0 + 0x8000);
            W(lbW00D51C, (ushort)d0);
            SW(ref d0, (ushort)d0 + 0x4000 - W(trackDirectionInversionFlag));
            W(segmentTargetAngle, (ushort)d0);
            SB(ref d4, 1 - (steer & 3));
            SW(ref d3, (Word(6) << 6) & 0xFFFF);
            SW(ref d0, W(lbW00D51C) - Ws(d3) - W(segmentSlopeFlags));
            if (Ws(d0) < 0) SW(ref d0, -Ws(d0));
            B(tempByte3, B(a5 + 8));
            SW(ref d0, Frac((ushort)d0, B(a5 + 8)));
            if (Bs(d4) < 0) { SB(ref d4, -Bs(d4)); d4 &= 7; SW(ref d0, (ushort)d0 << (int)d4); }
            else { d4 &= 7; SW(ref d0, (ushort)d0 >> (int)d4); }
            W(lbW00D410, (ushort)d0);
            SW(ref d0, (ushort)d0 >> 7);
            SB(ref d0, (byte)d0 + 2);
            if (Bs(d0) >= (sbyte)B(trackSegmentLimit))
            {
                var type = B(trackGeometryTypeIndex);
                if (type == 1 || type == 3)
                {
                    B(tempByte2, B(currentSegmentIndex));
                    if (B(reverseDirectionFlag) != 0) Retreat(); else Advance();
                    SB(ref d0, B(trackSegmentPropertiesTable + Ws(d1)) & 0x0F);
                    if ((byte)d0 == 4) continue;                                    // the next piece's geometry instead
                    B(currentSegmentIndex, B(tempByte2));
                }
            }
            PerspectiveAdjustment();
            SW(ref d3, Word(9) - W(perspectiveDepthDivisor));
            if ((B(trackDirectionInversionFlag) & 0x80) != 0) SW(ref d3, -Ws(d3));
            W(lateralRoadPosition, (ushort)d3);
            return;
        }
    }

    /// <summary>interpolateOpponentTrackPositionFromData ($5C6C4) with d2 = 2: the corner's centre from the car (tempWord1 / 2).</summary>
    void CornerCentre()
    {
        a5 = GeometryAt(W(geometryDatabaseOffset));
        int k = Ws(d2);
        int v1 = (short)(B(a5 + k + 1) << 8 | B(a5 + k)), v2 = (short)(B(a5 + k + 3) << 8 | B(a5 + k + 2));
        var t = B(trackHeightDifference);
        int bx = W(baseCoordinateX), by = W(baseCoordinateY);
        if ((t & 0x80) == 0)
        {
            if ((t & 0x40) == 0) { W(tempWord1, bx + v1); W(tempWord2, by + v2); }
            else { W(tempWord2, by + v1); W(tempWord1, bx - v2 + 0x800); }
        }
        else if ((t & 0x40) == 0) { W(tempWord1, bx - v1 + 0x800); W(tempWord2, by - v2 + 0x800); }
        else { W(tempWord2, by - v1 + 0x800); W(tempWord1, bx + v2); }
        SW(ref d3, v2);
        SB(ref d2, k + 4);
    }

    /// <summary>calculateDivision ($64D66): the angle of (d0, d3) (a word, 65536 a turn); d7 the ratio, d4 / d5 the inputs.</summary>
    void Division()
    {
        SW(ref d4, (ushort)d0);
        if (Ws(d0) < 0) SW(ref d0, -Ws(d0));
        SW(ref d5, (ushort)d3);
        if (Ws(d3) < 0) SW(ref d3, -Ws(d3));
        int x = (ushort)d0, y = (ushort)d3;
        if (y == x) { SW(ref d7, 0xFFFF); SW(ref d0, 0x2000); }
        else if (Ws(d3) < Ws(d0))
        {
            var q = ((uint)y << 16) / (uint)x;
            d3 = q; SW(ref d7, (ushort)q);
            SW(ref d3, ((ushort)q >> 4) & 0xFFFE);
            SW(ref d0, W(reciprocalTable + (ushort)d3));
            if (((Ws(d4) ^ Ws(d5)) & 0x8000) == 0) SW(ref d0, -Ws(d0));
            SW(ref d3, Ws(d4) < 0 ? 0xC000 : 0x4000);
            SW(ref d0, (ushort)d0 + (ushort)d3);
            return;
        }
        else
        {
            var q = ((uint)x << 16) / (uint)y;
            d0 = q; SW(ref d7, (ushort)q);
            SW(ref d0, ((ushort)q >> 4) & 0xFFFE);
            SW(ref d0, W(reciprocalTable + (ushort)d0));
        }
        SW(ref d3, (ushort)d4 ^ (ushort)d5);
        if (Ws(d3) < 0) SW(ref d0, -Ws(d0));
        if (Ws(d5) < 0) SW(ref d0, (ushort)d0 + 0x8000);
    }

    /// <summary>calculateVectorMagnitude ($64DE8): the length of (d4, d5) from the ratio d7.</summary>
    void VectorMagnitude()
    {
        if (Ws(d4) < 0) SW(ref d4, -Ws(d4));
        if (Ws(d5) < 0) SW(ref d5, -Ws(d5));
        if (Ws(d5) < Ws(d4)) (d4, d5) = (d5, d4);
        SW(ref d7, ((ushort)d7 >> 4) & 0xFFFE);
        d0 = (uint)((ushort)W(arctanTable + (ushort)d7) * (ushort)d4);
        d0 = d0 >> 16 | d0 << 16;
        SW(ref d0, (ushort)d0 + (ushort)d5);
    }

    /// <summary>calculatePerspectiveAdjustment ($5C65A): the corner's distance corrected by its shape's factor.</summary>
    void PerspectiveAdjustment()
    {
        static uint Sq(int w) { var v = (long)(short)w * (short)w; return (uint)Math.Abs(v); }
        d4 = Sq(W(tempWord1)) + Sq(W(tempWord2));
        d0 = Sq(W(perspectiveDepthDivisor));
        SB(ref d2, B(trackGeometryTypeIndex));
        var t = B(lbL04DFB8 + Ws(d2));
        B(tempByte3, t);
        d4 = (d4 - d0) >> 8;
        SW(ref d0, (ushort)d4);
        SW(ref d0, Frac((ushort)d0, t) >> 4);
        W(perspectiveDepthDivisor, W(perspectiveDepthDivisor) + Ws(d0));
    }

    void Advance() { SB(ref d1, B(currentSegmentIndex) + 1); if (Bs(d1) >= (sbyte)B(numTrackSegments)) SB(ref d1, 0); B(currentSegmentIndex, (byte)d1); }
    void Retreat() { SB(ref d1, B(currentSegmentIndex) - 1); if (Bs(d1) < 0) SB(ref d1, B(numTrackSegments) - 1); B(currentSegmentIndex, (byte)d1); }

    /// <summary>checkRoadBoundaries ($5FFFE + ...): 192 from the middle the side flag (the grind), 256 off the track (the
    /// restart timer $10 processed frames).</summary>
    void RoadBoundaries()
    {
        B(offRoadSideFlags, 0);
        var c = (short)(W(lateralRoadPosition) - 0xC0);
        if ((B(reverseDirectionFlag) & 0x80) != 0) c = (short)-c;
        W(offsetFromRoadCenter, c);
        var a = (short)Math.Abs((int)c);
        if (a >= 0xC0) B(offRoadSideFlags, c < 0 ? 0x80 : 0x02);
        if (a >= 0x100)
        {
            if ((B(offTrackStateFlags) & 0x80) != 0) return;
            B(offTrackStateFlags, 0x80);
            B(trackSideIndicator, B(offsetFromRoadCenter));
            B(restartTimerCountdown, 0x10);
            for (var k = 0x3E; k >= 0; k -= 2) W(debrisParticleYPositions + k, 0xD4);   // initializeDebrisParticlePositions
            return;
        }
        if ((B(offTrackStateFlags) & 0x40) != 0) return;
        B(offTrackStateFlags, 0); B(raceCompletionCheckFlag, 0);
    }

    // ---------------------------------------------------------------- updateWheelHeightsFromTrack ($5C0FA)
    /// <summary>The road's height under each contact point (wheel units), from the piece the car is on: across from
    /// lateralRoadPosition, along from lbW00D410, between the piece's height points; a wheel past the edge drops off the
    /// lip (checkLateralBoundsAndBounce) to the floor $1000.</summary>
    public (int FL, int FR, int Rear) WheelHeights()
    {
        SB(ref d1, B(playerSegmentIndex));
        B(currentSegmentIndex, (byte)d1);
        LoadSegment();
        B(renderingOrderMode, 0);
        SB(ref d1, 4);
        while (true)
        {
            B(wheelDataOffset, (byte)d1);
            SB(ref d0, B(playerSegmentIndex));
            if ((byte)d0 != B(currentSegmentIndex))
            {
                SB(ref d1, (byte)d0); B(currentSegmentIndex, (byte)d1);
                LoadSegment();
                SB(ref d1, B(wheelDataOffset));
            }
            B(tempByte3, B(segmentOrientationPrimary));
            SW(ref d0, (W(wheelCornerXFrontLeft + Ws(d1)) >> 4) + W(lateralRoadPosition));
            if ((ushort)d0 >= 0x0180)
            {
                B(wheelOutOfLateralBoundsFlag, B(wheelOutOfLateralBoundsFlag) | 0x80);
                W(lateralPositionOutOfBounds, (ushort)d0);
                SB(ref d0, Ws(d0) < 0 ? 0 : 0xFF);
            }
            else
            {
                SW(ref d0, Frac((ushort)d0, B(tempByte3)));
                if (Ws(d0) >= 0x100) SB(ref d0, 0xFF);
            }
            B(lateralTrackPosition, (byte)d0);
            if ((B(reverseDirectionFlag) & 0x80) != 0) SB(ref d0, (byte)d0 ^ 0xFF);
            if ((byte)d1 == 4) B(playerLateralPosition, (byte)d0);
            B(tempByte3, B(segmentOrientationAlternate));
            SW(ref d0, Frac(W(wheelCornerYFrontLeft + Ws(d1)) >> 3, B(tempByte3)) + W(lbW00D410));
            W(forwardPositionIntermediate, (ushort)d0);
            SB(ref d0, B(forwardPositionIntermediate) << 1);
            B(segmentDataIndexScaled, (byte)d0);
            if (Bs(d0) < 0 || Bs(d0) >= (sbyte)B(maxSegmentIndex)) SegmentTransition();
            a4 = GeometryAt(W(primaryGeometryOffset));
            a5 = GeometryAt(W(alternateGeometryOffset));
            int[] p = new int[4];
            if ((B(reverseDirectionFlag) & 0x80) != 0)
            {
                SB(ref d1, B(trackSegmentLimit) - B(segmentDataIndexScaled) - 4);
                for (var k = 3; k >= 0; k--) { p[k] = DataPoint(); SB(ref d1, (byte)d1 + 1); }
            }
            else
            {
                SB(ref d1, B(segmentDataIndexScaled));
                for (var k = 0; k < 4; k++) { p[k] = DataPoint(); SB(ref d1, (byte)d1 + 1); }
            }
            W(splineControlPoint1, p[0]); W(splineControlPoint2, p[1]); W(splineControlPoint3, p[2]); W(splineControlPoint4, p[3]);
            SB(ref d1, B(wheelDataOffset));
            WheelCoordinate();
            SB(ref d1, (byte)d1 - 2);
            if (Bs(d1) < 0) break;
        }
        return (L(wheelHeightFrontLeft), L(wheelHeightFrontRight), L(wheelHeightRear));
    }

    /// <summary>handleSegmentTransition ($5C484): a wheel past the piece's end reads the next (or last) piece.</summary>
    void SegmentTransition()
    {
        var f = (B(forwardPositionIntermediate) ^ B(reverseDirectionFlag)) & 0x80;
        bool startOfNext;
        if (f != 0) { Retreat(); LoadSegment(); startOfNext = (B(reverseDirectionFlag) & 0x80) != 0; }
        else { Advance(); LoadSegment(); startOfNext = (B(reverseDirectionFlag) & 0x80) == 0; }
        bool flip;
        if (startOfNext) { B(segmentDataIndexScaled, 0); flip = (B(forwardPositionIntermediate) & 0x80) != 0; }
        else { B(segmentDataIndexScaled, B(trackSegmentLimit) - 4); flip = (B(forwardPositionIntermediate) & 0x80) == 0; }
        if (!flip) return;
        B(segmentBlendParam, -B(segmentBlendParam)); if (B(segmentBlendParam) == 0) B(segmentBlendParam, 0xFF);
        B(lateralTrackPosition, -B(lateralTrackPosition)); if (B(lateralTrackPosition) == 0) B(lateralTrackPosition, 0xFF);
    }

    /// <summary>getTrackDataPoint ($5C0AA): height point d1 of the piece (a4 / a5 the two geometry tables).</summary>
    int DataPoint()
    {
        SB(ref d2, (byte)d1);
        int v;
        if ((B(geometryFormatFlag) & 0x80) != 0)
        {
            SB(ref d2, (byte)d2 & 0xFE);
            var a = ((byte)d1 & 1) != 0 ? a5 : a4;
            v = ((B(a + Ws(d2)) & 0x7F) << 8 | B(a + Ws(d2) + 1)) + W(((byte)d1 & 1) != 0 ? segmentBezierOffset2 : segmentBezierOffset1);
        }
        else
        {
            var odd = ((byte)d2 & 1) != 0;
            SB(ref d2, (byte)d2 >> 1);
            var b = B((odd ? a5 : a4) + Ws(d2));
            v = (((b << 1) & 0xE0) | ((b & 0x0F) << 8)) + W(odd ? segmentBezierOffset2 : segmentBezierOffset1);
            SW(ref d3, (b & 0x0F) << 8);
        }
        SW(ref d0, (short)v >> 5);
        return (ushort)d0;
    }

    /// <summary>updateCarWorldPositionCoordinate ($5C5F2): the height between the four points, the edge's lip, kept for
    /// wheel d1 (slow and level: the mean with the last).</summary>
    void WheelCoordinate()
    {
        Bilinear();
        SB(ref d1, (byte)d1 << 1);
        var was = B(wheelOutOfLateralBoundsFlag);
        B(wheelOutOfLateralBoundsFlag, was & 0x7F);
        if ((was & 0x80) != 0) LateralBounds();
        var at = wheelHeightFrontLeft + Ws(d1);
        var h = L(tempByte1);
        var slow = (sbyte)B(carSpeedMagnitude) < 0x0A;
        var pitch = (sbyte)B(cameraAngleX);
        if (pitch < 0) pitch = (sbyte)-pitch;
        if (slow && pitch <= 5) L(at, (int)(((long)(uint)L(at) + (uint)h) >> 1));
        else L(at, h);
        SB(ref d1, (byte)d1 >> 1);
    }

    /// <summary>calculateBilinearTrackInterpolation ($5C554): across between the point pairs, along between them (x 256).</summary>
    void Bilinear()
    {
        int lat = B(lateralTrackPosition);
        int p1 = W(splineControlPoint1), p2 = W(splineControlPoint2), p3 = W(splineControlPoint3), p4 = W(splineControlPoint4);
        var a = (short)(p2 - p1) * lat + (p1 << 8);
        var b = (short)(p4 - p3) * lat + (p3 << 8);
        int blend = B(segmentBlendParam);
        var diff = b - a;
        static int Mul(int v, int m)
        {
            if ((short)v < 0) { var r = (uint)(ushort)(-(short)v) * (uint)m; r &= 0xFFFFFF00; return -(int)r; }
            return (int)((uint)(ushort)v * (uint)m);
        }
        int r;
        if (Math.Abs(diff) >= 0x8000) r = Mul(diff >> 3, blend) << 3;
        else r = Mul(diff, blend);
        L(tempByte1, (r >> 8) + a);
    }

    /// <summary>checkLateralBoundsAndBounce ($5C808): a wheel up to $30 past the edge on the lip (the edge's height less $100
    /// less 16 a unit past), further or below $1000 the floor $1000.</summary>
    void LateralBounds()
    {
        int v = W(lateralPositionOutOfBounds), d;
        if (v < 0) d = -v;
        else { d = (short)(0x180 - v); if (d < 0) d = -d; }
        if ((short)d <= 0x30)
        {
            var h = L(tempByte1) - ((d & 0xFF) << 4) - 0x100;
            if (h >= 0x1000)
            {
                L(tempByte1, h);
                var s = (B(reverseDirectionFlag) ^ B(lateralPositionOutOfBounds)) & 0x80;
                B(wheelBouncePhaseAccumulator, s != 0 ? 0x80 : 0x40);
                return;
            }
        }
        L(tempByte1, 0x1000);
        B(renderingOrderMode, (B(renderingOrderMode) >> 1) | 0x80);
    }
}
