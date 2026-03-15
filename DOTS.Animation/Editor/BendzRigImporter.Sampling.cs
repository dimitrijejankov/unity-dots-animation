#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using AnimationSystem;
using Motion = AnimationSystem.Motion;
using AnyTransform = AnimationSystem.RigidTransform;

namespace AnimationSystem.Editor
{
    public unsafe partial class BendzRigImporter
    {
        private void Sample(UnityEditor.AssetImporters.AssetImportContext ctx, Animation anim, Motion.Animation info, List<Transform> bones, List<VirtualBone> virtualBones, AnyTransform* transforms, ref Motion motion)
        {
            if (anim.Clip.isHumanMotion)
                SampleHumanoid(anim, info, bones, virtualBones, transforms, ref motion);
            else
                SampleGeneric(anim, info, bones, virtualBones, transforms, ref motion);
        }

        private void SampleHumanoid(Animation anim, Motion.Animation info, List<Transform> bones, List<VirtualBone> virtualBones, AnyTransform* transforms, ref Motion motion)
        {
            var clip = anim.Clip;
            float dt = 1.0f / info.FrameRate;
            var copy = Instantiate(SkeletalMesh);
            var animator = copy.GetComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var graph = PlayableGraph.Create("Sample");
            var output = AnimationPlayableOutput.Create(graph, "Out", animator);
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(anim.ApplyFootIK);
            output.SetSourcePlayable(playable);

            var bindings = AnimationUtility.GetCurveBindings(clip);

            try
            {
                for (int f = info.Begin; f < info.End; f++)
                {
                    float time = (f - info.Begin) * dt;
                    playable.SetTime(time);
                    graph.Evaluate();

                    SampleHumanoidBones(copy, bones, f, transforms, ref motion);
                    SampleHumanoidVirtualBones(copy, clip, virtualBones, bones.Count, f, dt, info.Begin, transforms, ref motion, bindings);
                }
            }
            finally
            {
                DestroyImmediate(copy);
                graph.Destroy();
            }
        }

        private void SampleHumanoidBones(GameObject copy, List<Transform> bones, int f, AnyTransform* transforms, ref Motion motion)
        {
            for (int j = 0; j < bones.Count; j++)
            {
                var t = copy.transform.Find(GetBonePath(SkeletalMesh.transform, bones[j]));
                if (t != null)
                {
                    transforms[motion.GetBoneTransformIndex(j, f)] = CreateT(t.localPosition, t.localRotation, t.localScale);
                }
            }
        }

        private void SampleHumanoidVirtualBones(GameObject copy, AnimationClip clip, List<VirtualBone> virtualBones, int boneCount, int f, float dt, int beginFrame, AnyTransform* transforms, ref Motion motion, EditorCurveBinding[] bindings)
        {
            var virtualBoneWorldTransforms = new Dictionary<string, (float3 pos, quaternion rot)>();
            float time = (f - beginFrame) * dt;

            for (int v = 0; v < virtualBones.Count; v++)
            {
                var vBone = virtualBones[v];
                int boneIndex = boneCount + v;
                int transformIndex = motion.GetBoneTransformIndex(boneIndex, f);

                float3 worldPos = float3.zero;
                quaternion worldRot = quaternion.identity;
                bool hasWorldTransform = false;

                if (!string.IsNullOrEmpty(vBone.PinTarget))
                {
                    var sourceTransform = FindChildRecursive(copy.transform, vBone.PinTarget);
                    if (sourceTransform != null)
                    {
                        worldPos = sourceTransform.position;
                        worldRot = sourceTransform.rotation;
                        hasWorldTransform = true;
                    }
                }

                if (!hasWorldTransform)
                {
                    if (SampleVirtualBoneFromCurves(clip, vBone, time, out var curveT, bindings))
                    {
                        transforms[transformIndex] = curveT;
                        continue;
                    }

                    worldPos = copy.transform.position;
                    worldRot = copy.transform.rotation;
                }

                (float3 parentWorldPos, quaternion parentWorldRot) = GetVirtualBoneParentWorldTransform(copy, vBone, virtualBoneWorldTransforms);

                float3 localPos = math.mul(math.conjugate(parentWorldRot), worldPos - parentWorldPos);
                quaternion localRot = math.mul(math.conjugate(parentWorldRot), worldRot);

                if (vBone.Name.StartsWith("ATTR_"))
                {
                    transforms[transformIndex] = SampleAttributeBone(clip, vBone, time, bindings);
                    virtualBoneWorldTransforms[vBone.Name] = (worldPos, worldRot);
                    continue;
                }

                transforms[transformIndex] = CreateT(localPos, localRot, new float3(1, 1, 1));
                virtualBoneWorldTransforms[vBone.Name] = (worldPos, worldRot);
            }
        }

