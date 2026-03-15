using System;
using Unity.Burst;
using Unity.Mathematics;
using AnimationSystem;

namespace AnimationSystem.Ops
{
    [BurstCompile]
    public static class PoseOps
    {
        // --- Sampling ---

        public static Motion.PoseTime GetPoseTime(ref Motion motion, AnimationIndex animationIndex, float time)
        {
            var animation = motion.Animations[animationIndex.m_Value];
            var frame = (time * animation.FrameRate) * animation.Speed;
            if (animation.IsLooping)
                frame = frame % animation.Length;
            else
                frame = math.clamp(frame, 0, animation.Length - 1);
            frame += animation.Begin;
            return new Motion.PoseTime
            {
                AnimationIndex = animationIndex.m_Value,
                PoseIndex = (int)math.floor(frame),
                Theta = math.frac(frame),
            };
        }

        public static Motion.PoseTime GetPoseTime(ref Motion motion, AnimationIndex animationIndex, int frame)
        {
            var animation = motion.Animations[animationIndex.m_Value];
            if (animation.IsLooping)
                frame = frame % animation.Length;
            else
                frame = math.clamp(frame, 0, animation.Length - 1);
            frame += animation.Begin;
            return new Motion.PoseTime
            {
                AnimationIndex = animationIndex.m_Value,
                PoseIndex = frame,
            };
        }

        public static void SamplePose(ref Motion motion, in Motion.PoseTime time, SamplingMode interpolation, Span<RigidTransform> result)
        {
            switch (interpolation)
            {
                case SamplingMode.Interpolated:
                    SamplePoseLinear(ref motion, time, result);
                    break;
                default: // Nearest
                    SamplePose(ref motion, time, result);
                    break;
            }
        }

        public static void SamplePoseAtTime(ref Motion motion, AnimationIndex animationIndex, float time, SamplingMode interpolation, Span<RigidTransform> result)
        {
            var poseTime = GetPoseTime(ref motion, animationIndex, time);
            SamplePose(ref motion, poseTime, interpolation, result);
        }

        public static void SamplePoseAtFrame(ref Motion motion, AnimationIndex animationIndex, int frame, SamplingMode interpolation, Span<RigidTransform> result)
        {
            var poseTime = GetPoseTime(ref motion, animationIndex, frame);
            SamplePose(ref motion, poseTime, interpolation, result);
        }

        public static unsafe void SamplePose(ref Motion motion, in Motion.PoseTime time, Span<RigidTransform> result)
        {
            Unity.Collections.LowLevel.Unsafe.UnsafeUtility.MemCpy(
                Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AddressOf(ref result.GetPinnableReference()),
                (byte*)motion.Transforms.GetUnsafePtr() + time.PoseIndex * motion.PoseStride,
                motion.PoseStride);
        }

        public static void SamplePoseLinear(ref Motion motion, in Motion.PoseTime time, Span<RigidTransform> result)
        {
            var animation = motion.Animations[time.AnimationIndex];
            int nextPoseIndex;
            if (animation.IsLooping)
                nextPoseIndex = math.max(time.PoseIndex + 1 - animation.Begin, 0) % animation.Length + animation.Begin;
            else
                nextPoseIndex = math.clamp(time.PoseIndex + 1, animation.Begin, animation.End - 1);

            // Skeleton bones: lerp position + slerp rotation
            for (int boneIndex = 0; boneIndex < motion.SkeletonBoneCount; boneIndex++)
            {
                RigidTransform lhs = motion.GetBoneLocalToParentTransform(boneIndex, time.PoseIndex);
                RigidTransform rhs = motion.GetBoneLocalToParentTransform(boneIndex, nextPoseIndex);
                result[boneIndex] = new RigidTransform
                {
                    Position = math.lerp(lhs.Position, rhs.Position, time.Theta),
                    Rotation = math.slerp(lhs.Rotation, rhs.Rotation, time.Theta),
                };
            }
            // Curve bones: raw float lerp on all 7 floats (quaternion stores arbitrary float data)
            for (int boneIndex = motion.SkeletonBoneCount; boneIndex < motion.BoneCount; boneIndex++)
            {
                RigidTransform lhs = motion.GetBoneLocalToParentTransform(boneIndex, time.PoseIndex);
                RigidTransform rhs = motion.GetBoneLocalToParentTransform(boneIndex, nextPoseIndex);
                result[boneIndex] = new RigidTransform
                {
                    Position = math.lerp(lhs.Position, rhs.Position, time.Theta),
                    Rotation = new quaternion(math.lerp(lhs.Rotation.value, rhs.Rotation.value, time.Theta)),
                };
            }
        }

