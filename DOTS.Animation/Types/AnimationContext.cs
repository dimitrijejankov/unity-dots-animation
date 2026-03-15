using System;
using AnimationSystem;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using RigidTransform = AnimationSystem.RigidTransform;

namespace AnimationSystem
{
    /// <summary>
    /// Encapsulates the timing and buffer data required for animation sampling and finalization.
    /// This is a ref struct to ensure it remains on the stack and provides high-performance access
    /// to spans without managed allocations.
    /// </summary>
    public ref struct AnimationContext<T> where T : unmanaged
    {
        public float DeltaTime;
        public Span<RigidTransform> Pose;
        public Span<BoneInertia<T>> Inertias;
        public Span<RigidTransform> ParentSpaceHistory;
        public Span<RigidTransform> PreviousParentSpaceHistory;
        public BlobAssetReference<Skeleton> Skeleton;

        /// <summary>
        /// Provides direct access to parent indices from the skeleton.
        /// Used for mesh-space additive blending and other hierarchical operations.
        /// </summary>
        public unsafe ReadOnlySpan<int> ParentIndices
        {
            get
            {
                ref var boneParentIndices = ref Skeleton.Value.BoneParentIndices;
                return new ReadOnlySpan<int>(boneParentIndices.GetUnsafePtr(), boneParentIndices.Length);
            }
        }
    }
}
