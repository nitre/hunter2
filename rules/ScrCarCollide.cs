namespace Hunter.Rules;

/// <summary>STUNTIN': the original's touch between the cars, ported from the original program: calculatePlayerDistance
/// ($6076C), handleOpponentPositioning's slipstream and touch test ($636C0), calculateOpponentCollisionForces ($63CE4) and
/// applyOpponentCollisionForces (in the suspension step). The original's opponent rode the track; here each car runs it
/// with the other car's place, speed and height as that opponent's (SetOpponent).</summary>
public sealed partial class ScrCar
{
    public const int
        opponentSubSegmentProgress = 0x1BB0C, aiEnabled = 0x1BB44, collisionActiveFlag = 0x1BB46,
        opponentCollisionTimer = 0x1BBC3, opponentCollisionActive = 0x1BBEB, opponentLateralPosition = 0x1BBED,
        aiCurrentSpeed = 0x1BBEE, playerOpponentLateralDistance = 0x1BBF7, opponentDistance = 0x1BC38,
        playerDistanceDifference = 0x1BC3E, lateralOpponentCollisionForce = 0x1BD54, verticalOpponentCollisionForce = 0x1BD56,
        forwardOpponentCollisionForce = 0x1BD58, opponentWheelPositions = 0x1BD66, segmentWorldPositions = 0x1C7E0,
        startWorldPosition = 0x1CA1E, opponentTargetLateralPosition = 0x1BBA7;

    /// <summary>The other car as the original's opponent: its piece, how far along it, its place across, its speed, its height.</summary>
    public void SetOpponent(ScrCar o)
    {
        B(opponentSegmentIndex, o.B(playerSegmentIndex));
        W(opponentSubSegmentProgress, o.W(trackProgressionByte));
        B(opponentLateralPosition, o.B(playerLateralPosition));
        W(aiCurrentSpeed, o.W(carLocalVelocityZ));
        W(opponentWheelPositions, o.W(carHeightPosition));
        B(aiEnabled, 0x80);
    }

    /// <summary>calculatePlayerDistance ($6076C): the distance to the other car round the lap the short way, and whether it is
    /// ahead (opponentAheadFlag bit 7 clear: ahead).</summary>
    public void PlayerDistance()
    {
        var d0 = (short)((W(opponentSubSegmentProgress) - W(trackProgressionByte)) >> 3);
        int d1 = (B(opponentSegmentIndex) << 1) & 0xFF, d2 = (B(playerSegmentIndex) << 1) & 0xFF;
        d0 = (short)(d0 + (short)(W(segmentWorldPositions + d1) - W(segmentWorldPositions + d2)));
        var d5 = (ushort)d0;
        W(playerDistanceDifference, d0);
        var a = d0 < 0 ? (ushort)-d0 : (ushort)d0;
        var d4 = (ushort)(W(startWorldPosition) - a);
        if (d4 >= a) { d4 = a; d5 ^= 0x8000; }
        W(opponentDistance, d4);
        d5 ^= 0x8000;
        B(opponentAheadFlag, d5 >> 8);
    }

    /// <summary>handleOpponentPositioning's first part ($636C0): the slipstream (draftingTimer) and, close and level with the
    /// other car on the road, the touch (calculateOpponentCollisionForces); else the touch's state cleared.</summary>
    public void OpponentTouch()
    {
        var d2 = 0;
        B(draftingTimer, 0);
        int d0 = B(opponentLateralPosition);
        B(opponentTargetLateralPosition, d0);
        var diff = d0 - B(playerLateralPosition);
        if (diff < 0) { diff = -diff; d2 = 0xFF; }
        B(playerOpponentLateralDistance, diff);
        B(tempWord1, d2);
        if (B(opponentDistance) != 0) return;                              // far: no slipstream, no touch
        int near = B(opponentDistance + 1);
        if (near < 0x40 && (Neg(B(opponentAheadFlag)) || B(playerOpponentLateralDistance) < 0x32))
            B(draftingTimer, B(draftingTimer) - 1);
        var touch = near < 0x10 && B(playerOpponentLateralDistance) < 0x32
                    && (B(lateralRoadPosition) < 1 || B(lateralRoadPosition) == 1 && B(lateralRoadPosition + 1) < 0x80);
        if (touch) { OpponentCollision(); return; }
        B(opponentCollisionTimer, 0);
        B(opponentCollisionActive, 0);
    }

