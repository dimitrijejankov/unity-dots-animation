using System;
using Unity.Burst;
using Unity.Mathematics;
using RigidTransform = AnimationSystem.RigidTransform;

namespace AnimationSystem
{
    [BurstCompile]
    public static class AdditiveOps
    {
        #region Preprocessed Additive Methods (Recommended)

        /// <summary>
        /// Applies a preprocessed local space additive delta to the current pose.
        /// The delta should already be computed as: delta = base^-1 * additive
        /// Result = Current * Delta
        /// </summary>
        public static void ApplyLocalAdditive(
            ref Motion motion,
            Span<RigidTransform> pose,
            ReadOnlySpan<RigidTransform> preprocessedDelta,
            float weight)
        {
            ApplyLocalAdditive(pose, preprocessedDelta, weight, motion.SkeletonBoneCount);
        }

        /// <summary>
        /// Applies a preprocessed local space additive delta to the current pose.
        /// </summary>
        public static void ApplyLocalAdditive(
            Span<RigidTransform> pose,
            ReadOnlySpan<RigidTransform> preprocessedDelta,
            float weight,
            int skeletonBoneCount = -1)
        {
            if (weight < 0.001f)
                return;

            int count = skeletonBoneCount >= 0 ? skeletonBoneCount : pose.Length;
            for (int i = 0; i < count; i++)
            {
                RigidTransform delta = preprocessedDelta[i];

                if (weight < 0.999f)
                {
                    // Interpolate delta toward identity based on weight
                    delta.Position *= weight;
                    delta.Rotation = math.slerp(quaternion.identity, delta.Rotation, weight);
                }

                // Apply delta: Current * Delta
                pose[i] = pose[i].TransformTransform(delta);
            }
        }

        /// <summary>
        /// Applies a preprocessed mesh space additive delta to the current pose.
        /// The delta should contain: Rotation (in Component Space), Position (in Local Space).
        /// Mesh Space additives apply rotations in CS and translations in LS to prevent bone stretching.
        /// </summary>
        public static void ApplyMeshSpaceAdditive(
            ref Motion motion,
            Span<RigidTransform> pose,
            ReadOnlySpan<RigidTransform> preprocessedDeltaCS,
            ReadOnlySpan<int> parentIndices,
            float weight)
        {
            ApplyMeshSpaceAdditive(pose, preprocessedDeltaCS, parentIndices, weight, motion.SkeletonBoneCount);
        }

        /// <summary>
        /// Applies a preprocessed mesh space additive delta to the current pose.
        /// </summary>
        public static void ApplyMeshSpaceAdditive(
            Span<RigidTransform> pose,
            ReadOnlySpan<RigidTransform> preprocessedDeltaCS,
            ReadOnlySpan<int> parentIndices,
            float weight,
            int skeletonBoneCount = -1)
        {
            if (weight < 0.001f)
                return;

            int boneCount = skeletonBoneCount >= 0 ? skeletonBoneCount : pose.Length;
            Span<RigidTransform> baseCS = stackalloc RigidTransform[boneCount];
            Span<RigidTransform> resultCS = stackalloc RigidTransform[boneCount];

            // 1. First pass: Pre-calculate the ORIGINAL Mesh Space transforms (Base)
            for (int i = 0; i < boneCount; i++)
            {
                int parent = parentIndices[i];
                if (parent == -1) baseCS[i] = pose[i];
                else baseCS[i] = baseCS[parent].TransformTransform(pose[i]);
            }

            // 2. Second pass: Calculate NEW transforms hierarchically
            for (int i = 0; i < boneCount; i++)
            {
                RigidTransform delta = preprocessedDeltaCS[i];

                if (weight < 0.999f)
                {
                    delta.Position *= weight;
                    delta.Rotation = math.slerp(quaternion.identity, delta.Rotation, weight);
                }

                int parent = parentIndices[i];

                // a) Apply Mesh Space Rotation: DeltaCSRot * BaseCSRot
                // math.mul(delta, base) is left-multiplication, correctly applying the delta in Mesh Space
                quaternion targetCSRot = math.normalize(math.mul(delta.Rotation, baseCS[i].Rotation));

                // b) Get the new local translation (LS addition)
                float3 newLocalPos = pose[i].Position + delta.Position;

                // c) Convert target Mesh Space rotation back to Local Space relative to the NEW parent
                quaternion newLocalRot;
                if (parent == -1)
                {
                    newLocalRot = targetCSRot;
                    resultCS[i] = new RigidTransform { Position = newLocalPos, Rotation = newLocalRot };
                }
                else
                {
                    quaternion parentCSRot = resultCS[parent].Rotation;
                    newLocalRot = math.normalize(math.mul(math.conjugate(parentCSRot), targetCSRot));

                    // d) Store resultCS hierarchically for children to use
                    resultCS[i] = resultCS[parent].TransformTransform(new RigidTransform
                    {
                        Position = newLocalPos,
                        Rotation = newLocalRot
                    });
                }

                // e) Save final Local Transform back to the output pose
                pose[i] = new RigidTransform { Position = newLocalPos, Rotation = newLocalRot };
            }
        }

