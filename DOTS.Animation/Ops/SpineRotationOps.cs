using System;
using AnimationSystem;
using Unity.Burst;
using Unity.Mathematics;
using RigidTransform = AnimationSystem.RigidTransform;

namespace AnimationSystem.Ops
{
    /// <summary>
    /// Persisted spine aiming state. Updated each frame.
    /// </summary>
    public struct SpineAimingState
    {
        /// <summary>Smoothly interpolated view rotation used for spine yaw computation.</summary>
        public quaternion SmoothedAimingRotation;
        /// <summary>Blend weight between 0 (velocity-direction mode) and 1 (look-at mode).</summary>
        public float SpineRotationWeight;
    }

    /// <summary>
    /// ALS-style sequential component-space yaw rotation applied to the spine chain
    /// (Hips → Spine01 → Spine02 → Spine03)
    /// [Additive, Component Space] behaviour.
    /// </summary>
    [BurstCompile]
    public static class SpineRotationOps
    {
        /// <summary>
        /// Computes and stores the per-frame spine yaw and blend weight.
        /// </summary>
        public static void CalculateAimingValues(
            ref SpineAimingState state,
            quaternion characterRotation,
            quaternion viewRotation,
            bool useSpineRotation,
            float deltaTime,
            out float targetSpineYaw,
            float aimingRotationInterpSpeed = 10f,
            float spineWeightInterpSpeed = 5f)
        {
            // Smooth aiming rotation toward view rotation
            state.SmoothedAimingRotation = math.slerp(
                state.SmoothedAimingRotation,
                viewRotation,
                math.saturate(aimingRotationInterpSpeed * deltaTime));

            // Compute signed yaw delta between character rotation and view rotation
            quaternion aimDelta = math.mul(math.inverse(characterRotation), viewRotation);
            float3 aimFwd = math.mul(aimDelta, math.forward());
            float aimingYaw = math.degrees(math.atan2(aimFwd.x, aimFwd.z));

            // Spine yaw per-bone = total yaw / 4 (ALS: each of 4 bones gets AimingAngle.X / 4)
            targetSpineYaw = useSpineRotation ? aimingYaw / 4f : 0f;

            // Smooth Enable_SpineRotation weight between modes
            float targetWeight = useSpineRotation ? 1f : 0f;
            state.SpineRotationWeight = math.saturate(
                math.lerp(state.SpineRotationWeight, targetWeight, spineWeightInterpSpeed * deltaTime));
        }

        /// <summary>
        /// Applies additive component-space yaw rotation sequentially to 4 bones
        /// in the spine chain. Because each bone receives the same yawQ additively
        /// in CS, the cumulative CS rotation at Spine03 equals yawQ^4. Dividing the
        /// desired total yaw by 4 before calling this method (as ALS does) means the
        /// top of the spine accumulates the full original angle.
        /// </summary>
        public static void ApplySpineRotation(
            Span<RigidTransform> pose,
            ReadOnlySpan<int> parentIndices,
            int hipBone, int spine01, int spine02, int spine03,
            float yawDegrees)
        {
            if (math.abs(yawDegrees) < 0.001f) return;

            var yawQ = quaternion.AxisAngle(math.up(), math.radians(yawDegrees));

            // Build component-space (CS) quaternion of hipBone's parent by walking ancestors
            quaternion parentCS = BuildParentCS(pose, parentIndices, hipBone);

            // Apply yawQ in CS to each bone in chain order; parentCS advances each step
            ApplyToBone(pose, hipBone, yawQ, ref parentCS);
            ApplyToBone(pose, spine01, yawQ, ref parentCS);
            ApplyToBone(pose, spine02, yawQ, ref parentCS);
            ApplyToBone(pose, spine03, yawQ, ref parentCS);
        }

        // Walk up parentIndices to compute the accumulated CS quaternion of bone's parent
        private static quaternion BuildParentCS(
            ReadOnlySpan<RigidTransform> pose,
            ReadOnlySpan<int> parentIndices,
            int bone)
        {
            // Collect ancestors from root down to bone's direct parent
            Span<int> chain = stackalloc int[16];
            int depth = 0;
            int cur = parentIndices[bone];
            while (cur != -1 && depth < 16)
            {
                chain[depth++] = cur;
                cur = parentIndices[cur];
            }
            quaternion cs = quaternion.identity;
            for (int i = depth - 1; i >= 0; i--)
                cs = math.mul(cs, pose[chain[i]].Rotation);
            return cs;
        }

        // Apply yawQ in component space to one bone; advance parentCS to this bone's new CS
        private static void ApplyToBone(
            Span<RigidTransform> pose,
            int bone,
            quaternion yawQ,
            ref quaternion parentCS)
        {
            quaternion boneCS    = math.mul(parentCS, pose[bone].Rotation);
            quaternion newBoneCS = math.normalize(math.mul(yawQ, boneCS));
            pose[bone].Rotation = math.normalize(math.mul(math.conjugate(parentCS), newBoneCS));
            parentCS = newBoneCS;
        }
    }
}