    /// <summary>calculateOpponentCollisionForces ($63CE4): the kicks - up / down by the height between them (for a few
    /// passes), sideways away from the other ($08), along by half the speed difference ($03 more) - and damage to each corner.</summary>
    void OpponentCollision()
    {
        if (B(raceStartComplete) == 0) return;
        if (B(aiEnabled) == 0 || B(wheelMovementActive) == 0)
        {
            var d0 = (short)(W(carHeightPosition) - W(opponentWheelPositions));
            var d4 = d0;
            d0 = (short)(d0 + 0x28);
            if (d0 < 0) d0 = (short)-d0;
            if (d0 >= 0xC0) { B(opponentCollisionTimer, 3); return; }
            if (B(opponentCollisionTimer) != 0)
            {
                B(opponentCollisionTimer, B(opponentCollisionTimer) - 1);
                var d3 = (short)(0x100 - d0);
                if (d4 < 0) d3 = (short)-d3;
                W(verticalOpponentCollisionForce, d3 << 4);
            }
        }
        if (B(playerOpponentLateralDistance) < 0x2D && B(opponentDistance + 1) < 8)
            B(lateralOpponentCollisionForce, Neg(B(tempWord1)) ? 0x08 : 0xF8);
        if (!Neg(B(opponentCollisionActive)))
        {
            var d3 = 3;
            var d0 = B(crashRecoveryTimer) != 0 ? 0 : W(aiCurrentSpeed);
            d0 = (short)(d0 - W(carLocalVelocityZ));
            if (d0 < 0) d3 = -3;
            W(forwardOpponentCollisionForce, (short)((d0 >> 1) + d3));
        }
        B(collisionActiveFlag, 0x80);
        B(opponentCollisionActive, 0x80);
        int Abs(int a) { var w = W(a); return w < 0 ? -w : w; }
        var dmg = ((0x200 + Abs(lateralOpponentCollisionForce) + Abs(verticalOpponentCollisionForce) + Abs(forwardOpponentCollisionForce)) & 0xFFFF) >> 8;
        for (var k = 2; k >= 0; k--) B(accumulatedForceFrontLeft + k, Math.Min(0xFF, B(accumulatedForceFrontLeft + k) + (dmg & 0xFF)));
        B(damageAccumulationActive, 0x80);
    }

    /// <summary>applyOpponentCollisionForces (in the suspension step): the touch's kicks to the roll, pitch and yaw springs,
    /// once, with the crunch (sample 2, then 5 passes quiet).</summary>
    void ApplyCollision()
    {
        if (B(collisionSoundCooldown) != 0) B(collisionSoundCooldown, B(collisionSoundCooldown) - 1);
        if (B(collisionActiveFlag) == 0) return;
        B(collisionActiveFlag, 0);
        var s = W(aiCurrentSpeed) - W(forwardOpponentCollisionForce);
        W(aiCurrentSpeed, s < 0 ? 0 : s);
        W(rollSpringAngle, W(rollSpringAngle) + W(lateralOpponentCollisionForce));
        W(pitchSpringAngle, W(pitchSpringAngle) + W(verticalOpponentCollisionForce));
        W(yawSpringAngle, W(yawSpringAngle) + W(forwardOpponentCollisionForce));
        W(lateralOpponentCollisionForce, 0); W(verticalOpponentCollisionForce, 0); W(forwardOpponentCollisionForce, 0);
        if (B(collisionSoundCooldown) != 0) return;
        PlaySample(2);
        B(collisionSoundCooldown, 5);
    }
}
