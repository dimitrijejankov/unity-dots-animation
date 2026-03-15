using System;
using AnimationSystem;
using Motion = AnimationSystem.Motion;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace AnimationSystem
{
    /// <summary>
    /// Standalone rig asset that stores serialized Motion and Skeleton.
    /// </summary>
    [PreferBinarySerialization]
    [CreateAssetMenu(fileName = "BendzRig", menuName = "TorchlitTales/Bendz Rig")]
    public unsafe class BendzRig : ScriptableObject
    {
        public const int DefaultVersion = 1;

        [SerializeField, HideInInspector]
        byte[] m_MotionData;
        [SerializeField, HideInInspector]
        byte[] m_SkeletonData;
        [SerializeField]
        int m_Version = DefaultVersion;

        BlobAssetReference<Motion> m_Motion;
        BlobAssetReference<Skeleton> m_Skeleton;

        public int Version => m_Version;
        public bool IsCreated => m_Motion.IsCreated && m_Skeleton.IsCreated;
        public int TotalBytes => (m_MotionData?.Length ?? 0) + (m_SkeletonData?.Length ?? 0);

        void OnDisable() => Clear();
        void OnValidate() => Clear();

        public void Clear()
        {
            if (m_Motion.IsCreated) { m_Motion.Dispose(); m_Motion = default; }
            if (m_Skeleton.IsCreated) { m_Skeleton.Dispose(); m_Skeleton = default; }
        }

        public BlobAssetReference<Motion> GetOrCreateMotion()
        {
            if (!m_Motion.IsCreated)
                m_Motion = ReadMotion();
            return m_Motion;
        }

        public BlobAssetReference<Skeleton> GetOrCreateSkeleton()
        {
            if (!m_Skeleton.IsCreated)
                m_Skeleton = ReadSkeleton();
            return m_Skeleton;
        }

        public void WriteMotion(BlobBuilder builder)
        {
            using var stream = new MemoryBinaryWriter();
            BlobAssetReference<Motion>.Write(stream, builder, 1);
            m_MotionData = new byte[stream.Length];
            fixed (byte* ptr = m_MotionData)
                UnsafeUtility.MemCpy(ptr, stream.Data, stream.Length);
        }

        public void WriteSkeleton(BlobBuilder builder)
        {
            using var stream = new MemoryBinaryWriter();
            BlobAssetReference<Skeleton>.Write(stream, builder, 1);
            m_SkeletonData = new byte[stream.Length];
            fixed (byte* ptr = m_SkeletonData)
                UnsafeUtility.MemCpy(ptr, stream.Data, stream.Length);
        }

        public BlobAssetReference<Motion> ReadMotion()
        {
            if (m_MotionData == null) return default;
            fixed (byte* ptr = m_MotionData)
            {
                using var stream = new MemoryBinaryReader(ptr, m_MotionData.Length);
                BlobAssetReference<Motion>.TryRead(stream, 1, out var result);
                return result;
            }
        }

        public BlobAssetReference<Skeleton> ReadSkeleton()
        {
            if (m_SkeletonData == null) return default;
            fixed (byte* ptr = m_SkeletonData)
            {
                using var stream = new MemoryBinaryReader(ptr, m_SkeletonData.Length);
                BlobAssetReference<Skeleton>.TryRead(stream, 1, out var result);
                return result;
            }
        }

        public bool TryFindAnimationIndex(string name, out AnimationIndex index)
        {
            var motion = GetOrCreateMotion();
            if (!motion.IsCreated) { index = AnimationIndex.Default; return false; }
            return motion.Value.TryFindAnimationIndex(name, out index);
        }

        public bool TryFindBoneIndex(string name, out int index)
        {
            var skeleton = GetOrCreateSkeleton();
            if (!skeleton.IsCreated) { index = -1; return false; }
            return skeleton.Value.TryFindBoneIndex(name, out index);
        }

        public bool TryFindCurve(string name, out int floatIndex)
        {
            var skeleton = GetOrCreateSkeleton();
            if (!skeleton.IsCreated) { floatIndex = -1; return false; }
            return skeleton.Value.TryFindCurve(name, out floatIndex);
        }
    }
}
