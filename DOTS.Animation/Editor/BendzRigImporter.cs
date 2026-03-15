#if UNITY_EDITOR
using System;
using UnityEditor.AssetImporters;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using AnimationSystem;
using Motion = AnimationSystem.Motion;
using AnyTransform = AnimationSystem.RigidTransform;

namespace AnimationSystem.Editor
{
    [ScriptedImporter(BendzRig.DefaultVersion, "bendzrig")]
    public unsafe partial class BendzRigImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            if (SkeletalMesh == null)
                return;

            // 1. Collect Metadata
            Animations.Clear();
            if (Settings != null)
            {
                CollectAnimations(Settings, "");
            }

            var uniqueCurves = CollectUniqueCurves();
            var attributeBones = GenerateAttributeBones(uniqueCurves);

            // 2. Prepare Bone Data
            var enabledBones = Bones.Where(j => j.Enabled).Select(j => j.Transform).ToList();
            var addedVirtualBones = PrepareAddedVirtualBones(enabledBones, attributeBones);
            var totalBoneCount = enabledBones.Count + addedVirtualBones.Count;
            
            if (totalBoneCount == 0) 
                return;

            var allBoneNames = enabledBones.Select(j => j.name).ToList();
            allBoneNames.AddRange(addedVirtualBones.Select(v => v.Name));
            int firstAttrBoneIdx = allBoneNames.IndexOf("ATTR_0");

            // Calculate Parent Indices
            var parentIndices = new int[totalBoneCount];
            for (int i = 0; i < enabledBones.Count; i++)
                parentIndices[i] = FindBone(enabledBones, enabledBones[i].parent);

            for (int i = 0; i < addedVirtualBones.Count; i++)
            {
                var vBone = addedVirtualBones[i];
                int parentIdx = -1;
                if (!string.IsNullOrEmpty(vBone.ParentName))
                    parentIdx = allBoneNames.IndexOf(vBone.ParentName);
                parentIndices[enabledBones.Count + i] = parentIdx;
            }

            // 3. Build Skeleton
            using var skeletonBuilder = new BlobBuilder(Allocator.Temp);
            BuildSkeleton(skeletonBuilder, enabledBones, addedVirtualBones, uniqueCurves, totalBoneCount, allBoneNames, firstAttrBoneIdx, parentIndices);

            BendzRig asset = ScriptableObject.CreateInstance<BendzRig>();
            asset.name = SkeletalMesh.name;
            asset.WriteSkeleton(skeletonBuilder);

            // 4. Build Motion
            using var motionBuilder = new BlobBuilder(Allocator.Temp);
            BuildMotion(ctx, motionBuilder, asset, enabledBones, addedVirtualBones, uniqueCurves, totalBoneCount, firstAttrBoneIdx, parentIndices);
            asset.WriteMotion(motionBuilder);

