using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Assertions;

namespace AnimationSystem
{
    /// <summary>
    /// Shared component data referencing the Motion blob asset.
    /// </summary>
    public struct MotionRef : ISharedComponentData, IEquatable<MotionRef>
    {
        public BlobAssetReference<Motion> Value;

        public bool Equals(MotionRef other) => Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }

    /// <summary>
    /// Motion library containing all animation clip data.
    /// </summary>
    public struct Motion
    {
        public BlobArray<Animation> Animations;
        public BlobArray<FixedString64Bytes> AnimationNames;
        /// <summary>Flattened array of all bone transforms. Size = PoseCount * BoneCount.</summary>
        public BlobArray<RigidTransform> Transforms;
        /// <summary>Number of bones.</summary>
        public int BoneCount;
        /// <summary>
        /// Bone index of the first ATTR_ (curve) bone.
        /// Bones 0..SkeletonBoneCount-1 are skeleton bones blended with lerp+slerp.
        /// Bones SkeletonBoneCount..BoneCount-1 are curve storage bones blended as raw floats.
        /// </summary>
        public int SkeletonBoneCount;
        /// <summary>Number of poses (total frames across all clips).</summary>
        public int PoseCount;
        /// <summary>Bytes per single pose = BoneCount * sizeof(RigidTransform).</summary>
        public int PoseStride;
        /// <summary>Number of named curves packed into ATTR_ bones.</summary>
        public int CurveCount;

        public bool IsValidAnimationIndex(AnimationIndex index) =>
            index.Value >= 0 && index.Value < Animations.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetBoneTransformIndex(int boneIndex, int poseIndex)
        {
            CheckIndexInRange(boneIndex, BoneCount);
            CheckIndexInRange(poseIndex, PoseCount);
            return poseIndex * BoneCount + boneIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RigidTransform GetBoneLocalToParentTransform(int boneIndex, int poseIndex)
        {
            CheckIndexInRange(boneIndex, BoneCount);
            CheckIndexInRange(poseIndex, PoseCount);
            return Transforms[poseIndex * BoneCount + boneIndex];
        }

        public float GetNormalizedTime(AnimationIndex animationIndex, float time)
        {
            var animation = Animations[animationIndex.m_Value];
            if (animation.IsLooping)
                return time % animation.Time;
            else
                return math.saturate(time / animation.Time);
        }

        public bool TryFindAnimationIndex(in FixedString64Bytes name, out AnimationIndex index)
        {
            for (index.m_Value = 0; index.m_Value < AnimationNames.Length; index.m_Value++)
            {
                if (AnimationNames[index.m_Value] == name)
                    return true;
            }
            return false;
        }

        public AnimationIndex FindAnimationIndex(in FixedString64Bytes name)
        {
            for (int index = 0; index < AnimationNames.Length; index++)
            {
                if (AnimationNames[index] == name)
                    return AnimationIndex.FromIndex(index);
            }
            throw new InvalidOperationException($"Failed to find animation with name {name}");
        }

        public struct PoseTime
        {
            public int AnimationIndex;
            public int PoseIndex;
            public float Theta;
        }

        public struct Animation
        {
            public int Begin;
            public int End;
            public float FrameRate;
            public bool IsLooping;
            public float Speed;
            public float Time => Length / FrameRate;
            public int Length => End - Begin;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CheckIndexInRange(int index, int length)
        {
            if ((uint)index >= (uint)length)
                throw new IndexOutOfRangeException($"Index {index} is out of range in container of '{length}' Length.");
        }
    }
}