        private void SampleGeneric(Animation anim, Motion.Animation info, List<Transform> bones, List<VirtualBone> virtualBones, AnyTransform* transforms, ref Motion motion)
        {
            var clip = anim.Clip;
            float dt = 1.0f / info.FrameRate;
            var bindings = AnimationUtility.GetCurveBindings(clip);

            for (int j = 0; j < bones.Count; j++)
            {
                for (int f = info.Begin; f < info.End; f++)
                    transforms[motion.GetBoneTransformIndex(j, f)] = CreateT(bones[j].localPosition, bones[j].localRotation, bones[j].localScale);
            }

            for (int v = 0; v < virtualBones.Count; v++)
            {
                int boneIndex = bones.Count + v;
                for (int f = info.Begin; f < info.End; f++)
                {
                    var vBone = virtualBones[v];
                    float time = (f - info.Begin) * dt;

                    if (vBone.Name.StartsWith("ATTR_"))
                    {
                        transforms[motion.GetBoneTransformIndex(boneIndex, f)] = SampleAttributeBone(clip, vBone, time, bindings);
                        continue;
                    }
                    transforms[motion.GetBoneTransformIndex(boneIndex, f)] = CreateT(float3.zero, quaternion.identity, new float3(1, 1, 1));
                }
            }
        }

        private bool SampleVirtualBoneFromCurves(AnimationClip clip, VirtualBone vBone, float time, out AnyTransform result, EditorCurveBinding[] bindings)
        {
            float3 p = float3.zero;
            quaternion r = quaternion.identity;
            bool foundCurve = false;

            foreach (var binding in bindings)
            {
                if (binding.path == vBone.Name)
                {
                    foundCurve = true;
                    float value = AnimationUtility.GetEditorCurve(clip, binding).Evaluate(time);
                    if (binding.propertyName.StartsWith("m_LocalPosition.x")) p.x = value;
                    else if (binding.propertyName.StartsWith("m_LocalPosition.y")) p.y = value;
                    else if (binding.propertyName.StartsWith("m_LocalPosition.z")) p.z = value;
                    else if (binding.propertyName.StartsWith("m_LocalRotation.x")) r.value.x = value;
                    else if (binding.propertyName.StartsWith("m_LocalRotation.y")) r.value.y = value;
                    else if (binding.propertyName.StartsWith("m_LocalRotation.z")) r.value.z = value;
                    else if (binding.propertyName.StartsWith("m_LocalRotation.w")) r.value.w = value;
                }
            }

            if (foundCurve)
            {
                result = CreateT(p, math.normalize(r), new float3(1, 1, 1));
                return true;
            }

            result = default;
            return false;
        }

        private (float3 pos, quaternion rot) GetVirtualBoneParentWorldTransform(GameObject copy, VirtualBone vBone, Dictionary<string, (float3 pos, quaternion rot)> virtualBoneWorldTransforms)
        {
            if (!string.IsNullOrEmpty(vBone.ParentName))
            {
                if (virtualBoneWorldTransforms.TryGetValue(vBone.ParentName, out var parentVirtualTransform))
                {
                    return parentVirtualTransform;
                }

                var p = FindChildRecursive(copy.transform, vBone.ParentName);
                if (p != null)
                {
                    return (p.position, p.rotation);
                }
            }

            return (copy.transform.position, copy.transform.rotation);
        }

        private AnyTransform SampleAttributeBone(AnimationClip clip, VirtualBone vBone, float time, EditorCurveBinding[] bindings)
        {
            float3 localPos = float3.zero;
            quaternion localRotCurve = default;

            foreach (var binding in CurveBindings)
            {
                if (binding.BoneName == vBone.Name)
                {
                    var clipBinding = bindings.FirstOrDefault(b => b.propertyName == binding.CurveName);
                    if (clipBinding.propertyName != null)
                    {
                        float val = AnimationUtility.GetEditorCurve(clip, clipBinding).Evaluate(time);
                        if (binding.Component == 0) localPos.x = val;
                        else if (binding.Component == 1) localPos.y = val;
                        else if (binding.Component == 2) localPos.z = val;
                        else if (binding.Component == 3) localRotCurve.value.x = val;
                        else if (binding.Component == 4) localRotCurve.value.y = val;
                        else if (binding.Component == 5) localRotCurve.value.z = val;
                        else if (binding.Component == 6) localRotCurve.value.w = val;
                    }
                }
            }

            return CreateT(localPos, localRotCurve, new float3(1, 1, 1));
        }

        private static AnyTransform CreateT(float3 p, quaternion r, float3 s)
        {
            return AnyTransform.FromPositionRotation(p, r);
        }
    }
}
#endif
