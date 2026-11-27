namespace Hunter.Rules;

/// <summary>STUNTIN': Stunt Car Racer's car physics, ported routine by routine from the ORIGINAL program on the
/// disk (the bytes the emulator runs - not the Framerate Unleashed source, which changes a few lines for its frame rate):
/// updateGamePhysics ($6185C) and all it calls, and the throttle / steer / boost part of processPlayerInput ($5D8A2) and
/// updateTurboBoostSystem ($608A4). The state is the original's own memory: every variable a byte, word or long at its
/// own address in Ram, so its byte/word aliasing is exact (the engine push word $1BD2A is two named bytes; a byte clear
/// of a word variable leaves its low byte). Proven equal to the original in the emulator, word for word, on a ramp and
/// drop (ScrCarTests).
/// Not ported, inputs instead: updateWheelHeightsFromTrack (the road's height under each contact point, wheel units =
/// height value x 256, the callback in Physics) and calculateSteeringResponse's track lookup (loadTrackSegmentConfiguration:
/// segmentTargetAngle, segmentSteeringFlags, segmentBaseSteeringOffset, reverseDirectionFlag, trackDirectionInversionFlag
/// - set them before a pass). Left out: the race start / crane (raceStartTimer must be 0), the opponent's collision push
/// (collisionActiveFlag 0), sounds (only their counters run), the boost fuel display (FuelTick is raised where it ran).
/// The copy protection in the steering ($611E8) is taken as passed, as in the running game.</summary>
public sealed class ScrCar
{
    public readonly byte[] Ram = new byte[0x64000];

    // ---------------------------------------------------------------- the original's addresses (its own names)
    public const int
        cameraAngleX = 0x1BCE4, cameraAngleY = 0x1BCE6, cameraAngleZ = 0x1BCE8, segmentTargetAngle = 0x1BD5A,
        M = 0x1C230,                                                                           // lbL00DB30: the rotation table
        wheelCornerXRearCenter = 0x1BD06, wheelCornerYRearCenter = 0x1BD0C, wheelCornerXFrontLeft = 0x1BD02,
        wheelCornerYFrontLeft = 0x1BD08, wheelCornerXFrontRight = 0x1BD04, wheelCornerYFrontRight = 0x1BD0A,
        wheelHeightFrontLeft = 0x1BCA4, wheelHeightFrontRight = 0x1BCA8, wheelHeightRear = 0x1BCAC,
        trackSurfaceFrontLeft = 0x1BC94, trackSurfaceFrontRight = 0x1BC98, trackSurfaceRear = 0x1BC9C,
        cameraWorldX = 0x1BCD8, cameraWorldY = 0x1BCDC, cameraWorldZ = 0x1BCE0, tempWord1 = 0x1BBF6,
        worldXSpeed = 0x1BCEA, worldYSpeed = 0x1BCEC, worldZSpeed = 0x1BCEE,
        carLocalVelocityX = 0x1BD2C, carLocalVelocityY = 0x1BD2E, carLocalVelocityZ = 0x1BD30, carSpeedMagnitude = 0x1BD5C,
        wheelMovementActive = 0x1BB7E, viewOffsetX = 0x1BC62, baseRollAngle = 0x1BD10, additionalYawOffset = 0x1BD12,
        baseTargetRoll = 0x1BD0E, hardImpactCount = 0x1BB7D, maxCompressionVelocity = 0x1BC3A,
        suspensionTravelFrontLeft = 0x1BCB0, suspensionTravelFrontRight = 0x1BCB4, suspensionTravelRear = 0x1BCB8,
        trackOffsetAdjustment = 0x1BCA0, clampedSuspensionFrontLeft = 0x1BD14, clampedSuspensionFrontRight = 0x1BD16,
        clampedSuspensionRear = 0x1BD18, previousSuspensionFrontLeft = 0x1BD1A, previousSuspensionFrontRight = 0x1BD1C,
        previousSuspensionRear = 0x1BD1E, suspensionVelocityFrontLeft = 0x1BD20, suspensionVelocityFrontRight = 0x1BD22,
        suspensionVelocityRear = 0x1BD24, trackWidthMultiplier = 0x1BB01, frameThrottleFlag = 0x1BBCD,
        highCompressionFrameCount = 0x1BB56, damageFrameLimit = 0x63CE2, accumulatedForceFrontLeft = 0x1BB4F,
        accumulatedForceFrontRight = 0x1BB50, accumulatedForceRear = 0x1BB51, damageAccumulationActive = 0x1BB54,
        averageWheelVelocity = 0x1BD38, targetRollRate = 0x1BD28, targetPitchRate = 0x1BD26, raceStartTimer = 0x1BBDF,
        currentTrackID = 0x1CA33, rotationSpeedX = 0x1BCF0, rotationSpeedY = 0x1BCF2, rotationSpeedZ = 0x1BCF4,
        lbW00D646 = 0x1BD46, yawSpringAngle = 0x1BD44, impactSoundCooldownTimer = 0x620B6, sampleImpactVolume = 0x1B705,
        collisionSoundCooldown = 0x63EE0, pitchReferenceZero = 0x1BD4A, pitchDisplacement = 0x1BD4C,
        attenuatedDisplacementValue = 0x1BB2D, rawDisplacementValue = 0x1BB2B, tempAttenuatedValue = 0x1BB2C,
        yawSpringComponent = 0x1BD52, rollDisplacement = 0x1BD48, tempByte3 = 0x1BB1A, tempByte4 = 0x1BB1B,
        pitchSpringComponent = 0x1BD50, rollSpringComponent = 0x1BD4E, trackSideIndicatorCopy = 0x1BBBB,
        rollSpringAngle = 0x1BD40, pitchSpringAngle = 0x1BD42, adjustedPitchAngle = 0x1BD34,
        gasOutputAccumulatorValue = 0x1BD2A, gasOutputIntensityValue = 0x1BD2B, adjustedYawAngle = 0x1BD36,
        adjustedRollAngle = 0x1BD32, rollTransitionFlag = 0x1BBC1, reverseDirectionFlag = 0x1BC32,
        segmentSteeringFlags = 0x1BB4D, trackDirectionInversionFlag = 0x1BC44, segmentProgressDistance = 0x1BC2A,
        steeringScaleFactor = 0x1BC3C, trackDirectionMultiplier = 0x1BB5D, steeringInputDirection = 0x1BBC6,
        segmentBaseSteeringOffset = 0x1BBD4, angularAccelerationX = 0x1BCFC, angularAccelerationY = 0x1BCFE,
        angularAccelerationZ = 0x1BD00, worldAccelerationX = 0x1BCF6, worldAccelerationY = 0x1BCF8,
        worldAccelerationZ = 0x1BCFA, offTrackStateFlags = 0x1BB9C, postWreckStateFlag = 0x1BCA2, draftingTimer = 0x1BBC7,
        opponentAheadFlag = 0x1BBB8, transformedAngularVelocityX = 0x1BD3A, transformedAngularVelocityY = 0x1BD3C,
        transformedAngularVelocityZ = 0x1BD3E, raceCompletionCheckFlag = 0x1BB75, renderingOrderMode = 0x1BB9A,
        lbB00D4AB = 0x1BBAB, viewportCenterY = 0x1BC42, inputStateFlags = 0x1BB47, gasInputAccumulatorValue = 0x1BAFB,
        gasInputIntensityValue = 0x1BAFA, accelerationStateFlag = 0x1BBA8, raceActiveFlag = 0x1BB70,
        boostFuelLevel = 0x1CA20, fuelConsumptionTimer = 0x1BB3D, fuelConsumptionRate = 0x1BAFE, boostActiveFlag = 0x1BB62,
        sampleParameterTable = 0x1B6CA, enginePitchDelta = 0x1BBF4, enginePitchAccumulator = 0x1BC34,
        majorImpactCooldownTimer = 0x1BB73, holeRenderingPosition = 0x1C9CF;

