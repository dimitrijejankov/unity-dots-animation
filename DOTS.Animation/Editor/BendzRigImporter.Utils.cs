#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using UnityEngine;
using AnimationSystem;

namespace AnimationSystem.Editor
{
    public unsafe partial class BendzRigImporter
    {
        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var found = FindChildRecursive(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static string GetBonePath(Transform root, Transform leaf)
        {
            var b = new System.Text.StringBuilder();
            b.Insert(0, leaf.name);
            while (leaf.parent != null && leaf.parent != root) { leaf = leaf.parent; b.Insert(0, '/'); b.Insert(0, leaf.name); }
            return b.ToString();
        }

        private static int FindBone(List<Transform> list, Transform t)
        {
            return list?.IndexOf(t) ?? -1;
        }

        private static void AllocateCurve(ref BlobArray<CurveKeyFrame> blobArray, AnimationCurve curve, BlobBuilder builder)
        {
            var keyframes = curve.keys;
            var array = builder.Allocate(ref blobArray, keyframes.Length);
            for (int i = 0; i < keyframes.Length; i++)
            {
                array[i] = new CurveKeyFrame
                {
                    Time = keyframes[i].time,
                    Value = keyframes[i].value,
                    InTangent = keyframes[i].inTangent,
                    OutTangent = keyframes[i].outTangent
                };
            }
        }

        public void CollectRenderers()
        {
            if (SkeletalMesh == null)
            {
                Skins.Clear();
                return;
            }

            Skins = SkeletalMesh.GetComponentsInChildren<SkinnedMeshRenderer>()
                .Select(r => new SkinEntry { Enabled = true, SkinnedMeshRenderer = r })
                .ToList();
        }

        public void CollectBones()
        {
            Bones.Clear();
            if (SkeletalMesh == null) return;
            for (int i = 0; i < SkeletalMesh.transform.childCount; i++)
                BuildSkeletonRecursive(SkeletalMesh.transform.GetChild(i));
        }

        private bool BuildSkeletonRecursive(Transform t)
        {
            int index = Bones.Count;
            bool enabled = ContainsBindPose(t);
            Bones.Add(new Bone { Transform = t, Enabled = false });
            for (int i = 0; i < t.childCount; i++)
                enabled |= BuildSkeletonRecursive(t.GetChild(i));
            var j = Bones[index];
            j.Enabled = enabled;
            Bones[index] = j;
            return enabled;
        }

        private bool ContainsBindPose(Transform transform)
        {
            return Skins.Any(s => s.SkinnedMeshRenderer != null && s.SkinnedMeshRenderer.bones.Any(b => b == transform));
        }
    }
}
#endif
