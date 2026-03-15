using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Mathematics;
using AnimationSystem.Ops;

namespace AnimationSystem
{
    // Blend result types
    public struct BlendResult1D
    {
        public AnimationIndex Animation0;
        public AnimationIndex Animation1;
        public float Weight;
    }

    public struct BlendResult2D
    {
        public AnimationIndex Animation0;
        public AnimationIndex Animation1;
        public AnimationIndex Animation2;
        public float3 Weights;
    }

    [BurstCompile]
    public static class BlendOps
    {
        public static BlendResult2D EvaluateBlendSpace2D(BlendSpace2D blendSpace, float2 position)
        {
            var r = blendSpace.Evaluate(position);
            return new BlendResult2D
            {
                Animation0 = r.Item1,
                Animation1 = r.Item2,
                Animation2 = r.Item3,
                Weights = r.Item4
            };
        }

        public static unsafe void SampleTriangle(ref Motion motion, in BlendResult2D blend, float time,
            Span<RigidTransform> result)
        {
            PoseOps.SamplePoseAtTime(ref motion, blend.Animation0, time, SamplingMode.Interpolated, result);

            if (blend.Animation1 != AnimationIndex.Default)
            {
                Span<RigidTransform> pose1 = stackalloc RigidTransform[motion.BoneCount];
                PoseOps.SamplePoseAtTime(ref motion, blend.Animation1, time, SamplingMode.Interpolated, pose1);

                if (blend.Animation2 != AnimationIndex.Default)
                {
                    Span<RigidTransform> pose2 = stackalloc RigidTransform[motion.BoneCount];
                    PoseOps.SamplePoseAtTime(ref motion, blend.Animation2, time, SamplingMode.Interpolated, pose2);

                    MotionBlendOps.Blend3(ref motion, result, pose1, pose2, blend.Weights.x, blend.Weights.y, blend.Weights.z);
                }
                else
                {
                    MotionBlendOps.Blend(ref motion, result, pose1, blend.Weights.y);
                }
            }
        }

        public static BlendResult2D SampleBlendSpace2D(ref Motion motion, BlendSpace2D blendSpace, float2 position,
            float time, Span<RigidTransform> result)
        {
            var blend = EvaluateBlendSpace2D(blendSpace, position);
            SampleTriangle(ref motion, blend, time, result);
            return blend;
        }

        public static unsafe BlendResult2D SampleSynchronizedBlendSpace2D(ref Motion motion, BlendSpace2D blendSpace,
            float2 position,
            ref float normalizedPhase, float deltaTime, float playRate, Span<RigidTransform> result)
        {
            var blend = EvaluateBlendSpace2D(blendSpace, position);

            // Calculate blended duration to advance phase
            var blendedDuration = GetBlendSpace2DDuration(ref motion, blend);

            if (blendedDuration > 0.001f)
                normalizedPhase = math.frac(normalizedPhase + deltaTime * playRate / blendedDuration);

            // Sample and blend
            SampleBlendSpace2DAtPhase(ref motion, blend, normalizedPhase, result);

            return blend;
        }

        public static float GetBlendSpace2DDuration(ref Motion motion, in BlendResult2D blend)
        {
            float blendedDuration = 0;
            if (blend.Animation0 != AnimationIndex.Default)
            {
                var anim0 = motion.Animations[blend.Animation0.m_Value];
                blendedDuration += anim0.Length / anim0.FrameRate * blend.Weights.x;
            }

            if (blend.Animation1 != AnimationIndex.Default)
            {
                var anim1 = motion.Animations[blend.Animation1.m_Value];
                blendedDuration += anim1.Length / anim1.FrameRate * blend.Weights.y;
            }

            if (blend.Animation2 != AnimationIndex.Default)
            {
                var anim2 = motion.Animations[blend.Animation2.m_Value];
                blendedDuration += anim2.Length / anim2.FrameRate * blend.Weights.z;
            }

            return blendedDuration;
        }

        public static unsafe void SampleBlendSpace2DAtPhase(ref Motion motion, in BlendResult2D blend,
            float normalizedPhase, Span<RigidTransform> result)
        {
            SampleSynchronizedPose(ref motion, blend.Animation0, normalizedPhase, result);

            if (blend.Animation1 != AnimationIndex.Default)
            {
                Span<RigidTransform> pose1 = stackalloc RigidTransform[motion.BoneCount];
                SampleSynchronizedPose(ref motion, blend.Animation1, normalizedPhase, pose1);

                if (blend.Animation2 != AnimationIndex.Default)
                {
                    Span<RigidTransform> pose2 = stackalloc RigidTransform[motion.BoneCount];
                    SampleSynchronizedPose(ref motion, blend.Animation2, normalizedPhase, pose2);

                    MotionBlendOps.Blend3(ref motion, result, pose1, pose2, blend.Weights.x, blend.Weights.y, blend.Weights.z);
                }
                else
                {
                    MotionBlendOps.Blend(ref motion, result, pose1, blend.Weights.y);
                }
            }
        }