    // the program's own small tables ($1CA42 quarter wave, $1EC46, $1EECA, $6125A, $61AD4)
    static readonly ushort[] Quarter = [65535, 65534, 65531, 65524, 65516, 65505, 65491, 65475, 65457, 65436, 65412, 65386, 65358, 65327, 65294, 65258, 65220, 65179, 65136, 65091, 65043, 64992, 64939, 64884, 64826, 64766, 64703, 64638, 64571, 64501, 64428, 64353, 64276, 64197, 64115, 64030, 63943, 63854, 63762, 63668, 63571, 63473, 63371, 63268, 63162, 63053, 62942, 62829, 62714, 62596, 62475, 62353, 62228, 62100, 61971, 61839, 61705, 61568, 61429, 61288, 61144, 60998, 60850, 60700, 60547, 60392, 60235, 60075, 59913, 59749, 59583, 59414, 59243, 59070, 58895, 58718, 58538, 58356, 58172, 57986, 57797, 57606, 57414, 57219, 57022, 56822, 56621, 56417, 56212, 56004, 55794, 55582, 55368, 55152, 54933, 54713, 54491, 54266, 54040, 53811, 53581, 53348, 53114, 52877, 52639, 52398, 52155, 51911, 51665, 51416, 51166, 50914, 50660, 50403, 50146, 49886, 49624, 49360, 49095, 48828, 48558, 48288, 48015, 47740, 47464, 47186, 46906, 46624, 46340, 46055, 45768, 45480, 45189, 44897, 44603, 44308, 44011, 43712, 43412, 43110, 42806, 42501, 42194, 41885, 41575, 41263, 40950, 40636, 40319, 40002, 39682, 39362, 39039, 38716, 38390, 38064, 37736, 37406, 37075, 36743, 36409, 36074, 35738, 35400, 35061, 34721, 34379, 34036, 33692, 33346, 32999, 32651, 32302, 31952, 31600, 31247, 30893, 30538, 30181, 29824, 29465, 29105, 28745, 28383, 28020, 27656, 27291, 26925, 26557, 26189, 25820, 25450, 25079, 24707, 24334, 23960, 23586, 23210, 22833, 22456, 22078, 21699, 21319, 20938, 20557, 20175, 19792, 19408, 19024, 18638, 18253, 17866, 17479, 17091, 16702, 16313, 15923, 15533, 15142, 14751, 14359, 13966, 13573, 13179, 12785, 12390, 11995, 11600, 11204, 10807, 10410, 10013, 9616, 9218, 8819, 8421, 8022, 7623, 7223, 6823, 6423, 6023, 5622, 5222, 4821, 4420, 4018, 3617, 3215, 2814, 2412, 2010, 1608, 1206, 804, 402, 0, 0];
    static readonly byte[] TransformIndex = [0x16, 0x14, 0x02, 0x18, 0x0F, 0x04, 0x17, 0x15, 0x03, 0x16, 0x18, 0x17, 0x14, 0x0F, 0x15, 0x02, 0x04, 0x03, 0x11, 0x10, 0x12, 0x11];
    static readonly byte[] Attenuation = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE, 0xFE, 0xFE, 0xFE, 0xFD, 0xFD, 0xFD, 0xFD, 0xFC, 0xFC, 0xFB, 0xFB, 0xFB, 0xFA, 0xFA, 0xF9, 0xF9, 0xF8, 0xF8, 0xF7, 0xF7, 0xF6, 0xF6, 0xF5, 0xF4, 0xF4, 0xF3, 0xF3, 0xF2, 0xF1, 0xF0, 0xF0, 0xEF, 0xEE, 0xED, 0xEC, 0xEC, 0xEB, 0xEA, 0xE9, 0xE8, 0xE7, 0xE6, 0xE5, 0xE4, 0xE3, 0xE2, 0xE1, 0xE0, 0xDF, 0xDE, 0xDD, 0xDB, 0xDA, 0xD9, 0xD8, 0xD6, 0xD5, 0xD4, 0xD2, 0xD1, 0xCF, 0xCE, 0xCC, 0xCB, 0xC9, 0xC8, 0xC6, 0xC5, 0xC3, 0xC1, 0xBF, 0xBE, 0xBC, 0xBA, 0xB8, 0xB6, 0xB4, 0xB2, 0xB0, 0xAE, 0xAC, 0xA9, 0xA7, 0xA5, 0xA2, 0xA0, 0x9D, 0x9B, 0x98, 0x95, 0x92, 0x8F, 0x8C, 0x89, 0x86, 0x83, 0x7F, 0x7C, 0x78, 0x74, 0x70, 0x6C, 0x68, 0x63, 0x5E, 0x59, 0x53, 0x4D, 0x47, 0x3F, 0x37, 0x2D, 0x20];
    static readonly short[] SteeringOffset = [0, 217, -217];
    static readonly short[] AngleLimits = [11264, 2560, -11520, -2816];
    const int Step = 0xEE;                                                                   // TIMESTEP_FACTOR, the original's

    // ---------------------------------------------------------------- memory (big-endian, as the 68000)
    public byte B(int a) => Ram[a];
    public void B(int a, int v) => Ram[a] = (byte)v;
    public short W(int a) => (short)(Ram[a] << 8 | Ram[a + 1]);
    public void W(int a, int v) { Ram[a] = (byte)(v >> 8); Ram[a + 1] = (byte)v; }
    public int L(int a) => Ram[a] << 24 | Ram[a + 1] << 16 | Ram[a + 2] << 8 | Ram[a + 3];
    public void L(int a, int v) { W(a, v >> 16); W(a + 2, v); }
    static bool Neg(int b) => (b & 0x80) != 0;                                               // a byte's sign
    static short Hi(int a, int b) => (short)((a * b) << 1 >> 16);                            // muls, asl.l #1, swap
    short Mul(int v, int idx) => Hi((short)v, W(M + idx * 2));                               // matrixMultiply ($61344)
    static short Scale(int v) => (short)((short)v * Step >> 8);                              // muls by $EE, asr.l #8

    /// <summary>calculateSine / calculateCosine ($64D08 / $64D10).</summary>
    static short Sine(int angle, bool cos)
    {
        int d0 = angle & 0xFFFF, d5 = cos ? 0x4000 : 0;
        int d3 = d0 & 0x3FFF;
        if (((d0 & 0x4000) ^ d5) == 0) d3 = ((d3 ^ 0x3FFF) + 1) & 0xFFFF;
        d3 = (d3 >> 5 | d3 << 11) & 0xFFFF;                                                  // ror.w #5
        int i = (d3 & 0x3FE) >> 1;
        int d6 = (Quarter[i] - Quarter[i + 1]) & 0xFFFF;
        d3 = (d3 >> 1 | d3 << 15) & 0xFC00;                                                  // ror.w #1, andi
        d6 = (int)((uint)d3 * (uint)d6 >> 16);                                               // mulu, swap
        int d7 = ((Quarter[i] - d6) & 0xFFFF) >> 1;
        if ((short)(d0 ^ ((d0 & d5) << 1)) < 0) d7 = -d7;
        return (short)d7;
    }

    // ---------------------------------------------------------------- the throttle, steer and boost (processPlayerInput)
    /// <summary>Raised where the boost's fuel display ran (updateBoostFuelDisplay: a unit of boost fuel used).</summary>
    public event Action? FuelTick;

    /// <summary>Raised where the original calls playSample with this sample number (the sound's own pitch and volume are
    /// in sampleParameterTable, entry x 16: period +8, volume +$A).</summary>
    public event Action<int>? Sample;
    public void PlaySample(int sample) => Sample?.Invoke(sample);

    /// <summary>processPlayerInput after readControllerInput ($5D8A8): flags = inputStateFlags (bits 0-1 the throttle:
    /// 1 accelerate, 2-3 brake; bits 2-3 steer: 4 one way, 8 the other; bit 4 fire = boost), then updateTurboBoostSystem.</summary>
    public void Input(int flags)
    {
        B(inputStateFlags, flags);
        int d0 = B(wheelMovementActive);
        if (d0 != 0) { d0 = B(raceStartTimer); if (d0 == 0) { d0 = flags & 0xC; if (d0 != 0) d0 = d0 == 4 ? 0xF1 : 0x0F; } }
        B(steeringInputDirection, d0);
        B(raceActiveFlag, (flags & 0x10) ^ 0x10);
        int d1 = 0, d2 = 0;
        var vz = B(carLocalVelocityZ);
        if ((Neg(vz) || vz < 0x78) && B(raceStartTimer) == 0 && B(postWreckStateFlag) == 0)
        {
            var t = flags & 3;
            if (t > 1) { d1 = 0x10; d2 = 0xFF; B(accelerationStateFlag, 0); }
            else if (t == 1 || Neg(B(accelerationStateFlag))) { d1 = B(gasInputIntensityValue); d2 = B(gasInputAccumulatorValue); B(accelerationStateFlag, 0x80); }
        }
        B(gasOutputIntensityValue, d1);
        B(gasOutputAccumulatorValue, d2);
        // updateTurboBoostSystem ($608A4)
        B(boostActiveFlag, 0);                                                       // every way out leaves it off (lbC052210)
        if ((B(raceActiveFlag) | B(postWreckStateFlag)) != 0) return;
        if (!Neg(B(accelerationStateFlag)) && (flags & 3) == 0) return;
        if (B(boostFuelLevel) == 0) return;
        if (!Neg(B(frameThrottleFlag)))
        {
            B(fuelConsumptionTimer, B(fuelConsumptionTimer) - 1);
            if (Neg(B(fuelConsumptionTimer))) { B(fuelConsumptionTimer, B(fuelConsumptionRate)); FuelTick?.Invoke(); }
        }
        B(boostActiveFlag, 0x80);
        W(gasOutputAccumulatorValue, W(gasOutputAccumulatorValue) << 1);
    }

    // ---------------------------------------------------------------- one pass (updateGamePhysics $6185C)
    /// <summary>One pass. heights: the road's height under the three contact points (front left, front right, rear),
    /// wheel units, given the corner offsets (wheelCorner*, set just before - calculateWheelCornerPositions) and the car's
    /// place (cameraWorldX / Z): updateWheelHeightsFromTrack's job.</summary>
    public void Physics(Func<ScrCar, (int FL, int FR, int Rear)> heights)
    {
        TransformMatrices();
        WheelCorners();
        var (fl, fr, rr) = heights(this);
        L(wheelHeightFrontLeft, fl); L(wheelHeightFrontRight, fr); L(wheelHeightRear, rr);
        ExpectedSurface();
        TransformWorld();
        WheelSpeed();
        // calculateViewAngles ($615E6): gravity $13D turned into the car's axes
        W(baseRollAngle, Mul(-0x13D, 0xF)); W(additionalYawOffset, Mul(-0x13D, 4)); W(baseTargetRoll, Mul(0x13D, 0xE));
        Suspension();
        Orientation();                                                                       // frameProcessingFlag: set in play ($80)
        Steering();
        AxesToWorld(worldAccelerationX, 2, 1, adjustedRollAngle, 9, adjustedPitchAngle, 12, adjustedYawAngle, 15);   // calculateSecondaryCoordinates
        Drag();
        // updateVelocityDamping ($62138)
        var ax = W(targetPitchRate) - (W(rotationSpeedX) >> 4);
        if (B(wheelMovementActive) != 0) ax += W(adjustedYawAngle) >> 2;
        W(angularAccelerationX, ax);
        W(angularAccelerationZ, W(targetRollRate) - (W(rotationSpeedZ) >> 4));
        // integrateVelocityComponents ($61B26)
        W(rotationSpeedX, W(rotationSpeedX) + Scale(W(angularAccelerationX)));
        W(rotationSpeedY, W(rotationSpeedY) + Scale(W(angularAccelerationY)));
        W(rotationSpeedZ, W(rotationSpeedZ) + Scale(W(angularAccelerationZ)));
        // calculateTertiaryCoordinates ($61672)
        for (var d2 = 1; d2 >= 0; d2--)
            W(transformedAngularVelocityX + 2 * d2, (short)(Mul(W(rotationSpeedX), TransformIndex[0x12 + d2]) + Mul(W(rotationSpeedY), TransformIndex[0x14 + d2])));
        W(transformedAngularVelocityZ, Mul(W(transformedAngularVelocityY), 4) + W(rotationSpeedZ));
        // applyVelocityIntegration ($61ADC)
        W(worldXSpeed, W(worldXSpeed) + Scale(W(worldAccelerationX)));
        W(worldYSpeed, W(worldYSpeed) + Scale(W(worldAccelerationY)));
        W(worldZSpeed, W(worldZSpeed) + Scale(W(worldAccelerationZ)));
        WorldPosition();
    }

    void TransformMatrices()                                                                // calculateTransformMatrices ($61368)
    {
        short Mw(int o) => W(M + o);
        void Set(int o, int v) => W(M + o, v);
        var s = Sine(W(cameraAngleY), false);
        foreach (var o in new[] { 4, 0xC, 0xE, 0x14, 0x16 }) Set(o, s);
        var c = Sine(W(cameraAngleY), true);
        foreach (var o in new[] { 6, 0x10, 0x12, 0x18, 0x1A }) Set(o, c);
        var rel = W(cameraAngleY) - W(segmentTargetAngle);
        s = Sine(rel, false); foreach (var o in new[] { 0x34, 0x42, 0x44 }) Set(o, s);
        c = Sine(rel, true); foreach (var o in new[] { 0x38, 0x3E, 0x46 }) Set(o, c);
        Set(8, Sine(W(cameraAngleX), false));
        c = Sine(W(cameraAngleX), true); Set(0xA, c); Set(0x1C, c); Set(0x1E, c);
        Set(0x22, Sine(W(cameraAngleZ), true));
        Set(0x20, Sine(W(cameraAngleZ), false));
        void Times(int by, int from, int to, int step) { var d5 = Mw(by); for (var o = from; o <= to; o += step) Set(o, Hi(Mw(o), d5)); }
        Times(8, 0xC, 0x12, 2);
        Times(8, 0x34, 0x38, 4);
        Set(0, Mw(0xC)); Set(2, Mw(0x10));
        Times(0xA, 4, 6, 2);
        Times(0xA, 0x44, 0x46, 2);
        Times(0x20, 0xC, 0x1C, 4);
        Times(0x20, 0x34, 0x38, 4);
        Times(0x22, 0xE, 0x1E, 4);
        Times(0x22, 0x3E, 0x42, 4);
        Set(0x28, Mw(0x18) - Mw(0xE));
        Set(0x2A, -Mw(0x12) - Mw(0x14));
        Set(0x2C, Mw(0x1A) + Mw(0xC));
        Set(0x2E, Mw(0x10) - Mw(0x16));
        Set(0x30, -Mw(0x1C));
        Set(0x24, -Mw(0x20));
    }

    void WheelCorners()                                                                      // calculateWheelCornerPositions ($618CE): the
    {                                                                                        // source's transformMatrix44/46/48 are +$42/$44/$46
        var d4 = (short)((W(M + 0x3E) >> 1) - (W(M + 0x34) >> 1)) >> 5;
        var d5 = (short)((W(M + 0x38) >> 1) - (W(M + 0x42) >> 1)) >> 5;
        int d0 = W(M + 0x44) >> 5, d3 = W(M + 0x46) >> 5;
        W(wheelCornerXRearCenter, -d0); W(wheelCornerYRearCenter, -d3);
        W(wheelCornerXFrontLeft, d0 - d4); W(wheelCornerYFrontLeft, d3 - d5);
        W(wheelCornerXFrontRight, d0 + d4); W(wheelCornerYFrontRight, d3 + d5);
    }

    void ExpectedSurface()                                                                   // calculateExpectedTrackSurfaceHeights ($61B70)
    {
        int d0 = Sine(W(cameraAngleZ), false) << 3, d3 = Sine(W(cameraAngleX), false) << 4, y = L(cameraWorldY);
        L(trackSurfaceRear, (y - d3) >> 8);
        L(trackSurfaceFrontRight, (y + d3 - d0) >> 8);
        L(trackSurfaceFrontLeft, (y + d3 + d0) >> 8);
    }

    void TransformWorld()                                                                    // transformWorldCoordinates ($6158C): Z and X only
    {
        for (var d2 = 2; d2 >= 0; d2 -= 2)
            W(carLocalVelocityX + 2 * d2, (short)(Mul(W(worldXSpeed), TransformIndex[d2]) + Mul(W(worldYSpeed), TransformIndex[3 + d2]) + Mul(W(worldZSpeed), TransformIndex[6 + d2])));
    }

    void WheelSpeed()                                                                        // updateWheelSpeed ($60FBE): the drawn wheels' spin
    {
        int d0 = W(carLocalVelocityZ); if (d0 < 0) d0 = (short)-d0;
        W(carSpeedMagnitude, d0);
        if (B(wheelMovementActive) == 0) { W(viewOffsetX, W(viewOffsetX) - ((W(viewOffsetX) & 0xFFFF) >> 2)); return; }
        if (d0 < 0x800) { W(viewOffsetX, d0 << 3); return; }
        var v = ((d0 << 1) & 0xFFFF) + 0x3000;
        W(viewOffsetX, v > 0xFFFF ? 0xFF00 : v);
    }

    void AxesToWorld(int to, int top, int stepDown, int a, int ia, int b, int ib, int c, int ic)
    {
        for (var d2 = top; d2 >= 0; d2 -= stepDown)
            W(to + 2 * d2, (short)(Mul(W(a), TransformIndex[ia + d2]) + Mul(W(b), TransformIndex[ib + d2]) + Mul(W(c), TransformIndex[ic + d2])));
    }

    // ---------------------------------------------------------------- the suspension (updateWheelSuspensionPhysics $61BCC)
    void Suspension()
    {
        B(hardImpactCount, 0);
        B(maxCompressionVelocity, 0);                                                        // a byte: the word's high half
        Wheel(wheelHeightFrontLeft, trackSurfaceFrontLeft, suspensionTravelFrontLeft, clampedSuspensionFrontLeft, previousSuspensionFrontLeft, suspensionVelocityFrontLeft, accumulatedForceFrontLeft);
        Wheel(wheelHeightFrontRight, trackSurfaceFrontRight, suspensionTravelFrontRight, clampedSuspensionFrontRight, previousSuspensionFrontRight, suspensionVelocityFrontRight, accumulatedForceFrontRight);
        Wheel(wheelHeightRear, trackSurfaceRear, suspensionTravelRear, clampedSuspensionRear, previousSuspensionRear, suspensionVelocityRear, accumulatedForceRear);
        var t1 = (short)(W(suspensionVelocityFrontLeft) + W(suspensionVelocityFrontRight)) >> 1;
        W(tempWord1, t1);
        W(averageWheelVelocity, (short)(t1 + W(suspensionVelocityRear)) >> 1);
        SpringForces();
        var d3 = (short)(W(suspensionVelocityFrontLeft) - W(suspensionVelocityFrontRight));
        var d0 = (short)(d3 * 3); if (d0 < 0) d0 = (short)-d0;
        if (d0 >= 0x1000) d0 = 0x1000;
        if (d3 < 0) d0 = (short)-d0;
        W(targetRollRate, d0);
        W(targetPitchRate, W(tempWord1) - W(suspensionVelocityRear));
        B(wheelMovementActive, B(averageWheelVelocity) | B(averageWheelVelocity + 1));
        if (B(wheelMovementActive) == 0 && B(raceStartTimer) == 0)
        {                                                                                    // all wheels up: the nose drops ($61FE0)
            int p = -0x80, ax = W(cameraAngleX);
            var go = true;
            if (ax < 0) { var t = B(currentTrackID); go = t == 7 || t == 4; if (t == 4) p = -8; }   // track 7's own value goes to D1 (the original's slip): -$80 stays
            else if (ax >= 0x1000) p = -0x100;
            if (go)
            {
                var d = (short)(p - W(targetPitchRate));
                var rx = B(rotationSpeedX);
                if (d < 0 && (!Neg(rx) || rx == 0xFF)) W(targetPitchRate, d);
            }
        }
        // handleRaceStartCountdown: raceStartTimer 0 - nothing
        W(lbW00D646, W(yawSpringAngle));
        if (B(collisionSoundCooldown) != 0) B(collisionSoundCooldown, B(collisionSoundCooldown) - 1);   // applyOpponentCollisionForces, no collision
        if (B(impactSoundCooldownTimer) != 0) B(impactSoundCooldownTimer, B(impactSoundCooldownTimer) - 1);
        if (B(hardImpactCount) != 0)
        {
            int v = B(maxCompressionVelocity);
            if (v < 7) v = 7;
            v = (v << 2) & 0xFF;
            if (v >= 0x40) v = 0x40;
            B(sampleImpactVolume, v);
            if (B(impactSoundCooldownTimer) == 0) { PlaySample(3); B(impactSoundCooldownTimer, 5); }   // the bang
        }
    }

    void Wheel(int height, int surface, int travel, int clamped, int previous, int velocity, int force)
    {
        var d0 = L(height) - L(surface) - L(trackOffsetAdjustment);
        L(travel, d0);
        if (d0 < 0) { if ((uint)d0 < 0xFFFFFD00) d0 = -0x300; }
        else if ((uint)d0 >= 0x1400) d0 = 0x1400;
        W(clamped, d0);
        var d6 = (short)d0;
        var push = (short)((short)((short)d0 - W(previous)) * 0x114 >> 8);                   // applyMomentumAmplification ($6180E)
        push = (short)(push + d6);
        if (push < 0) { W(velocity, 0); B(highCompressionFrameCount, 0); }
        else
        {
            var was = W(velocity);
            W(velocity, push);
            if (push >= 0x400 && was < 0x200) B(hardImpactCount, B(hardImpactCount) + 1);
            var d = (short)(push - (B(trackWidthMultiplier) << 8));
            if (d < 0 || d < 0x700) B(highCompressionFrameCount, 0);
            else
            {
                if ((ushort)d >= (ushort)W(maxCompressionVelocity)) W(maxCompressionVelocity, d);
                d = (short)(d - 0x600);
                if (!Neg(B(frameThrottleFlag)))
                {
                    B(highCompressionFrameCount, B(highCompressionFrameCount) + 1);
                    if ((sbyte)B(highCompressionFrameCount) < (sbyte)B(damageFrameLimit))
                    {
                        int b = (d & 0xFFFF) >> 8;
                        b = (b + (b >> 1)) & 0xFF;
                        b += B(force);
                        B(force, b > 0xFF ? 0xFF : b);
                        B(damageAccumulationActive, 0x80);
                    }
                }
                if ((ushort)W(velocity) >= 0x1200) W(velocity, 0x11FF);
            }
        }
        W(previous, W(clamped));
    }

    void SpringForces()                                                                      // calculateSuspensionSpringForces ($622DC)
    {
        W(pitchReferenceZero, 0);
        var d0 = (((L(suspensionTravelFrontLeft) + L(suspensionTravelFrontRight)) >> 1) - L(suspensionTravelRear)) >> 4;
        W(pitchDisplacement, (short)d0 ^ 0x8000);
        Attenuate(d0);
        B(tempAttenuatedValue, B(attenuatedDisplacementValue));
        B(yawSpringComponent, B(rawDisplacementValue));
        d0 = (L(suspensionTravelFrontLeft) - L(suspensionTravelFrontRight)) >> 3;
        W(rollDisplacement, d0);
        Attenuate(d0);
        B(tempByte3, B(tempAttenuatedValue));
        B(pitchSpringComponent, Product(B(attenuatedDisplacementValue)));
        B(rollSpringComponent, Product(B(rawDisplacementValue)));
        W(rollSpringAngle, Signed(W(averageWheelVelocity), B(rollSpringComponent), B(rollDisplacement)));
        W(pitchSpringAngle, Signed(W(averageWheelVelocity), B(pitchSpringComponent), B(pitchReferenceZero)));
        W(yawSpringAngle, Signed(W(averageWheelVelocity), B(yawSpringComponent), B(pitchDisplacement)));
        B(tempByte3, B(yawSpringComponent));
        B(trackSideIndicatorCopy, B(pitchDisplacement));
    }

    void Attenuate(int v)                                                                    // attenuateDisplacementValue ($62424)
    {
        var d0 = (short)v; if (d0 < 0) d0 = (short)-d0;
        var d1 = d0 < 0x100 ? d0 & 0xFF : 0xFF;
        B(rawDisplacementValue, d1);
        B(attenuatedDisplacementValue, Attenuation[d1 >> 1]);
    }

    int Product(int v)                                                                       // multiplyAndRandomize ($612A2)
    {
        var p = (v & 0xFF) * B(tempByte3);
        B(tempByte4, p);
        return p >> 8;
    }

    static short Signed(int v, int by, int side) => Hi(v, (short)((Neg(side) ? -by : by) << 7));

    // ---------------------------------------------------------------- the push and the turn
    short Multiplier() => B(wheelMovementActive) != 0 ? (short)(W(pitchSpringAngle) << 1) : (short)0;   // getSteeringMultiplier ($621DA)

    void Orientation()                                                                       // updateCarOrientation ($620B8)
    {
        W(adjustedPitchAngle, W(baseRollAngle) + W(pitchSpringAngle));
        var d0 = B(gasOutputAccumulatorValue) | B(carLocalVelocityZ);
        if (!Neg(d0) && B(gasOutputIntensityValue) != 0) W(gasOutputAccumulatorValue, W(gasOutputAccumulatorValue) - d0);
        var d3 = W(gasOutputAccumulatorValue); if (d3 < 0) d3 = (short)-d3;
        var m = Multiplier();
        if ((ushort)d3 >= (ushort)m) W(gasOutputAccumulatorValue, Neg(B(gasOutputAccumulatorValue)) ? -m : m);
        W(adjustedYawAngle, W(gasOutputAccumulatorValue) + W(yawSpringAngle) + W(additionalYawOffset));
        // calculateRollAngleTransition ($6217A)
        var d4 = (short)(W(baseTargetRoll) + W(rollSpringAngle));
        var gap = (short)(d4 - W(carLocalVelocityX)); if (gap < 0) gap = (short)-gap;
        m = Multiplier();
        if ((ushort)gap < (ushort)m) { W(adjustedRollAngle, W(rollSpringAngle) - W(carLocalVelocityX)); B(rollTransitionFlag, 0); }
        else { W(adjustedRollAngle, d4 - (Neg(B(carLocalVelocityX)) ? -m : m)); B(rollTransitionFlag, 0x80); }
    }

    void Steering()                                                                          // calculateSteeringResponse ($61012), the track given
    {
        var d4 = (short)((W(segmentTargetAngle) - W(cameraAngleY)) ^ W(reverseDirectionFlag));
        var d2 = 0;
        if (Neg(B(segmentSteeringFlags))) { d2 = 1; if ((short)(W(trackDirectionInversionFlag) ^ W(reverseDirectionFlag)) < 0) d2 = 2; }
        d4 = (short)(d4 + SteeringOffset[d2]);
        var dist = d4 < 0 ? (short)-d4 : d4;
        W(segmentProgressDistance, dist);
        W(tempWord1, d4);
        W(steeringScaleFactor, (ushort)dist >= 0x800 ? 0x7FFF : dist << 4);
        // (the next piece's lookup when near its end: the track's, given)
        B(trackDirectionMultiplier, B(trackDirectionInversionFlag) ^ B(reverseDirectionFlag));
        int d0;
        if (B(steeringInputDirection) != 0)
        {
            B(tempByte4, B(steeringInputDirection) ^ B(tempWord1));
            if (Neg(B(segmentSteeringFlags)))
            {
                if (Neg(B(steeringInputDirection) ^ B(trackDirectionMultiplier)))
                {
                    B(steeringInputDirection, B(trackDirectionMultiplier));
                    Toward(B(segmentBaseSteeringOffset) - 0x23); return;
                }
                d0 = B(segmentBaseSteeringOffset) + 0x2D;
            }
            else d0 = B(segmentBaseSteeringOffset);
            if (!Neg(B(tempByte4))) d0 += B(steeringScaleFactor);
            Toward(d0); return;
        }
        if (Neg(B(segmentSteeringFlags))) { B(steeringInputDirection, B(trackDirectionMultiplier)); Toward(B(segmentBaseSteeringOffset)); return; }
        Straighten(0);
    }

    /// <summary>$6121C: the turn push from the stick (or a corner's own), then on to $611D4 or, far off the road's way, $61160.</summary>
    void Toward(int d0)
    {
        B(tempByte3, d0);
        var d = Hi(W(carLocalVelocityZ), (short)((B(tempByte3) << 7) & 0x7FFF));
        if (Neg(B(steeringInputDirection))) d = (short)-d;
        var d4 = (short)(d >> 3);
        if (B(segmentProgressDistance) < 0x1E) Finish(d4); else Straighten(d4);
    }

    void Straighten(int d4)                                                                  // $61160: turn the car itself back to the road's way
    {
        B(steeringInputDirection, B(tempWord1));
        int d0 = W(segmentProgressDistance);
        var d2 = d0 & 0xFF;
        var skip = false;
        if (B(segmentProgressDistance) != 0)
        {
            d0 = (short)(d0 - 0x1E00);
            if (d0 >= 0) skip = true; else d2 = 0xFF;
        }
        if (!skip)
        {
            B(tempByte3, d2);
            int v = W(carLocalVelocityZ); if (v < 0) v = (short)-v;
            v = (short)(v + 0xA00); if (v < 0) v = 0x7F00;
            d0 = (Hi(v, (short)((B(tempByte3) << 7) & 0x7FFF)) & 0xFFFF) >> 7;
            if ((d0 & 0xFF) == 0) d0 = (d0 & 0xFF00) | 1;
        }
        if (Neg(B(tempWord1))) d0 = -d0;
        W(cameraAngleY, W(cameraAngleY) + d0);
        Finish(d4);
    }

    void Finish(int d4)                                                                      // $611D4
    {
        d4 = (short)(d4 - W(rotationSpeedY));
        if (B(wheelMovementActive) == 0) d4 = 0;
        W(angularAccelerationY, d4);
    }

    void Drag()                                                                              // updateCollisionState ($621F4)
    {
        int d7 = 1, d0;
        bool heavy;
        if (B(wheelMovementActive) == 0) heavy = B(raceStartTimer) != 0 ? (d7 = 3) > 0 : false;
        else
        {
            var y = B(lbW00D646); if (Neg(y)) y ^= 0xFF;
            if ((sbyte)y >= 3 || Neg(B(offTrackStateFlags))) heavy = true;
            else if (B(postWreckStateFlag) != 0 || B(raceStartTimer) != 0) { d7 = 3; heavy = true; }
            else heavy = false;
        }
        if (heavy) d0 = 0x6000;
        else
        {
            static int Abs(int v) => v < 0 ? (short)-v : v;
            d0 = Math.Max(Abs(W(carLocalVelocityX)), Abs(W(carLocalVelocityY)));
            d0 = Math.Max(d0, Abs(W(carLocalVelocityZ)));
            d7 = 5;
            if (Neg(B(draftingTimer)) && !Neg(B(opponentAheadFlag))) { d0 -= 0x14 << 7; if (d0 < 0) d0 = 0; }
        }
        foreach (var (speed, acc) in new[] { (worldXSpeed, worldAccelerationX), (worldYSpeed, worldAccelerationY), (worldZSpeed, worldAccelerationZ) })
            W(acc, W(acc) - ((short)(W(speed) * (short)d0 >> 16) >> d7));
    }

    void WorldPosition()                                                                     // updateWorldPosition ($61950)
    {
        L(cameraWorldX, L(cameraWorldX) + (Scale(W(worldXSpeed)) << 6));
        L(cameraWorldY, L(cameraWorldY) + (Scale(W(worldYSpeed)) << 7));
        L(cameraWorldZ, L(cameraWorldZ) + (Scale(W(worldZSpeed)) << 6));
        if (W(cameraWorldY) >= 0x3E8) W(cameraWorldY, 0x3E8);                                // the long's high word
        W(cameraAngleX, W(cameraAngleX) + Scale(W(transformedAngularVelocityX)));
        W(cameraAngleY, W(cameraAngleY) + Scale(W(transformedAngularVelocityY)));
        W(cameraAngleZ, W(cameraAngleZ) + Scale(W(transformedAngularVelocityZ)));
        var k = Neg(B(raceCompletionCheckFlag)) && B(renderingOrderMode) == 0xE0 ? 1 : 0;
        void Limit(int angle, int rate)
        {
            var a = W(angle);
            int lim;
            if (a >= 0) { lim = AngleLimits[k]; if ((ushort)lim >= (ushort)a) return; }
            else { lim = AngleLimits[k + 2]; if ((ushort)lim < (ushort)a) return; }
            W(angle, lim);
            if ((short)(lim ^ W(rate)) >= 0) W(rate, 0);
        }
        Limit(cameraAngleX, rotationSpeedX);
        Limit(cameraAngleZ, rotationSpeedZ);
        var f = B(lbB00D4AB) & 0x7F;
        var hx = (sbyte)B(cameraAngleX); if (hx < 0) hx = (sbyte)-hx;
        if (hx >= 0xF) f |= 0x80;
        B(lbB00D4AB, f);
        W(viewportCenterY, -W(cameraAngleX));
    }
}
