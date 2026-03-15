using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystem.Ops
{
    [BurstCompile]
    public static class CurveOps
    {
        /// <summary>Number of floats in one RigidTransform (float3 pos + quaternion rot = 7).</summary>
        public const int FloatsPerBone = 7;

        public static float EvaluateCurve(ref BlobArray<CurveKeyFrame> curve, float time)
        {
            if (curve.Length == 0) return 0;
            if (curve.Length == 1) return curve[0].Value;

            var count = curve.Length;
            if (time <= curve[0].Time) return curve[0].Value;
            if (time >= curve[count - 1].Time) return curve[count - 1].Value;

            // Binary search for the keys
            var low = 0;
            var high = count - 1;
            while (low < high - 1)
            {
                var mid = (low + high) / 2;
                if (time < curve[mid].Time) high = mid;
                else low = mid;
            }

            // Hermite interpolation
            var k0 = curve[low];
            var k1 = curve[high];

            var dt = k1.Time - k0.Time;
            if (dt <= 0.0001f) return k0.Value;

            var t = (time - k0.Time) / dt;
            var t2 = t * t;
            var t3 = t2 * t;

            var m0 = k0.OutTangent * dt;
            var m1 = k1.InTangent * dt;

            var a = 2 * t3 - 3 * t2 + 1;
            var b = t3 - 2 * t2 + t;
            var c = t3 - t2;
            var d = -2 * t3 + 3 * t2;

            return a * k0.Value + b * m0 + c * m1 + d * k1.Value;
        }

        private static unsafe Span<float> AsFloats(Span<RigidTransform> span)
            => new Span<float>(
                Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AddressOf(ref MemoryMarshal.GetReference(span)),
                span.Length * FloatsPerBone);

        private static unsafe ReadOnlySpan<float> AsFloats(ReadOnlySpan<RigidTransform> span)
            => new ReadOnlySpan<float>(
                Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AddressOf(ref MemoryMarshal.GetReference(span)),
                span.Length * FloatsPerBone);

        public static float GetCurveValue(ReadOnlySpan<RigidTransform> pose, CurveIndices curve)
        {
            if (!curve.IsValid) return 0;
            return AsFloats(pose)[curve.Index];
        }

        public static void SetCurveValue(Span<RigidTransform> pose, CurveIndices curve, float value)
        {
            if (!curve.IsValid) return;
            AsFloats(pose)[curve.Index] = value;
        }

        /// <summary>
        /// View the curve region of a pose as a contiguous float span.
        /// </summary>
        public static ReadOnlySpan<float> AsCurveSpan(ref Motion motion, ReadOnlySpan<RigidTransform> pose)
        {
            if (motion.CurveCount <= 0) return ReadOnlySpan<float>.Empty;
            return AsFloats(pose.Slice(motion.SkeletonBoneCount)).Slice(0, motion.CurveCount);
        }

        /// <summary>
        /// View the curve region of a pose as a contiguous float span (writable).
        /// </summary>
        public static Span<float> AsCurveSpan(ref Motion motion, Span<RigidTransform> pose)
        {
            if (motion.CurveCount <= 0) return Span<float>.Empty;
            return AsFloats(pose.Slice(motion.SkeletonBoneCount)).Slice(0, motion.CurveCount);
        }

        /// <summary>
        /// View the curve region of a pose as a contiguous float span.
        /// </summary>
        public static ReadOnlySpan<float> AsCurveSpan(ReadOnlySpan<RigidTransform> pose, int firstCurveBone, int curveCount)
            => AsFloats(pose.Slice(firstCurveBone)).Slice(0, curveCount);

        /// <summary>
        /// View the curve region of a pose as a contiguous float span (writable).
        /// </summary>
        public static Span<float> AsCurveSpan(Span<RigidTransform> pose, int firstCurveBone, int curveCount)
            => AsFloats(pose.Slice(firstCurveBone)).Slice(0, curveCount);
    }
}