            // 5. Finalize
            ctx.AddObjectToAsset("Rig", asset);
            ctx.SetMainObject(asset);
            asset.Clear();
        }

        private List<string> CollectUniqueCurves()
        {
            var virtualBoneNames = VirtualBones.Select(v => v.Name).ToHashSet();
            var muscleNames = HumanTrait.MuscleName;
            var boneNames = Enum.GetNames(typeof(HumanBodyBones));

            var animationCurves = Animations
                .Where(anim => anim.Clip != null)
                .SelectMany(anim => AnimationUtility.GetCurveBindings(anim.Clip))
                .Where(binding => 
                {
                    var n = binding.propertyName;
                    if (binding.type == typeof(Animator) || binding.type == typeof(Transform))
                    {
                        if (virtualBoneNames.Contains(binding.path)) return false;
                        if (n.StartsWith("m_LocalPosition") || n.StartsWith("m_LocalRotation") || n.StartsWith("m_LocalScale")) return false;
                    }

                    bool isInternal = n.StartsWith("Root") || n.StartsWith("Motion") || n.Contains("IK") || n.StartsWith("m_");
                    if (!isInternal)
                    {
                        if (boneNames.Any(bn => n.StartsWith(bn)) || muscleNames.Any(mn => n.Equals(mn)))
                            isInternal = true;
                    }

                    return !isInternal;
                })
                .Select(binding => binding.propertyName);

            return animationCurves
                .Concat(ExtraCurveNames)
                .Distinct()
                .ToList();
        }

        private List<VirtualBone> GenerateAttributeBones(List<string> uniqueCurves)
        {
            CurveBindings.Clear();
            var attributeBones = new List<VirtualBone>();
            int attrBoneCount = (uniqueCurves.Count + 6) / 7;
            
            for (int b = 0; b < attrBoneCount; b++)
                attributeBones.Add(new VirtualBone { Name = $"ATTR_{b}", ParentName = "Root", PinTarget = "" });

            for (int i = 0; i < uniqueCurves.Count; i++)
            {
                CurveBindings.Add(new CurveBinding
                {
                    CurveName = uniqueCurves[i],
                    BoneName = $"ATTR_{i / 7}",
                    Component = i % 7
                });
            }
            return attributeBones;
        }

        private List<VirtualBone> PrepareAddedVirtualBones(List<Transform> enabledBones, List<VirtualBone> attributeBones)
        {
            var allBoneNames = enabledBones.Select(j => j.name).ToHashSet();
            return VirtualBones
                .Where(v => !allBoneNames.Contains(v.Name))
                .Concat(attributeBones)
                .ToList();
        }

        private void BuildSkeleton(BlobBuilder builder, List<Transform> enabledBones, List<VirtualBone> addedVirtualBones, List<string> uniqueCurves, int totalBoneCount, List<string> allBoneNames, int firstAttrBoneIdx, int[] parentIndices)
        {
            ref var skeleton = ref builder.ConstructRoot<Skeleton>();
            var enabledRenderers = Skins.Where(s => s.Enabled).Select(s => s.SkinnedMeshRenderer).ToList();
            var skins = builder.Allocate(ref skeleton.Skins, enabledRenderers.Count);
            int bindCount = 0;
            
            for (int i = 0; i < enabledRenderers.Count; i++)
            {
                var r = enabledRenderers[i];
                int count = r.sharedMesh.bindposeCount;
                skins[i] = new AnimationSystem.Skin
                {
                    Root = 0,
                    Begin = bindCount,
                    End = bindCount + count,
                    Bounds = new AABB { Center = r.localBounds.center, Extents = r.localBounds.extents }
                };
                bindCount += count;
            }

            var boneBindPose = builder.Allocate(ref skeleton.SkinBindPoses, bindCount);
            var skinMatrixIndices = builder.Allocate(ref skeleton.SkinBoneIndices, bindCount);
            int bIdx = 0;
            for (int i = 0; i < enabledRenderers.Count; i++)
            {
                var r = enabledRenderers[i];
                var bones = r.bones;
                var poses = r.sharedMesh.bindposes;
                for (int j = 0; j < poses.Length; j++)
                {
                    int boneIdx = FindBone(enabledBones, bones[j]);
                    if (boneIdx != -1) { boneBindPose[bIdx] = poses[j]; skinMatrixIndices[bIdx] = boneIdx; }
                    bIdx++;
                }
            }

            var boneParentIndices = builder.Allocate(ref skeleton.BoneParentIndices, totalBoneCount);
            for (int i = 0; i < totalBoneCount; i++)
                boneParentIndices[i] = parentIndices[i];

            if (BoneNames)
            {
                var names = builder.Allocate(ref skeleton.BoneNames, totalBoneCount);
                for (int i = 0; i < enabledBones.Count; i++) names[i] = enabledBones[i].name;
                for (int i = 0; i < addedVirtualBones.Count; i++) names[enabledBones.Count + i] = addedVirtualBones[i].Name;
            }
            else builder.Allocate(ref skeleton.BoneNames, 0);

            var curveNames = builder.Allocate(ref skeleton.CurveNames, uniqueCurves.Count);
            var curveFloatIndices = builder.Allocate(ref skeleton.CurveFloatIndices, uniqueCurves.Count);
            skeleton.FirstCurveBone = firstAttrBoneIdx >= 0 ? firstAttrBoneIdx : totalBoneCount;
            skeleton.CurveCount = uniqueCurves.Count;

            for (int i = 0; i < uniqueCurves.Count; i++)
            {
                curveNames[i] = uniqueCurves[i];
                int boneIdx = firstAttrBoneIdx >= 0 ? firstAttrBoneIdx + (i / 7) : -1;
                curveFloatIndices[i] = boneIdx >= 0 ? boneIdx * 7 + (i % 7) : -1;
            }
        }

        private void BuildMotion(AssetImportContext ctx, BlobBuilder builder, BendzRig asset, List<Transform> enabledBones, List<VirtualBone> addedVirtualBones, List<string> uniqueCurves, int totalBoneCount, int firstAttrBoneIdx, int[] parentIndices)
        {
            ref var motion = ref builder.ConstructRoot<Motion>();
            var allAnims = new List<Animation> { new Animation { Name = "" } };
            allAnims.AddRange(Animations);

            motion.BoneCount = totalBoneCount;
            motion.SkeletonBoneCount = firstAttrBoneIdx >= 0 ? firstAttrBoneIdx : totalBoneCount;
            motion.CurveCount = uniqueCurves.Count;
            motion.PoseCount = 0;
            motion.PoseStride = totalBoneCount * sizeof(AnyTransform);
            
            var mAnims = builder.Allocate(ref motion.Animations, allAnims.Count);
            for (int i = 0; i < allAnims.Count; i++)
            {
                var clip = allAnims[i].Clip;
                if (clip != null)
                {
                    int f = (int)(clip.length * clip.frameRate);
                    mAnims[i] = new Motion.Animation { Begin = motion.PoseCount, End = motion.PoseCount + f, FrameRate = clip.frameRate, IsLooping = clip.isLooping, Speed = allAnims[i].Speed };
                    motion.PoseCount += f;
                }
                else
                {
                    mAnims[i] = new Motion.Animation { Begin = motion.PoseCount, End = motion.PoseCount + 1, FrameRate = 1, Speed = 1 };
                    motion.PoseCount += 1;
                }
            }

            if (AnimationNames)
            {
                var names = builder.Allocate(ref motion.AnimationNames, allAnims.Count);
                for (int i = 0; i < allAnims.Count; i++) names[i] = allAnims[i].Name;
            }
            else builder.Allocate(ref motion.AnimationNames, 0);

            var transforms = builder.Allocate(ref motion.Transforms, motion.PoseCount * totalBoneCount);
            AnyTransform identity = AnyTransform.FromPositionRotation(float3.zero, quaternion.identity);
            for (int i = 0; i < transforms.Length; i++) transforms[i] = identity;

            for (int i = 0; i < allAnims.Count; i++)
            {
                var anim = allAnims[i];
                if (anim.Clip == null) continue;
                Sample(ctx, anim, mAnims[i], enabledBones, addedVirtualBones, (AnyTransform*)transforms.GetUnsafePtr(), ref motion);
            }

            if (Settings != null)
            {
                var additiveInfos = CollectAdditiveMetadata(Settings, "");
                foreach (var additiveInfo in additiveInfos)
                {
                    int additiveAnimIdx = allAnims.FindIndex(a => a.Clip != null && a.Clip.name == additiveInfo.AdditiveClipName);
                    int baseAnimIdx = allAnims.FindIndex(a => a.Clip != null && a.Clip.name == additiveInfo.BaseClipName);

                    if (additiveAnimIdx != -1 && baseAnimIdx != -1)
                    {
                        PreprocessAdditive((AnyTransform*)transforms.GetUnsafePtr(), ref motion, mAnims[additiveAnimIdx], mAnims[baseAnimIdx], parentIndices, additiveInfo.Type, additiveInfo.BaseFrame);
                    }
                }
            }
        }
    }
}
#endif