        #region Layered Blend Per Bone

        /// <summary>
        /// Returns the hierarchy distance from boneIdx up to rootIdx, or -1 if boneIdx is not
        /// a descendant of rootIdx (including rootIdx itself, which returns distance 0).
        /// </summary>
        public static int GetDepthFromRoot(int boneIdx, int rootIdx, ReadOnlySpan<int> parentIndices)
        {
            if (boneIdx == rootIdx)
                return 0;

            var current = boneIdx;
            var depth = 0;
            while (current != -1)
            {
                current = parentIndices[current];
                depth++;
                if (current == rootIdx)
                    return depth;
            }

            return -1;
        }

        /// <summary>
        /// Per-bone weighted lerp in local space.
        /// blendDepth == -1: only the root bone is blended (weight 1.0).
        /// blendDepth == 0:  all descendants get full weight immediately.
        /// blendDepth > 0:   weight ramps up over blendDepth bones from the root.
        /// </summary>
        public static void ApplyLayeredBlendLocal(
            Span<RigidTransform> basePose,
            ReadOnlySpan<RigidTransform> overlayPose,
            int rootBoneIndex,
            int blendDepth,
            float globalWeight,
            ReadOnlySpan<int> parentIndices)
        {
            if (globalWeight < 0.001f || rootBoneIndex < 0)
                return;

            if (blendDepth == -1)
            {
                // Only root bone
                basePose[rootBoneIndex] = new RigidTransform
                {
                    Position = math.lerp(basePose[rootBoneIndex].Position, overlayPose[rootBoneIndex].Position,
                        globalWeight),
                    Rotation = math.slerp(basePose[rootBoneIndex].Rotation, overlayPose[rootBoneIndex].Rotation,
                        globalWeight)
                };
                return;
            }

            var increment = blendDepth > 0 ? 1f / blendDepth : 1f;

            for (var i = 0; i < basePose.Length; i++)
            {
                var distance = GetDepthFromRoot(i, rootBoneIndex, parentIndices);
                if (distance == -1) continue;

                var weight = math.saturate(increment * (distance + 1)) * globalWeight;
                if (weight < 0.001f) continue;

                basePose[i] = new RigidTransform
                {
                    Position = math.lerp(basePose[i].Position, overlayPose[i].Position, weight),
                    Rotation = math.slerp(basePose[i].Rotation, overlayPose[i].Rotation, weight)
                };
            }
        }