        public static unsafe void SampleBlendSpace1D(ref Motion motion, ReadOnlySpan<AnimationIndex> animations, ReadOnlySpan<float> positions, float position, float time, Span<RigidTransform> result)
        {
            if (positions[0] >= position)
            {
                SamplePose(ref motion, GetPoseTime(ref motion, animations[0], time), SamplingMode.Interpolated, result);
                return;
            }
            for (int i = 1; i < animations.Length; i++)
            {
                var lower = positions[i - 1];
                var upper = positions[i];
                if (lower <= position && position < upper)
                {
                    SamplePose(ref motion, GetPoseTime(ref motion, animations[i - 1], time), SamplingMode.Interpolated, result);
                    Span<RigidTransform> targetPose = stackalloc RigidTransform[motion.BoneCount];
                    SamplePose(ref motion, GetPoseTime(ref motion, animations[i], time), SamplingMode.Interpolated, targetPose);
                    float alpha = (position - lower) / (upper - lower);
                    MotionBlendOps.Blend(ref motion, result, targetPose, alpha);
                    return;
                }
            }
            SamplePose(ref motion, GetPoseTime(ref motion, animations[animations.Length - 1], time), SamplingMode.Interpolated, result);
        }

        public static unsafe void SampleBlendSpace2D(ref Motion motion, BlendSpace2D blendSpace, float2 position, float time, Span<RigidTransform> result)
        {
            var r = blendSpace.Evaluate(position);
            SamplePose(ref motion, GetPoseTime(ref motion, r.Item1, time), SamplingMode.Interpolated, result);
            Span<RigidTransform> pose1 = stackalloc RigidTransform[motion.BoneCount];
            SamplePose(ref motion, GetPoseTime(ref motion, r.Item2, time), SamplingMode.Interpolated, pose1);
            Span<RigidTransform> pose2 = stackalloc RigidTransform[motion.BoneCount];
            SamplePose(ref motion, GetPoseTime(ref motion, r.Item3, time), SamplingMode.Interpolated, pose2);
            MotionBlendOps.Blend3(ref motion, result, pose1, pose2, r.Item4.x, r.Item4.y, r.Item4.z);
        }

        // --- Component space (from StopState consolidation) ---

        /// <summary>
        /// Computes mesh-space rotations for all bones, top-down from root.
        /// </summary>
        public static void ComputeComponentSpaceRotations(
            ReadOnlySpan<RigidTransform> pose,
            ReadOnlySpan<int> parentIndices,
            Span<quaternion> meshRot)
        {
            for (int i = 0; i < pose.Length; i++)
            {
                int parent = parentIndices[i];
                meshRot[i] = parent >= 0
                    ? math.mul(meshRot[parent], pose[i].Rotation)
                    : pose[i].Rotation;
            }
        }

        /// <summary>
        /// Converts a component-space rotation to a local-space rotation relative to its parent.
        /// </summary>
        public static quaternion ComponentSpaceToLocal(quaternion componentRot, quaternion parentComponentRot)
        {
            return math.normalize(math.mul(math.conjugate(parentComponentRot), componentRot));
        }

        /// <summary>
        /// Sets a bone's rotation in component space (from a target pose), converts back to local
        /// using the parent's component-space rotation, and copies position from target.
        /// Performs a blend with the current result pose.
        /// </summary>
        public static void ApplyBoneComponentSpaceOverride(
            Span<RigidTransform> result,
            ReadOnlySpan<RigidTransform> targetPose,
            quaternion parentComponentRot,
            quaternion targetComponentRot,
            int boneIdx,
            float alpha)
        {
            var targetBone = targetPose[boneIdx];
            var currentBone = result[boneIdx];

            quaternion targetLocalRot = ComponentSpaceToLocal(targetComponentRot, parentComponentRot);

            // Blend the override influence
            currentBone.Rotation = math.slerp(currentBone.Rotation, targetLocalRot, alpha);
            currentBone.Position = math.lerp(currentBone.Position, targetBone.Position, alpha);

            result[boneIdx] = currentBone;
        }
    }
}
