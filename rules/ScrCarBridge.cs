namespace Hunter.Rules;

/// <summary>STUNTIN': the original's animateDrawBridge ($5A794), ported from the original program: on Draw Bridge (track 5),
/// once a pass after the frame, the bridge's phase steps (unless a car is on it) and its leaves' height points are written
/// into the bridge piece's geometry - the road the wheel lookup reads.</summary>
public sealed partial class ScrCar
{
    public const int
        opponentSegmentIndex = 0x1BB1D, drawBridgeActive = 0x1BB3F, drawBridgeAnimationPhase = 0x1BBB0,
        drawBridgeUpdateFlag = 0x1BB71, rawTrackDataOffset = 0x1BCC0, drawBridgeHeightValues = 0x1C982,
        drawBridgeAngle = 0x1C985, drawBridgeAngleTable = 0x5A996, drawBridgeHeightTable = 0x5A9A6;

    /// <summary>The bridge piece's geometry: where the leaves' height points are written (segmentGeometryOffsetTable $BE).</summary>
    public int BridgeGeometry => GeometryAt(W(segmentGeometryOffsetTable + 0xBE));

    /// <summary>One pass of the bridge (the other car's piece in opponentSegmentIndex).</summary>
    public void AnimateDrawBridge()
    {
        if (B(currentTrackID) != 5) return;
        int ps = B(playerSegmentIndex), os = B(opponentSegmentIndex);
        var held = ps < 0x38 && ps >= 0x33 || os < 0x38 && (os >= 0x33 || B(drawBridgeActive) != 0 && os >= 0x30);
        if (held)
        {
            B(drawBridgeActive, 0x0C);
            BridgeHeights(0x0C + B(drawBridgeAnimationPhase));
            return;
        }
        if (!Neg(B(frameThrottleFlag))) B(drawBridgeAnimationPhase, B(drawBridgeAnimationPhase) + 1);
        W(segmentProgressDistance, 0);
        B(drawBridgeActive, 0);
        var d0 = (B(drawBridgeAnimationPhase) & 0x1F) - 0x10;
        if (d0 < 0) d0 = ~d0;
        B(drawBridgeAngle, B(drawBridgeAngleTable + d0)); B(drawBridgeAngle + 1, B(drawBridgeAngleTable + d0));
        W(tempWord1, (d0 + 4) << 5);
        W(rawTrackDataOffset, W(segmentGeometryOffsetTable + 0xBE));
        var a0 = GeometryAt(W(rawTrackDataOffset));
        var k = 2;
        for (var n = 0x0F; n > 0; n--)
        {
            W(segmentProgressDistance, W(segmentProgressDistance) + W(tempWord1));
            do
            {
                var hi = B(segmentProgressDistance);
                if (k == 0x20) hi |= 0x80;
                B(a0 + k, hi); k++;
                B(a0 + k, B(segmentProgressDistance + 1)); k++;
                B(tempByte4, k);
                var m = 0x48 - k;
                B(a0 + m, hi); m++;
                B(a0 + m, B(segmentProgressDistance + 1));
                k = B(tempByte4);
            } while (k == 0x12);
        }
        B(drawBridgeUpdateFlag, 0x80);                                       // (the drawn bridge's own pointer words: ours draws it)
        if (os == 0x2F) BridgeHeights(B(drawBridgeAnimationPhase));
    }

    /// <summary>$5A966: the three height values the bridge's ends take at a phase.</summary>
    void BridgeHeights(int phase)
    {
        var t = B(drawBridgeHeightTable + ((phase & 0x1F) >> 1));
        var v = 0xC6;
        for (var k = 0; k < 3; k++) { v = (v + t) & 0xFF; B(drawBridgeHeightValues + k, v); }
    }

    /// <summary>The bridge's state another car's memory takes from this one (the geometry the leaves are written to and the
    /// bridge's own bytes).</summary>
    public void CopyBridgeTo(ScrCar other)
    {
        var a0 = BridgeGeometry;
        Array.Copy(Ram, a0, other.Ram, a0, 0x4A);
        foreach (var a in new[] { drawBridgeActive, drawBridgeAnimationPhase, drawBridgeUpdateFlag })
            other.Ram[a] = Ram[a];
        Array.Copy(Ram, drawBridgeHeightValues, other.Ram, drawBridgeHeightValues, 5);
    }
}
