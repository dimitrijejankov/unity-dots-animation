using System;
using AnimationSystem;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using RigidTransform = AnimationSystem.RigidTransform;

namespace AnimationSystem.Ops
{
    /// <summary>
    /// Shared lean logic used by both normal and crouch locomotion cycles.
    /// Samples a 5-point blend space to produce an additive lean delta.
    /// </summary>
    [BurstCompile]
    public static class LeanOps
    {
        /// <summary>
        /// Samples the 5-point lean blend space (Center, Forward, Backward, Left, Right)
        /// using the provided lean amount. The resulting additive pose is stored in leanDelta.
        /// The caller is responsible for calculating/interpolating the leanAmount and applying the result.
        /// </summary>
        public static void SampleLeanDelta(
            ref Motion motion,
            float2 leanAmount,
            AnimationIndex leanPose0,
            AnimationIndex leanPoseF,
            AnimationIndex leanPoseB,
            AnimationIndex leanPoseL,
            AnimationIndex leanPoseR,
            Span<RigidTransform> leanDelta)
        {
            var builder = new BlendSpace2DBuilder(5, Allocator.Temp);
            builder.Add(leanPose0, new float2(0, 0));
            builder.Add(leanPoseF, new float2(0, 1));
            builder.Add(leanPoseB, new float2(0, -1));
            builder.Add(leanPoseL, new float2(-1, 0));
            builder.Add(leanPoseR, new float2(1, 0));

            var leanBlendSpace = builder.Create();
            var leanResult = BlendOps.EvaluateBlendSpace2D(leanBlendSpace, leanAmount);
            BlendOps.SampleTriangle(ref motion, leanResult, 0.0f, leanDelta);
            leanBlendSpace.Dispose();
        }
    }
}