        #endregion

        #region Dynamic Additive Computation

        /// <summary>
        /// Computes a local-space additive delta between a base and a target pose.
        /// </summary>
        public static void MakeDynamicAdditiveLocal(
            ref Motion motion,
            ReadOnlySpan<RigidTransform> basePose,
            ReadOnlySpan<RigidTransform> targetPose,
            Span<RigidTransform> delta)
        {
            MakeDynamicAdditiveLocal(basePose, targetPose, delta, motion.SkeletonBoneCount);
        }

        /// <summary>
        /// Computes a local-space additive delta between a base and a target pose.
        /// delta[i] = base[i]^-1 * target[i]
        /// Apply with ApplyLocalAdditive to recover target from base.
        /// </summary>
        public static void MakeDynamicAdditiveLocal(
            ReadOnlySpan<RigidTransform> basePose,
            ReadOnlySpan<RigidTransform> targetPose,
            Span<RigidTransform> delta,
            int skeletonBoneCount = -1)
        {
            int count = skeletonBoneCount >= 0 ? skeletonBoneCount : basePose.Length;
            for (int i = 0; i < count; i++)
            {
                delta[i] = basePose[i].InverseTransformTransform(targetPose[i]);
            }
        }

        /// <summary>
        /// Computes a mesh-space additive delta between a base and a target pose.
        /// </summary>
        public static void MakeDynamicAdditiveMeshSpace(
            ref Motion motion,
            ReadOnlySpan<RigidTransform> basePose,
            ReadOnlySpan<RigidTransform> targetPose,
            ReadOnlySpan<int> parentIndices,
            Span<RigidTransform> deltaCS)
        {
            MakeDynamicAdditiveMeshSpace(basePose, targetPose, parentIndices, deltaCS, motion.SkeletonBoneCount);
        }

        /// <summary>
        /// Computes a mesh-space additive delta between a base and a target pose.
        /// Stores the CS Rotation delta and LS Translation delta for use with ApplyMeshSpaceAdditive.
        /// </summary>
        public static void MakeDynamicAdditiveMeshSpace(
            ReadOnlySpan<RigidTransform> basePose,
            ReadOnlySpan<RigidTransform> targetPose,
            ReadOnlySpan<int> parentIndices,
            Span<RigidTransform> deltaCS,
            int skeletonBoneCount = -1)
        {
            int boneCount = skeletonBoneCount >= 0 ? skeletonBoneCount : basePose.Length;
            Span<RigidTransform> baseCS   = stackalloc RigidTransform[boneCount];
            Span<RigidTransform> targetCS = stackalloc RigidTransform[boneCount];

            for (int i = 0; i < boneCount; i++)
            {
                int parent = parentIndices[i];
                if (parent == -1)
                {
                    baseCS[i]   = basePose[i];
                    targetCS[i] = targetPose[i];
                }
                else
                {
                    baseCS[i]   = baseCS[parent].TransformTransform(basePose[i]);
                    targetCS[i] = targetCS[parent].TransformTransform(targetPose[i]);
                }
            }

            // Rotation = target * conjugate(base) in CS, Position = target - base in LS
            for (int i = 0; i < boneCount; i++)
            {
                deltaCS[i] = new RigidTransform
                {
                    Rotation = math.normalize(math.mul(targetCS[i].Rotation, math.conjugate(baseCS[i].Rotation))),
                    Position = targetPose[i].Position - basePose[i].Position
                };
            }
        }

        #endregion
    }
}