        /// <summary>
        /// Per-bone weighted blend with rotations blended in component/mesh space,
        /// and translations blended in local space.
        /// Unblended bones naturally follow their parents without deformation.
        /// </summary>
        public static void ApplyLayeredBlendMeshSpace(
            Span<RigidTransform> basePose,
            ReadOnlySpan<RigidTransform> overlayPose,
            int rootBoneIndex,
            int blendDepth,
            float globalWeight,
            ReadOnlySpan<int> parentIndices)
        {
            if (globalWeight < 0.001f || rootBoneIndex < 0)
                return;

            var boneCount = basePose.Length;
            Span<RigidTransform> baseCS = stackalloc RigidTransform[boneCount];
            Span<RigidTransform> overlayCS = stackalloc RigidTransform[boneCount];
            Span<float> boneWeights = stackalloc float[boneCount];

            // 1. Build component-space poses and compute weights
            for (var i = 0; i < boneCount; i++)
            {
                var parent = parentIndices[i];
                if (parent == -1)
                {
                    baseCS[i] = basePose[i];
                    overlayCS[i] = overlayPose[i];
                }
                else
                {
                    baseCS[i] = baseCS[parent].TransformTransform(basePose[i]);
                    overlayCS[i] = overlayCS[parent].TransformTransform(overlayPose[i]);
                }

                // Compute weight
                if (blendDepth == -1)
                {
                    boneWeights[i] = (i == rootBoneIndex) ? globalWeight : 0f;
                }
                else
                {
                    var distance = GetDepthFromRoot(i, rootBoneIndex, parentIndices);
                    if (distance == -1)
                    {
                        boneWeights[i] = 0f;
                    }
                    else
                    {
                        var increment = blendDepth > 0 ? 1f / blendDepth : 1f;
                        boneWeights[i] = math.saturate(increment * (distance + 1)) * globalWeight;
                    }
                }
            }

            // 2. Blend hierarchically
            Span<RigidTransform> resultCS = stackalloc RigidTransform[boneCount];

            for (var i = 0; i < boneCount; i++)
            {
                var parent = parentIndices[i];
                var weight = boneWeights[i];

                RigidTransform newLocal;

                if (weight < 0.001f)
                {
                    // Unblended bone: strictly preserve its original local transform
                    newLocal = basePose[i];
                }
                else
                {
                    // Translation is blended in Local Space to preserve bone lengths
                    float3 newLocalPos = math.lerp(basePose[i].Position, overlayPose[i].Position, weight);

                    // Rotation is blended in Component/Mesh Space
                    quaternion blendedCSRot = math.normalize(math.slerp(baseCS[i].Rotation, overlayCS[i].Rotation, weight));

                    // Convert blended CS Rotation back to Local Rotation
                    quaternion newLocalRot;
                    if (parent == -1)
                    {
                        newLocalRot = blendedCSRot;
                    }
                    else
                    {
                        // LocalRot = Parent_New_CS_Rot^-1 * Child_New_CS_Rot
                        quaternion parentCSRot = resultCS[parent].Rotation;
                        newLocalRot = PoseOps.ComponentSpaceToLocal(blendedCSRot, parentCSRot);
                    }

                    newLocal = new RigidTransform { Position = newLocalPos, Rotation = newLocalRot };
                }

                // Save final local pose
                basePose[i] = newLocal;

                // Compute new CS transform for children to use
                if (parent == -1)
                {
                    resultCS[i] = newLocal;
                }
                else
                {
                    resultCS[i] = resultCS[parent].TransformTransform(newLocal);
                }
            }
        }

        #endregion

        public static void SampleSynchronizedPose(ref Motion motion, AnimationIndex animationIndex,
            float normalizedPhase, Span<RigidTransform> result)
        {
            var anim = motion.Animations[animationIndex.m_Value];
            var frame = math.clamp(normalizedPhase * anim.Length, 0f, math.max(anim.Length - 1f, 0f));
            PoseOps.SamplePoseLinear(ref motion, new Motion.PoseTime
            {
                AnimationIndex = animationIndex.m_Value,
                PoseIndex = (int)math.floor(frame) + anim.Begin,
                Theta = math.frac(frame)
            }, result);
        }

        public static unsafe BlendResult1D SampleSynchronizedBlendSpace1D(ref Motion motion,
            ReadOnlySpan<AnimationIndex> animations, ReadOnlySpan<float> positions, float position,
            ref float normalizedPhase, float deltaTime,
            Span<RigidTransform> result)
        {
            // Find indices using a simple linear scan (efficient for small blend spaces)
            var i1 = 0;
            while (i1 < animations.Length && position > positions[i1]) i1++;
            var i0 = math.max(0, i1 - 1);
            i1 = math.min(animations.Length - 1, i1);

            var p0 = positions[i0];
            var p1 = positions[i1];
            var alpha = i0 == i1 ? 0 : math.saturate((position - p0) / (p1 - p0));

            // Calculate durations directly to advance phase
            var anim0 = motion.Animations[animations[i0].m_Value];
            var anim1 = motion.Animations[animations[i1].m_Value];
            var d0 = anim0.Length / anim0.FrameRate;
            var d1 = anim1.Length / anim1.FrameRate;
            var blendedDuration = math.lerp(d0, d1, alpha);

            if (blendedDuration > 0.001f)
                normalizedPhase = math.frac(normalizedPhase + deltaTime / blendedDuration);

            // Sample first animation
            SampleSynchronizedPose(ref motion, animations[i0], normalizedPhase, result);

            // If we have a second animation and weight is significant, sample and blend
            if (alpha > 0.001f)
            {
                Span<RigidTransform> pose1 = stackalloc RigidTransform[motion.BoneCount];
                SampleSynchronizedPose(ref motion, animations[i1], normalizedPhase, pose1);
                MotionBlendOps.Blend(ref motion, result, pose1, alpha);
                return new BlendResult1D { Animation0 = animations[i0], Animation1 = animations[i1], Weight = alpha };
            }

            return new BlendResult1D { Animation0 = animations[i0], Animation1 = AnimationIndex.Default, Weight = 0 };
        }

