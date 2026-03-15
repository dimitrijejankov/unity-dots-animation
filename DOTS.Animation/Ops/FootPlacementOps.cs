using System;
using AnimationSystem.Settings;
using AnimationSystem;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace AnimationSystem.Ops
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Foot Placement Pipeline
    //
    // Three-phase ALS-style foot IK executed every frame:
    //
    //   Phase 1 – Foot Locking    : plants each foot when a FootLock curve is active.
    //   Phase 2 – Terrain Offsets : raycasts beneath each foot and adapts to the
    //                               ground height and slope; also adjusts the pelvis.
    //   Phase 3 – Skeleton Write  : writes the computed offsets additively to the
    //                               pose, then runs two-bone IK to bend each leg.
    //
    // All persistent state lives in FootIKRuntimeState.
    // All intermediate values are in component space unless noted otherwise.
    // ─────────────────────────────────────────────────────────────────────────────

    [BurstCompile]
    public static class FootPlacementOps
    {
        // ── Entry Point ───────────────────────────────────────────────────────────

        public static void Update(
            ref FootIKRuntimeState state,
            in FootIKSettings settings,
            in IKSettings ikSettings,
            Span<RigidTransform> pose,
            in LocalTransform characterTransform,
            float3 velocity,
            bool isGrounded,
            quaternion previousRotation,
            in PhysicsWorld physicsWorld,
            float deltaTime,
            float enableL,
            float enableR,
            float lockL,
            float lockR,
            float rotationAmount)
        {
            if (!settings.EnableFootIK)
                return;

            if (ikSettings.RootBoneIndex < 0 || ikSettings.RootBoneIndex >= pose.Length)
                return;

            if (!state.IsInitialized)
                InitializeState(ref state);

            var rootCS = pose[ikSettings.RootBoneIndex];
            if (math.lengthsq(rootCS.Rotation.value) < 0.5f) // uninitialized pose guard
                return;

            // Phase 1: Foot locking (always runs, even when airborne)
            SetFootLocking(ref state.LockL, enableL, lockL, rotationAmount,
                pose, ikSettings.IKFootLIndex, rootCS, velocity, characterTransform, previousRotation, deltaTime);
            SetFootLocking(ref state.LockR, enableR, lockR, rotationAmount,
                pose, ikSettings.IKFootRIndex, rootCS, velocity, characterTransform, previousRotation, deltaTime);

            // Phase 2: Terrain offsets + pelvis (grounded) or smooth reset (airborne)
            if (isGrounded)
            {
                SetFootTerrainOffset(ref state.OffsetL, enableL, pose, ikSettings.IKFootLIndex,
                    rootCS, characterTransform, physicsWorld, settings, velocity, deltaTime);
                SetFootTerrainOffset(ref state.OffsetR, enableR, pose, ikSettings.IKFootRIndex,
                    rootCS, characterTransform, physicsWorld, settings, velocity, deltaTime);
                SetPelvisOffset(ref state, enableL, enableR, settings, deltaTime);
            }
            else
            {
                SetPelvisOffset(ref state, 0f, 0f, settings, deltaTime);
                ResetFootOffsets(ref state, settings.ResetInterpSpeed, deltaTime);
            }

            // Phase 3: Apply to skeleton + two-bone IK
            ApplyToSkeleton(ref state, in ikSettings, pose, rootCS, enableL, enableR);
        }

        private static void InitializeState(ref FootIKRuntimeState state)
        {
            state.IsInitialized          = true;
            state.LockL.Rotation         = quaternion.identity;
            state.LockR.Rotation         = quaternion.identity;
            state.OffsetL.RotationOffset = quaternion.identity;
            state.OffsetR.RotationOffset = quaternion.identity;
        }

        // ── Phase 1: Foot Locking ─────────────────────────────────────────────────
        //
        // Hysteresis-based foot plant. The lock engages when the FootLock curve
        // reaches 1 and holds until the curve drops or a turn-in-place is detected.
        // While locked, the foot position is kept stable in world space by
        // compensating for character translation and yaw rotation each frame.

        private static void SetFootLocking(
            ref FootLockState lockState,
            float enableFootIK,
            float footLockCurve,
            float rotationAmount,
            ReadOnlySpan<RigidTransform> pose,
            int ikFootIndex,
            in RigidTransform rootCS,
            float3 velocity,
            in LocalTransform characterTransform,
            quaternion previousRotation,
            float deltaTime)
        {
            if (enableFootIK <= 0f)
                return;

            // Hysteresis: engage at ≥ 0.99, disengage on rotation; alpha can only decrease
            float lockCurveVal;
            if (lockState.UseFootLockCurve)
            {
                lockState.UseFootLockCurve = math.abs(rotationAmount) <= 0.001f;
                lockCurveVal = footLockCurve;
            }
            else
            {
                lockState.UseFootLockCurve = footLockCurve >= 0.99f;
                lockCurveVal = 0f;
            }

            if (lockCurveVal >= 0.99f || lockCurveVal < lockState.Alpha)
                lockState.Alpha = lockCurveVal;

            // Capture foot in component space when fully locked
            if (lockState.Alpha >= 0.99f && ikFootIndex >= 0 && ikFootIndex < pose.Length)
            {
                var footLocal      = pose[ikFootIndex];
                lockState.Location = rootCS.Position + math.mul(rootCS.Rotation, footLocal.Position);
                lockState.Rotation = math.mul(rootCS.Rotation, footLocal.Rotation);
            }

            // Keep the locked world-space position stable as the character moves
            if (lockState.Alpha > 0f)
                CompensateLockForMovement(ref lockState, velocity, characterTransform, previousRotation, deltaTime);
        }

        // Subtracts character translation and counters yaw rotation so the locked
        // foot remains visually stationary in world space.
        private static void CompensateLockForMovement(
            ref FootLockState lockState,
            float3 velocity,
            in LocalTransform characterTransform,
            quaternion previousRotation,
            float deltaTime)
        {
            float3 locationDiff = math.mul(math.inverse(characterTransform.Rotation), velocity * deltaTime);
            lockState.Location -= locationDiff;

            quaternion rotDiff = math.mul(characterTransform.Rotation, math.inverse(previousRotation));
            float yawDelta = MathUtil.ToEuler(rotDiff).y;

            if (math.abs(yawDelta) > 0.0001f)
            {
                lockState.Location = math.mul(quaternion.AxisAngle(math.down(), yawDelta), lockState.Location);
                lockState.Rotation = math.mul(math.inverse(quaternion.AxisAngle(math.up(), yawDelta)), lockState.Rotation);
            }
        }

        // ── Phase 2a: Terrain Offset ──────────────────────────────────────────────
        //
        // Raycasting and ground target computation are in FootGroundCastOps.
        // This method only orchestrates the interp and state update.

        private static void SetFootTerrainOffset(
            ref FootOffsetState offsetState,
            float enableFootIK,
            ReadOnlySpan<RigidTransform> pose,
            int ikFootIndex,
            in RigidTransform rootCS,
            in LocalTransform characterTransform,
            in PhysicsWorld physicsWorld,
            in FootIKSettings settings,
            float3 velocity,
            float deltaTime)
        {
            if (enableFootIK <= 0f)
            {
                offsetState.LocationOffset = float3.zero;
                offsetState.RotationOffset = quaternion.identity;
                return;
            }

            if (ikFootIndex < 0 || ikFootIndex >= pose.Length)
                return;

            // Foot floor: the foot's XZ position projected onto the root's Y height
            var    footLocal  = pose[ikFootIndex];
            float3 footCS     = rootCS.Position + math.mul(rootCS.Rotation, footLocal.Position);
            float3 footWorld  = characterTransform.Position + math.mul(characterTransform.Rotation, footCS);
            float3 rootWorld  = characterTransform.Position + math.mul(characterTransform.Rotation, rootCS.Position);
            float3 footFloor  = new float3(footWorld.x, rootWorld.y, footWorld.z);

            float3 traceStart = footFloor + new float3(0f, settings.IK_TraceDistanceAboveFoot, 0f);
            float3 traceEnd   = footFloor - new float3(0f, settings.IK_TraceDistanceBelowFoot, 0f);

            if (!math.all(math.isfinite(traceStart)) || !math.all(math.isfinite(traceEnd)))
                return;
            if (math.lengthsq(traceStart) >= 1e8f || math.lengthsq(traceEnd) >= 1e8f)
                return;

            var filter = settings.GroundLayerMask != 0
                ? new CollisionFilter { BelongsTo = ~0u, CollidesWith = settings.GroundLayerMask, GroupIndex = 0 }
                : CollisionFilter.Default;

            GroundHit hit = FootGroundCastOps.CastGround(traceStart, traceEnd, characterTransform.Rotation, settings, filter, physicsWorld);

            // Discard hits above the trace origin — these are wall contacts from diagonal movement
            if (hit.IsValid && hit.Point.y > footFloor.y + settings.IK_TraceDistanceAboveFoot)
                hit = GroundHit.Invalid;

            float3     locationTarget = float3.zero;
            quaternion rotationTarget = quaternion.identity;

            if (hit.IsValid)
                FootGroundCastOps.ComputeFootTarget(hit, footFloor, characterTransform, settings, velocity,
                    out locationTarget, out rotationTarget);

            offsetState.LocationTarget = locationTarget;

            float locationSpeed = offsetState.LocationOffset.y > locationTarget.y
                ? settings.FootOffsetInterpSpeedDown
                : settings.FootOffsetInterpSpeedUp;

            offsetState.LocationOffset = MathOps.InterpTo(offsetState.LocationOffset, locationTarget, deltaTime, locationSpeed);
            offsetState.RotationOffset = MathOps.InterpTo(offsetState.RotationOffset, rotationTarget, deltaTime, settings.FootOffsetRotationInterpSpeed);
        }

        // ── Phase 2b: Pelvis Offset ───────────────────────────────────────────────
        //
        // Lowers the pelvis to accommodate whichever foot has the larger downward
        // reach, so both legs can remain grounded simultaneously.

        private static void SetPelvisOffset(
            ref FootIKRuntimeState state,
            float enableL,
            float enableR,
            in FootIKSettings settings,
            float deltaTime)
        {
            state.PelvisAlpha = (enableL + enableR) * 0.5f;

            if (state.PelvisAlpha <= 0f)
            {
                state.PelvisOffset = float3.zero;
                return;
            }

            // Drive the pelvis toward whichever foot needs to go lower
            float3 pelvisTarget = state.OffsetL.LocationTarget.y < state.OffsetR.LocationTarget.y
                ? state.OffsetL.LocationTarget
                : state.OffsetR.LocationTarget;

            float interpSpeed = pelvisTarget.y > state.PelvisOffset.y
                ? settings.PelvisInterpSpeedUp
                : settings.PelvisInterpSpeedDown;

            state.PelvisOffset = MathOps.InterpTo(state.PelvisOffset, pelvisTarget, deltaTime, interpSpeed);
        }

        // Smoothly returns foot offsets to neutral while airborne.
        private static void ResetFootOffsets(ref FootIKRuntimeState state, float interpSpeed, float deltaTime)
        {
            state.OffsetL.LocationOffset = MathOps.InterpTo(state.OffsetL.LocationOffset, float3.zero,         deltaTime, interpSpeed);
            state.OffsetR.LocationOffset = MathOps.InterpTo(state.OffsetR.LocationOffset, float3.zero,         deltaTime, interpSpeed);
            state.OffsetL.RotationOffset = MathOps.InterpTo(state.OffsetL.RotationOffset, quaternion.identity, deltaTime, interpSpeed);
            state.OffsetR.RotationOffset = MathOps.InterpTo(state.OffsetR.RotationOffset, quaternion.identity, deltaTime, interpSpeed);
        }

        // ── Phase 3: Skeleton Application ────────────────────────────────────────
        //
        // Writes the pelvis offset and per-foot lock + terrain offsets additively
        // to the pose, then runs two-bone IK so the leg bones reach the targets.

        private static void ApplyToSkeleton(
            ref FootIKRuntimeState state,
            in IKSettings ikSettings,
            Span<RigidTransform> pose,
            in RigidTransform rootCS,
            float alphaL,
            float alphaR)
        {
            // Pelvis: convert component-space offset to hip local space and add it
            if (state.PelvisAlpha > 0f && ikSettings.HipBoneIndex >= 0 && ikSettings.HipBoneIndex < pose.Length)
            {
                float3 localPelvisOffset = math.mul(math.inverse(rootCS.Rotation), state.PelvisOffset * state.PelvisAlpha);
                pose[ikSettings.HipBoneIndex].Position += localPelvisOffset;
            }

            if (alphaL > 0f) ApplyFootIK(ref state.LockL, ref state.OffsetL, ikSettings, pose, rootCS, isLeft: true,  alphaL);
            if (alphaR > 0f) ApplyFootIK(ref state.LockR, ref state.OffsetR, ikSettings, pose, rootCS, isLeft: false, alphaR);
        }

        // Blends foot lock + terrain offset onto the IK foot bone, then solves
        // two-bone IK for the full leg chain (thigh → shin → foot).
        private static void ApplyFootIK(
            ref FootLockState lockState,
            ref FootOffsetState offsetState,
            in IKSettings ikSettings,
            Span<RigidTransform> pose,
            in RigidTransform rootCS,
            bool isLeft,
            float alpha)
        {
            int ikFootIdx = isLeft ? ikSettings.IKFootLIndex        : ikSettings.IKFootRIndex;
            int thighIdx  = isLeft ? ikSettings.LeftThighBoneIndex : ikSettings.RightThighBoneIndex;
            int shinIdx   = isLeft ? ikSettings.LeftShinBoneIndex  : ikSettings.RightShinBoneIndex;
            int footIdx   = isLeft ? ikSettings.LeftFootBoneIndex  : ikSettings.RightFootBoneIndex;

            if (ikFootIdx < 0 || ikFootIdx >= pose.Length || thighIdx < 0 || shinIdx < 0 || footIdx < 0)
                return;

            // Build target: current IK foot → apply lock → apply terrain offset
            var        ikFootLocal  = pose[ikFootIdx];
            float3     ikFootCS     = rootCS.Position + math.mul(rootCS.Rotation, ikFootLocal.Position);
            quaternion ikFootRotCS  = math.mul(rootCS.Rotation, ikFootLocal.Rotation);

            float3     targetCS     = ikFootCS;
            quaternion targetRotCS  = ikFootRotCS;

            if (lockState.Alpha > 0f)
            {
                targetCS    = math.lerp(targetCS,    lockState.Location, lockState.Alpha);
                targetRotCS = math.slerp(targetRotCS, lockState.Rotation, lockState.Alpha);
            }

            targetCS    += offsetState.LocationOffset;
            targetRotCS  = math.mul(offsetState.RotationOffset, targetRotCS);

            // Write blended IK foot pose back to local space
            quaternion invRootRot  = math.inverse(rootCS.Rotation);
            float3     targetLocal    = math.mul(invRootRot, targetCS - rootCS.Position);
            quaternion targetRotLocal = math.mul(invRootRot, targetRotCS);

            pose[ikFootIdx].Position = math.lerp(ikFootLocal.Position,  targetLocal,     alpha);
            pose[ikFootIdx].Rotation = math.slerp(ikFootLocal.Rotation, targetRotLocal,  alpha);

            // Two-bone IK: bend thigh + shin to reach the adjusted foot position
            var limb = FootIKOps.ComputeLimbComponentSpace(
                pose, ikSettings.RootBoneIndex, ikSettings.HipBoneIndex, thighIdx, shinIdx, footIdx);

            float3  finalFootCS = rootCS.Position + math.mul(rootCS.Rotation, pose[ikFootIdx].Position);
            ref var thigh       = ref pose[thighIdx];
            ref var shin        = ref pose[shinIdx];
            var     tRot        = thigh.Rotation;
            var     sRot        = shin.Rotation;

            TwoBoneIKOps.Solve(
                limb.ThighPosition, limb.ShinPosition, limb.FootPosition, finalFootCS,
                limb.ThighRotation, limb.ShinRotation,
                ref tRot, ref sRot);

            thigh.Rotation = math.slerp(thigh.Rotation, tRot, alpha);
            shin.Rotation  = math.slerp(shin.Rotation,  sRot, alpha);

            // The IK solve changed thigh + shin rotations, so we must re-derive the shin
            // CS rotation before we can express the foot rotation in its local frame.
            quaternion hipCS    = math.mul(rootCS.Rotation, pose[ikSettings.HipBoneIndex].Rotation);
            quaternion thighCS  = math.mul(hipCS,  thigh.Rotation);
            quaternion shinCS   = math.mul(thighCS, shin.Rotation);

            quaternion footRotCS    = math.mul(rootCS.Rotation, pose[ikFootIdx].Rotation);
            quaternion footRotLocal = math.mul(math.inverse(shinCS), footRotCS);
            pose[footIdx].Rotation  = math.slerp(pose[footIdx].Rotation, footRotLocal, alpha);
        }
    }
}
