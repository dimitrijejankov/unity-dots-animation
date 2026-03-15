using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystem
{
    /// <summary>
    /// Skin is a subset of skeleton bones that drives a SkinnedMeshRenderer.
    /// </summary>
    public struct Skin
    {
        /// <summary>Root bone index in BonePose buffer.</summary>
        public int Root;
        /// <summary>Begin offset in Skeleton.SkinBindPoses.</summary>
        public int Begin;
        /// <summary>End offset in Skeleton.SkinBindPoses.</summary>
        public int End;
        /// <summary>Bounding box in object space.</summary>
        public AABB Bounds;
        /// <summary>Total number of bindings = End - Begin.</summary>
        public int Length => End - Begin;
    }

    /// <summary>
    /// Shared component data referencing the Skeleton blob asset.
    /// </summary>
    public struct SkeletonRef : ISharedComponentData, IEquatable<SkeletonRef>
    {
        public BlobAssetReference<Skeleton> Value;

        public void Dispose() { Value.Dispose(); }
        public bool Equals(SkeletonRef other) => Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }

    /// <summary>
    /// Shared component data referencing an Skeleton blob and a specific skin index.
    /// </summary>
    public struct SkinRef : ISharedComponentData, IEquatable<SkinRef>
    {
        public BlobAssetReference<Skeleton> Skeleton;
        public int SkinIndex;

        public void Dispose() { Skeleton.Dispose(); }
        public bool Equals(SkinRef other) => Skeleton == other.Skeleton && SkinIndex == other.SkinIndex;
        public override int GetHashCode() => (int)math.hash(new int2(Skeleton.GetHashCode(), SkinIndex));
    }

    /// <summary>
    /// Skeleton hierarchy metadata blob.
    /// </summary>
    public struct Skeleton
    {
        /// <summary>Bone names. May be empty if baked without names option.</summary>
        public BlobArray<FixedString64Bytes> BoneNames;
        /// <summary>Curve names. Stores the names of float curves sampled from animations.</summary>
        public BlobArray<FixedString64Bytes> CurveNames;
        /// <summary>
        /// Absolute flat-float index per curve into the pose viewed as float[].
        /// CurveFloatIndices[i] = boneIdx * 7 + floatOffset (0-2=pos.x/y/z, 3-6=rot.x/y/z/w).
        /// </summary>
        public BlobArray<int> CurveFloatIndices;
        /// <summary>Bone index of the first ATTR_ (curve storage) bone.</summary>
        public int FirstCurveBone;
        /// <summary>Number of named curves packed into ATTR_ bones.</summary>
        public int CurveCount;
        /// <summary>Parent index per bone (-1 for root bones).</summary>
        public BlobArray<int> BoneParentIndices;
        /// <summary>Skin subsets.</summary>
        public BlobArray<Skin> Skins;
        /// <summary>
        /// Flattened bind pose matrices (object → bone space).
        /// Use GetSkinBindPoses(Skin) to access per-skin.
        /// </summary>
        public BlobArray<float4x4> SkinBindPoses;
        /// <summary>
        /// Flattened bone indices per skin.
        /// Use GetSkinBoneIndices(Skin) to access per-skin.
        /// </summary>
        public BlobArray<int> SkinBoneIndices;

        public int BoneCount => BoneParentIndices.Length;

        public unsafe ReadOnlySpan<float4x4> GetSkinBindPoses(Skin skin)
        {
            return new ReadOnlySpan<float4x4>((float4x4*)SkinBindPoses.GetUnsafePtr() + skin.Begin, skin.Length);
        }

        public unsafe ReadOnlySpan<int> GetSkinBoneIndices(Skin skin)
        {
            return new ReadOnlySpan<int>((int*)SkinBoneIndices.GetUnsafePtr() + skin.Begin, skin.Length);
        }

        public int FindBoneIndex(in FixedString64Bytes name)
        {
            if (TryFindBoneIndex(name, out var boneIndex))
                return boneIndex;
            throw new InvalidOperationException($"Failed to find bone with name {name}");
        }

        public bool TryFindBoneIndex(in FixedString64Bytes name, out int index)
        {
            for (index = 0; index < BoneNames.Length; index++)
                if (BoneNames[index] == name)
                    return true;
            return false;
        }

        public int FindBoneIndex(string name)
        {
            if (TryFindBoneIndex(name, out var boneIndex))
                return boneIndex;
            throw new InvalidOperationException($"Failed to find bone with name {name}");
        }

        public bool TryFindBoneIndex(string name, out int index)
        {
            for (index = 0; index < BoneNames.Length; index++)
                if (BoneNames[index] == name)
                    return true;
            return false;
        }

        public int FindCurveIndex(string name)
        {
            if (TryFindCurveIndex(name, out var index))
                return index;
            throw new InvalidOperationException($"Failed to find curve with name {name}");
        }

        public bool TryFindCurveIndex(string name, out int index)
        {
            for (index = 0; index < CurveNames.Length; index++)
                if (CurveNames[index] == name)
                    return true;
            return false;
        }

        public bool TryFindCurve(string name, out int floatIndex)
        {
            if (TryFindCurveIndex(name, out var i))
            {
                floatIndex = CurveFloatIndices[i];
                return true;
            }
            floatIndex = -1;
            return false;
        }
    }

    /// <summary>
    /// Per-bone pose in parent space. Written by character system each frame.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct BonePose : IBufferElementData
    {
        public RigidTransform Value;
        public static BonePose Default => new BonePose { Value = RigidTransform.Identity };
    }

    /// <summary>
    /// Per-bone pose from the previous frame (parent space). Used for inertialization.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct BonePreviousPose : IBufferElementData
    {
        public RigidTransform Value;
        public static BonePreviousPose Default => new BonePreviousPose { Value = RigidTransform.Identity };
    }

    /// <summary>
    /// Per-bone inertia coefficients for smooth transition blending (GDC 2018 inertialization).
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct BoneInertia : IBufferElementData
    {
        public Float3Inertia Position;
        public QuaternionInertia Rotation;
        public static BoneInertia Default => new BoneInertia
        {
            Rotation = new QuaternionInertia { Axis = math.right() }
        };
    }

    /// <summary>
    /// Extension methods for accessing BonePose buffers as NativeArray spans.
    /// </summary>
    public static class PoseUtility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NativeArray<RigidTransform> AsTransformArray(this NativeArray<BonePose> pose) =>
            pose.Reinterpret<RigidTransform>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NativeArray<RigidTransform> AsTransformArray(this NativeArray<BonePreviousPose> pose) =>
            pose.Reinterpret<RigidTransform>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NativeArray<RigidTransform> AsTransformArray(this DynamicBuffer<BonePose> pose) =>
            pose.AsNativeArray().Reinterpret<RigidTransform>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NativeArray<RigidTransform> AsTransformArray(this DynamicBuffer<BonePreviousPose> pose) =>
            pose.AsNativeArray().Reinterpret<RigidTransform>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ResizeInitialized<T>(this DynamicBuffer<T> buffer, int length, T defaultValue) where T : unmanaged
        {
            buffer.ResizeUninitialized(length);
            for (int i = 0; i < length; i++)
                buffer[i] = defaultValue;
        }
    }
}