        public static unsafe BlendResult1D SampleBlendSpace1D(ref Motion motion,
            ReadOnlySpan<AnimationIndex> animations, ReadOnlySpan<float> positions, float position, float time,
            Span<RigidTransform> result)
        {
            if (positions[0] >= position)
            {
                PoseOps.SamplePoseAtTime(ref motion, animations[0], time, SamplingMode.Interpolated, result);
                return new BlendResult1D
                    { Animation0 = animations[0], Animation1 = AnimationIndex.Default, Weight = 0 };
            }

            for (var i = 1; i < animations.Length; i++)
            {
                var lower = positions[i - 1];
                var upper = positions[i];

                if (lower <= position && position < upper)
                {
                    PoseOps.SamplePoseAtTime(ref motion, animations[i - 1], time, SamplingMode.Interpolated, result);

                    Span<RigidTransform> targetPose = stackalloc RigidTransform[motion.BoneCount];
                    PoseOps.SamplePoseAtTime(ref motion, animations[i], time, SamplingMode.Interpolated, targetPose);

                    var alpha = (position - lower) / (upper - lower);
                    MotionBlendOps.Blend(ref motion, result, targetPose, alpha);

                    return new BlendResult1D
                        { Animation0 = animations[i - 1], Animation1 = animations[i], Weight = alpha };
                }
            }

            var last = animations.Length - 1;
            PoseOps.SamplePoseAtTime(ref motion, animations[last], time, SamplingMode.Interpolated, result);
            return new BlendResult1D { Animation0 = animations[last], Animation1 = AnimationIndex.Default, Weight = 0 };
        }

        public static unsafe void Blend2(
            ref Motion motion,
            Span<RigidTransform> source,
            ReadOnlySpan<RigidTransform> target1,
            ReadOnlySpan<RigidTransform> target2,
            float w1, float w2)
        {
            var totalWeight = w1 + w2;
            if (totalWeight < 0.0001f) return;

            // For 2-way blend: t = w2 / (w1 + w2)
            var t = w2 / totalWeight;

            int skeletonBoneCount = motion.SkeletonBoneCount;
            int totalBoneCount = motion.BoneCount;

            for (var i = 0; i < skeletonBoneCount; i++)
            {
                var p0 = source[i];
                var p1 = target1[i];
                var p2 = target2[i];

                source[i] = new RigidTransform
                {
                    Position = math.lerp(math.lerp(p0.Position, p1.Position, w1 > 0 ? 1f : 0f), p2.Position, t),
                    Rotation = math.slerp(
                        w1 > 0 ? math.slerp(p0.Rotation, p1.Rotation, 1f) : p0.Rotation,
                        p2.Rotation, t)
                };
            }
            if (totalBoneCount > skeletonBoneCount)
            {
                var s = AsFloats(source.Slice(skeletonBoneCount, totalBoneCount - skeletonBoneCount));
                var t1s = AsFloats(target1.Slice(skeletonBoneCount, totalBoneCount - skeletonBoneCount));
                var t2s = AsFloats(target2.Slice(skeletonBoneCount, totalBoneCount - skeletonBoneCount));
                for (int i = 0; i < s.Length; i++)
                    s[i] = math.lerp(math.lerp(s[i], t1s[i], w1 > 0 ? 1f : 0f), t2s[i], t);
            }
        }

