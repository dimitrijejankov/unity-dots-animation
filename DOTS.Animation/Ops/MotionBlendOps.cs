using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Mathematics;

namespace AnimationSystem
{
    /// <summary>
    /// Static pose blending operations extracted from MotionBlob.
    /// These are pure math operations that don't depend on blob internals.
    /// Skeleton bones (0..skeletonBoneCount-1) are blended with lerp+slerp.
    /// Curve bones (skeletonBoneCount..totalBoneCount-1) are blended as raw floats.
    /// </summary>
    [BurstCompile]
    public static class MotionBlendOps
    {
        /// <summary>
        /// Blend between two poses into a result pose: result = lerp(start, end, alpha)
        /// </summary>
        public static void Blend(
            ref Motion motion,
            Span<RigidTransform> result,
            ReadOnlySpan<RigidTransform> start,
            ReadOnlySpan<RigidTransform> end,
            float alpha)
        {
            int skeletonBoneCount = motion.SkeletonBoneCount;
            int totalBoneCount = motion.BoneCount;

            for (int i = 0; i < skeletonBoneCount; i++)
            {
                result[i] = new RigidTransform
                {
                    Position = math.lerp(start[i].Position, end[i].Position, alpha),
                    Rotation = math.slerp(start[i].Rotation, end[i].Rotation, alpha),
                };
            }
            if (totalBoneCount > skeletonBoneCount)
            {
                var r = AsFloats(result.Slice(skeletonBoneCount, totalBoneCount - skeletonBoneCount));
                var s = AsFloats(start.Slice(skeletonBoneCount, totalBoneCount - skeletonBoneCount));
                var e = AsFloats(end.Slice(skeletonBoneCount, totalBoneCount - skeletonBoneCount));
                for (int i = 0; i < r.Length; i++) r[i] = math.lerp(s[i], e[i], alpha);
            }
        }

        /// <summary>
        /// Blend between two poses in-place: source = lerp(source, target, alpha)
        /// </summary>
        public static void Blend(
            ref Motion motion,
            Span<RigidTransform> source,
            ReadOnlySpan<RigidTransform> target,
            float alpha)
        {
            int skeletonBoneCount = motion.SkeletonBoneCount;
            int totalBoneCount = motion.BoneCount;

            for (int i = 0; i < skeletonBoneCount; i++)
            {
                source[i] = new RigidTransform
                {
                    Position = math.lerp(source[i].Position, target[i].Position, alpha),
                    Rotation = math.slerp(source[i].Rotation, target[i].Rotation, alpha),
                };
            }
            if (totalBoneCount > skeletonBoneCount)
            {
                var s = AsFloats(source.Slice(skeletonBoneCount, totalBoneCount - skeletonBoneCount));
                var t = AsFloats(target.Slice(skeletonBoneCount, totalBoneCount - skeletonBoneCount));
                for (int i = 0; i < s.Length; i++) s[i] = math.lerp(s[i], t[i], alpha);
            }
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
        /// Blend between three poses with weights.
        /// source = gamma*source + alpha*target1 + beta*target2 (normalized)
        /// </summary>
        public static void Blend3(
            ref Motion motion,
            Span<RigidTransform> source,
            ReadOnlySpan<RigidTransform> target1,
            ReadOnlySpan<RigidTransform> target2,
            float gamma,
            float alpha,
            float beta)
        {
            int skeletonBoneCount = motion.SkeletonBoneCount;
            int totalBoneCount = motion.BoneCount;

            var t0 = (alpha + gamma) != 0 ? alpha / (alpha + gamma) : 0;
            var t1 = (alpha + beta + gamma) != 0 ? beta / (alpha + beta + gamma) : 0;

            for (int i = 0; i < skeletonBoneCount; i++)
            {
                source[i] = new RigidTransform
                {
                    Position = gamma * source[i].Position
                             + alpha * target1[i].Position
                             + beta * target2[i].Position,
                    Rotation = math.slerp(
                        math.slerp(source[i].Rotation, target1[i].Rotation, t0),
                        target2[i].Rotation, t1),
                };
            }
            if (totalBoneCount > skeletonBoneCount)
            {
                var s = AsFloats(source.Slice(skeletonBoneCount, totalBoneCount - skeletonBoneCount));
                var t1s = AsFloats(target1.Slice(skeletonBoneCount, totalBoneCount - skeletonBoneCount));
                var t2s = AsFloats(target2.Slice(skeletonBoneCount, totalBoneCount - skeletonBoneCount));
                for (int i = 0; i < s.Length; i++) s[i] = gamma * s[i] + alpha * t1s[i] + beta * t2s[i];
            }
        }
    }
}
