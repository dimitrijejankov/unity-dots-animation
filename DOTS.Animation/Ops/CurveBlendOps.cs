using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Mathematics;

namespace AnimationSystem
{
    /// <summary>
    /// Operations for blending curve values stored as ATTR_ virtual bone data.
    /// Curve bones occupy indices [SkeletonBoneCount, BoneCount).
    /// Each ATTR_ bone holds 7 floats: float3 Position + quaternion Rotation.
    /// CurveCount is the number of named curves (may be less than bones * 7).
    /// </summary>
    [BurstCompile]
    public static class CurveBlendOps
    {
        /// <summary>
        /// Accumulate: result_curves[i] += source_curves[i] * weight.
        /// </summary>
        public static void BlendCurvesAccumulate(
            ref Motion motion,
            Span<RigidTransform> result,
            ReadOnlySpan<RigidTransform> source,
            float weight)
        {
            if (weight < 0.001f) return;
            int firstCurveBone = motion.SkeletonBoneCount;
            int curveCount = motion.CurveCount;
            if (curveCount <= 0) return;

            var r = AsFloats(result.Slice(firstCurveBone));
            var s = AsFloats(source.Slice(firstCurveBone));
            for (int i = 0; i < curveCount; i++) r[i] += s[i] * weight;
        }

        /// <summary>
        /// Override: result_curves[i] = lerp(result_curves[i], source_curves[i], weight).
        /// </summary>
        public static void BlendCurvesOverride(
            ref Motion motion,
            Span<RigidTransform> result,
            ReadOnlySpan<RigidTransform> source,
            float weight)
        {
            if (weight < 0.001f) return;
            int firstCurveBone = motion.SkeletonBoneCount;
            int curveCount = motion.CurveCount;
            if (curveCount <= 0) return;

            var r = AsFloats(result.Slice(firstCurveBone));
            var s = AsFloats(source.Slice(firstCurveBone));
            for (int i = 0; i < curveCount; i++) r[i] = math.lerp(r[i], s[i], weight);
        }

        private static unsafe Span<float> AsFloats(Span<RigidTransform> span)
            => new Span<float>(
                Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AddressOf(ref MemoryMarshal.GetReference(span)),
                span.Length * 7);

        private static unsafe ReadOnlySpan<float> AsFloats(ReadOnlySpan<RigidTransform> span)
            => new ReadOnlySpan<float>(
                Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AddressOf(ref MemoryMarshal.GetReference(span)),
                span.Length * 7);

        /// <summary>
        /// Curve Transfer:
        ///   result_curves[i] = basePose_curves[i] + overlayPose_curves[i]
        /// Replaces whatever mangled curve values the bone-blend pipeline left in result.
        /// </summary>
        public static void TransferCurves(
            ref Motion motion,
            Span<RigidTransform> result,
            ReadOnlySpan<RigidTransform> basePose,
            ReadOnlySpan<RigidTransform> overlayPose)
        {
            int firstCurveBone = motion.SkeletonBoneCount;
            int curveCount = motion.CurveCount;
            if (curveCount <= 0) return;

            var r = AsFloats(result.Slice(firstCurveBone));
            var b = AsFloats(basePose.Slice(firstCurveBone));
            var o = AsFloats(overlayPose.Slice(firstCurveBone));
            for (int i = 0; i < curveCount; i++) r[i] = b[i] + o[i];
        }
    }
}