        public static unsafe void Blend4(
            ref Motion motion,
            Span<RigidTransform> source,
            ReadOnlySpan<RigidTransform> target1,
            ReadOnlySpan<RigidTransform> target2,
            ReadOnlySpan<RigidTransform> target3,
            float w0, float w1, float w2, float w3)
        {
            float totalWeight = w0 + w1 + w2 + w3;
            if (totalWeight < 0.0001f) return;

            // Normalize weights
            float n0 = w0 / totalWeight;
            float n1 = w1 / totalWeight;
            float n2 = w2 / totalWeight;
            float n3 = w3 / totalWeight;

            // Accumulate blended pose
            float t0 = (w0 + w1) > 0 ? w1 / (w0 + w1) : 0;
            float t1 = (w0 + w1 + w2) > 0 ? w2 / (w0 + w1 + w2) : 0;
            float t2 = (w0 + w1 + w2 + w3) > 0 ? w3 / (w0 + w1 + w2 + w3) : 0;

            int skeletonBoneCount = motion.SkeletonBoneCount;
            int totalBoneCount = motion.BoneCount;

            for (int i = 0; i < skeletonBoneCount; i++)
            {
                var p0 = source[i];
                var p1 = target1[i];
                var p2 = target2[i];
                var p3 = target3[i];

                source[i] = new RigidTransform
                {
                    Position = p0.Position * n0 + p1.Position * n1 + p2.Position * n2 + p3.Position * n3,
                    Rotation = math.slerp(
                        math.slerp(
                            math.slerp(p0.Rotation, p1.Rotation, t0),
                            p2.Rotation, t1),
                        p3.Rotation, t2)
                };
            }
            if (totalBoneCount > skeletonBoneCount)
            {
                var s = AsFloats(source.Slice(skeletonBoneCount, totalBoneCount - skeletonBoneCount));
                var t1s = AsFloats(target1.Slice(skeletonBoneCount, totalBoneCount - skeletonBoneCount));
                var t2s = AsFloats(target2.Slice(skeletonBoneCount, totalBoneCount - skeletonBoneCount));
                var t3s = AsFloats(target3.Slice(skeletonBoneCount, totalBoneCount - skeletonBoneCount));
                for (int i = 0; i < s.Length; i++)
                    s[i] = s[i] * n0 + t1s[i] * n1 + t2s[i] * n2 + t3s[i] * n3;
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

        public static float GetDuration(ref Motion motion, AnimationIndex idx)
        {
            if (idx == AnimationIndex.Default) return 0f;
            var anim = motion.Animations[idx.m_Value];
            return anim.Length / anim.FrameRate;
        }

        /// <summary>
        /// Samples 4 cardinal additive clips at a one-shot phase (startPosition + stateTime * playRate),
        /// blends them by F/B/L/R weights, and applies the result as a local additive.
        /// </summary>
        public static void ApplyAdditiveBlend4(
            ref Motion motion,
            AnimationIndex clipF, AnimationIndex clipB, AnimationIndex clipL, AnimationIndex clipR,
            float wF, float wB, float wL, float wR,
            float detailAlpha,
            float stateTime,
            float playRate,
            float startPosition,
            Span<RigidTransform> pose)
        {
            if (detailAlpha < 0.001f)
                return;

            int boneCount = motion.BoneCount;
            Span<RigidTransform> pF = stackalloc RigidTransform[boneCount];
            Span<RigidTransform> pB = stackalloc RigidTransform[boneCount];
            Span<RigidTransform> pL = stackalloc RigidTransform[boneCount];
            Span<RigidTransform> pR = stackalloc RigidTransform[boneCount];

            float dF = GetDuration(ref motion, clipF);
            float dB = GetDuration(ref motion, clipB);
            float dL = GetDuration(ref motion, clipL);
            float dR = GetDuration(ref motion, clipR);

            SampleSynchronizedPose(ref motion, clipF, dF > 0.001f ? math.saturate(startPosition + (stateTime * playRate) / dF) : 1f, pF);
            SampleSynchronizedPose(ref motion, clipB, dB > 0.001f ? math.saturate(startPosition + (stateTime * playRate) / dB) : 1f, pB);
            SampleSynchronizedPose(ref motion, clipL, dL > 0.001f ? math.saturate(startPosition + (stateTime * playRate) / dL) : 1f, pL);
            SampleSynchronizedPose(ref motion, clipR, dR > 0.001f ? math.saturate(startPosition + (stateTime * playRate) / dR) : 1f, pR);

            Blend4(ref motion, pF, pB, pL, pR, wF, wB, wL, wR);
            AdditiveOps.ApplyLocalAdditive(ref motion, pose, pF, detailAlpha);
        }
    }

    public struct BlendSpace2DBuilder
    {
        private Unity.Collections.NativeArray<AnimationIndex> m_Animations;
        private Unity.Collections.NativeArray<float2> m_Positions;
        private int m_Count;
        private readonly Unity.Collections.Allocator m_Allocator;

        public BlendSpace2DBuilder(int capacity, Unity.Collections.Allocator allocator)
        {
            m_Animations = new Unity.Collections.NativeArray<AnimationIndex>(capacity, Unity.Collections.Allocator.Temp);
            m_Positions = new Unity.Collections.NativeArray<float2>(capacity, Unity.Collections.Allocator.Temp);
            m_Count = 0;
            m_Allocator = allocator;
        }

        public void Add(AnimationIndex animation, float2 position)
        {
            if (m_Count >= m_Animations.Length)
                return;

            m_Animations[m_Count] = animation;
            m_Positions[m_Count] = position;
            m_Count++;
        }

        public unsafe BlendSpace2D Create()
        {
            return new BlendSpace2D(
                new ReadOnlySpan<AnimationIndex>((AnimationIndex*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(m_Animations), m_Count),
                new ReadOnlySpan<float2>((float2*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(m_Positions), m_Count),
                m_Allocator);
        }
    }
}
