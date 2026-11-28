namespace Hunter.Rules;

/// <summary>STUNTIN': the original's restart and race start, ported from the original program: initializePlayerAtSegment
/// with applyCameraLateralOffset, handleRaceStartCountdown (the crane: lift, roll, hold, drop) with adjustCarHeightToTrack
/// and updateRaceStartRoll, the restart timer of continueGameLoop, updateFrameThrottlingAndTimers' throttle flag and
/// generateRandomNumber.</summary>
public sealed partial class ScrCar
{
    public const int
        raceStartRoll = 0x1BC00, trackSurfaceHeight = 0x1BC60, raceStartComplete = 0x1BBC4,
        crashRecoveryTimer = 0x1BB74, gameMessageActiveFlag = 0x1BB8E, selectedTrackInDivision = 0x1CA22,
        chainVerticalPosition = 0x1BBEA, chainLiftVelocity = 0x1BBE9,
        trackFeatureCount = 0x1CA2F, trackFeatureData = 0x1C8E8, geometryParameterTable = 0x1F0C2,
        globalFrameCounter = 0x1BBC9, frameThrottleAccumulator = 0x1BBCF, blinkCountdownTimer = 0x1BB6C,
        randomState = 0x625A2;

    /// <summary>initializePlayerAtSegment ($605B6): the car on piece seg - stepped back over joins, corners and the track's
    /// feature pieces - at its cell's middle, pointing along it, its memory cleared, one physics pass, then raised for the
    /// crane (raceStartTimer $F0, or $E6 once the race has started).</summary>
    public void InitializeAt(int seg)
    {
        SB(ref d1, seg);
        while (true)
        {
            B(currentSegmentIndex, (byte)d1); B(playerSegmentIndex, (byte)d1);
            var type = B(trackSegmentPropertiesTable + Ws(d1)) & 0x0F;
            SB(ref d2, type);
            var back = (sbyte)B(geometryParameterTable + type) < 0;
            if (!back && B(trackFeatureCount) is var n and > 0)
                for (var k = n - 1; k >= 0 && !back; k--) back = B(trackFeatureData + k) == (byte)d1;
            if (!back) break;
            Retreat();
        }
        for (var a = rotatedCameraX; a < 0x1BD5E; a++) Ram[a] = 0;
        B(raceStartTimer, 0xF0);
        LoadSegment();
        int c = B(trackSegmentCoordinates + Ws(d1));
        B(gridLookupX, c & 0x0F); B(gridLookupY, c >> 4);
        W(cameraWorldX + 2, 0); W(cameraWorldZ + 2, 0);
        W(cameraWorldX, ((c & 0x0F) << 7) + 0x40);
        W(cameraWorldZ, ((c >> 4) << 7) + 0x40);
        B(cameraWorldY, 4);
        var type2 = B(trackGeometryTypeIndex);
        SB(ref d1, type2 == 4 || type2 == 0x0A ? 0x20 : 0);
        B(cameraAngleY, (B(segmentSlopeFlags) ^ B(reverseDirectionFlag)) + (byte)d1);
        CameraTransform();
        ApplySegmentGeometry();
        RoadBoundaries();
        Physics(car => car.WheelHeights());
        W(cameraWorldY + 2, 0); W(cameraWorldY, 0x10);
        var rear = L(wheelHeightRear);
        if (B(raceStartComplete) != 0)
        {
            L(cameraWorldY, (rear << 9) + 0x180000);
            B(raceStartTimer, 0xE6);
        }
        W(trackSurfaceHeight, (int)((uint)rear >> 2));
        LateralOffset();
        for (var k = 8; k >= 0; k -= 4) { L(wheelHeightFrontLeft + k, 0x1000); L(trackSurfaceFrontLeft + k, 0x1000); }
        L(previousSuspensionFrontLeft, 0); W(previousSuspensionRear, 0);
        CameraTransform();
        B(chainVerticalPosition, 0xB0); B(chainLiftVelocity, 8);
    }

    /// <summary>applyCameraLateralOffset ($5B506): the car moved $A0 / 256 of the rotation's side row to the side it went
    /// off (trackSideIndicator).</summary>
    void LateralOffset()
    {
        foreach (var (at, row, neg) in new[] { (cameraWorldZ, 4, true), (cameraWorldX, 6, false) })
        {
            var v = W(M + row);
            if (neg) v = (short)-v;
            B(trackSideIndicatorCopy, B(trackSideIndicator));
            B(tempByte3, 0xA0);
            var s = (sbyte)B(trackSideIndicatorCopy) < 0 ? -0xA0 : 0xA0;
            var m = (short)(s << 7);
            var p = (int)((long)v * m << 1) >> 16;
            L(at, L(at) + ((short)p << 6));
        }
    }

