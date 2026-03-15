#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using AnimationSystem;
using Motion = AnimationSystem.Motion;
using AnyTransform = AnimationSystem.RigidTransform;
using RigidTransform = AnimationSystem.RigidTransform;

namespace AnimationSystem.Editor
{
    public unsafe partial class BendzRigImporter
    {
        private void CollectAnimations(object obj, string prefix)
        {
            if (obj == null) return;
            var type = obj.GetType();
            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var field in fields)
            {
                var value = field.GetValue(obj);
                if (value == null) continue;

                string fieldName = string.IsNullOrEmpty(prefix) ? field.Name : $"{prefix}_{field.Name}";

                if (field.FieldType == typeof(AnimationClip))
                {
                    var clip = (AnimationClip)value;
                    AddAnimationFromSet(clip.name, clip);
                }
                else if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum && field.FieldType.Namespace != "UnityEngine")
                {
                    CollectAnimations(value, fieldName);
                }
            }
        }

        private void AddAnimationFromSet(string name, AnimationClip clip)
        {
            if (clip == null) return;
            if (Animations.Any(a => a.Clip == clip)) return;
            Animations.Add(new Animation { Name = name, Clip = clip });
        }

        private List<AdditiveInfo> CollectAdditiveMetadata(object obj, string prefix)
        {
            if (obj == null) return new List<AdditiveInfo>();

            var type = obj.GetType();
            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            var clipNameToFieldName = fields
                .Where(f => f.FieldType == typeof(AnimationClip))
                .Select(f => (f.Name, Clip: f.GetValue(obj) as AnimationClip))
                .Where(x => x.Clip != null)
                .ToDictionary(x => x.Name, x => x.Clip.name);

            var result = fields
                .Where(f => f.FieldType == typeof(AnimationClip))
                .Select(f => (Field: f, Attr: f.GetCustomAttributes(typeof(AdditiveAttribute), false).FirstOrDefault() as AdditiveAttribute))
                .Where(x => x.Attr != null)
                .SelectMany(x => 
                {
                    var clip = x.Field.GetValue(obj) as AnimationClip;
                    if (clip != null && clipNameToFieldName.TryGetValue(x.Attr.Base, out var baseClipName))
                    {
                        return new[] { new AdditiveInfo
                        {
                            AdditiveClipName = clip.name,
                            BaseClipName = baseClipName,
                            Type = x.Attr.Type,
                            BaseFrame = x.Attr.BaseFrame
                        }};
                    }
                    if (clip != null)
                        Debug.LogWarning($"Additive attribute on {x.Field.Name}: base field '{x.Attr.Base}' not found");
                    return Array.Empty<AdditiveInfo>();
                })
                .ToList();

            var nestedResults = fields
                .Where(f => f.FieldType.IsValueType && !f.FieldType.IsPrimitive && !f.FieldType.IsEnum && f.FieldType.Namespace != "UnityEngine")
                .Select(f => (Field: f, Value: f.GetValue(obj)))
                .Where(x => x.Value != null)
                .SelectMany(x => CollectAdditiveMetadata(x.Value, x.Field.Name));

            result.AddRange(nestedResults);
            return result;
        }

        private void PreprocessAdditive(
            AnyTransform* transforms,
            ref Motion motion,
            Motion.Animation additiveAnim,
            Motion.Animation baseAnim,
            int[] parentIndices,
            AdditiveType type,
            int baseFrameOverride = -1)
        {
            int boneCount = motion.BoneCount;
            int additiveFrameCount = additiveAnim.End - additiveAnim.Begin;
            int baseFrameCount = baseAnim.End - baseAnim.Begin;

            Span<RigidTransform> basePose = stackalloc RigidTransform[boneCount];
            Span<RigidTransform> additivePose = stackalloc RigidTransform[boneCount];
            Span<RigidTransform> baseCS = stackalloc RigidTransform[boneCount];
            Span<RigidTransform> additiveCS = stackalloc RigidTransform[boneCount];

            for (int f = 0; f < additiveFrameCount; f++)
            {
                int additiveFrame = additiveAnim.Begin + f;

                int baseFrame;
                if (baseFrameOverride >= 0)
                    baseFrame = baseAnim.Begin + math.clamp(baseFrameOverride, 0, baseFrameCount - 1);
                else if (baseFrameCount == 1)
                    baseFrame = baseAnim.Begin;
                else
                {
                    float normalizedTime = (float)f / math.max(1, additiveFrameCount - 1);
                    int baseFrameIndex = (int)math.floor(normalizedTime * (baseFrameCount - 1));
                    baseFrame = baseAnim.Begin + math.clamp(baseFrameIndex, 0, baseFrameCount - 1);
                }

                for (int j = 0; j < boneCount; j++)
                {
                    basePose[j] = transforms[motion.GetBoneTransformIndex(j, baseFrame)];
                    additivePose[j] = transforms[motion.GetBoneTransformIndex(j, additiveFrame)];
                }

                if (type == AdditiveType.Local)
                {
                    for (int j = 0; j < boneCount; j++)
                    {
                        RigidTransform delta = basePose[j].InverseTransformTransform(additivePose[j]);
                        int idx = motion.GetBoneTransformIndex(j, additiveFrame);
                        transforms[idx] = CreateT(delta.Position, delta.Rotation, new float3(1, 1, 1));
                    }
                }
                else // MeshSpace
                {
                    for (int j = 0; j < boneCount; j++)
                    {
                        int parent = parentIndices[j];
                        if (parent == -1)
                        {
                            baseCS[j] = basePose[j];
                            additiveCS[j] = additivePose[j];
                        }
                        else
                        {
                            baseCS[j] = baseCS[parent].TransformTransform(basePose[j]);
                            additiveCS[j] = additiveCS[parent].TransformTransform(additivePose[j]);
                        }
                    }

                    for (int j = 0; j < boneCount; j++)
                    {
                        var deltaRot = math.mul(additiveCS[j].Rotation, math.conjugate(baseCS[j].Rotation));
                        var deltaPos = additivePose[j].Position - basePose[j].Position;
                        int idx = motion.GetBoneTransformIndex(j, additiveFrame);
                        transforms[idx] = CreateT(deltaPos, deltaRot, new float3(1, 1, 1));
                    }
                }
            }
        }
    }
}
#endif