    /// <summary>handleRaceStartCountdown ($5B32E, from the suspension step): the crane's lift ($F0-$E6: the roll set), the
    /// raise ($E5, $E4: to the road, the roll levelled, then the hold's random length), the hold (counting down), the drop.</summary>
    void RaceStartCountdown()
    {
        int t = B(raceStartTimer);
        if (t == 0) return;
        if (t >= 0xE6)
        {
            B(raceStartRoll, (sbyte)B(trackSideIndicator) >= 0 ? 0x2C : 0xD4); B(raceStartRoll + 1, 0);
            B(raceStartTimer, t - 1);
            return;
        }
        if (t == 0xE5)
        {
            StartRoll(0);
            if ((sbyte)AdjustHeight(3) >= 0) B(raceStartTimer, t - 1);
            return;
        }
        if (t == 0xE4)
        {
            AdjustHeight(4);
            if (!StartRoll(0xFF)) return;
            var r = (Random() & 0x1F) + 0xA0;
            if ((sbyte)B(selectedTrackInDivision) >= 0) r = 0x8C;
            B(raceStartTimer, r);
            if (B(crashRecoveryTimer) != 0) B(crashRecoveryTimer, 0x32);
            return;                                                          // setMessageParameters: the message (ours)
        }
        StartRoll(0);
        AdjustHeight(2);
        if (!Neg(B(frameThrottleFlag)))
        {
            B(raceStartTimer, B(raceStartTimer) - 1);
            if (B(raceStartTimer) == 0) B(raceStartTimer, 1);
        }
        if (B(raceStartComplete) == 0) { if ((sbyte)B(raceStartTimer) < 0) return; }
        else if (B(raceActiveFlag) != 0) return;
        B(raceStartTimer, 0); B(offTrackStateFlags, 0); B(gameMessageActiveFlag, 0);
        B(raceStartComplete, 0x80);
    }

    /// <summary>adjustCarHeightToTrack ($5B472): the pitch spring pulled toward n above the road; the height left (bytes) + 2.</summary>
    int AdjustHeight(int n)
    {
        var d3 = (short)(W(carHeightPosition) - W(trackSurfaceHeight) - (n << 8));
        var d0 = (short)((d3 >> 3) - 0x100);
        if (d0 < 0 && (ushort)d0 < 0xFE00) d0 = unchecked((short)0xFE00);
        W(pitchSpringAngle, W(pitchSpringAngle) - d0);
        return (byte)(((ushort)d3 >> 8) + 2);
    }

    /// <summary>updateRaceStartRoll ($5B4A8): the start roll turned toward its end ($10, or $F0 on the left side) by d0
    /// x $EE / 256, the car's roll that less its place across; true when the roll is at its end.</summary>
    bool StartRoll(int d0)
    {
        var end = 0x10;
        if ((sbyte)B(trackSideIndicator) < 0) { d0 = (byte)-d0; end = 0xF0; }
        var step = (int)((long)(short)((byte)d0 << 8) * 0xEE) >> 8;
        var side = (short)(W(offsetFromRoadCenter) << 5);
        if (B(raceStartRoll) != end) W(raceStartRoll, W(raceStartRoll) + step);
        W(cameraAngleZ, W(raceStartRoll) - side);
        W(targetPitchRate, 0);
        return B(raceStartRoll) == end;
    }

    /// <summary>generateRandomNumber ($62574): the original's shift register (5 bytes at $625A2).</summary>
    public int Random()
    {
        var d0 = (ushort)W(randomState + 2) >> 4;
        var d3 = (ushort)W(randomState) >> 1;
        d0 = (d0 & 0xFF00) | ((d0 ^ d3) & 0xFF);
        L(randomState, (L(randomState) << 8) | B(randomState + 4));
        B(randomState + 4, d0);
        return (byte)d0;
    }

    /// <summary>updateFrameThrottlingAndTimers' throttle ($5DB34): $EE added each pass; no carry = a skipped step ($FF).</summary>
    public void FrameThrottle()
    {
        B(globalFrameCounter, B(globalFrameCounter) + 1);
        var sum = B(frameThrottleAccumulator) + 0xEE;
        B(frameThrottleAccumulator, sum);
        B(frameThrottleFlag, sum > 0xFF ? 0 : 0xFF);
    }

    /// <summary>continueGameLoop's restart ($5D5A0): off the track with the wheels on the ground, the restart timer runs
    /// (low down it marks raceCompletionCheckFlag); true when it has run out (and no wreck is blinking): restart on
    /// lastValidSegmentIndex.</summary>
    public bool RestartDue()
    {
        if (B(raceStartTimer) != 0 || !Neg(B(offTrackStateFlags)) || B(wheelMovementActive) == 0) return false;
        var h = (sbyte)B(carHeightPosition);
        if (h < 2) B(raceCompletionCheckFlag, B(offTrackStateFlags));
        B(restartTimerCountdown, B(restartTimerCountdown) - 1);
        if ((sbyte)B(restartTimerCountdown) >= 0) return false;
        B(restartTimerCountdown, B(restartTimerCountdown) + 1);
        return B(blinkCountdownTimer) == 0;
    }

    /// <summary>initializeGameSystemsAndMainLoop's restart ($5D402): on the piece, the frame and a physics pass as the original
    /// runs them before play goes on.</summary>
    public void RestartAt(int seg)
    {
        InitializeAt(seg);
        RoadFrame();
        FrameThrottle();
        Physics(car => car.WheelHeights());
        RoadFrame();
        FrameThrottle();
    }
}
