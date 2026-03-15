#if UNITY_EDITOR
using System;
using UnityEditor.AssetImporters;
using UnityEngine;
using System.Collections.Generic;
using AnimationSystem;

namespace AnimationSystem.Editor
{
    public unsafe partial class BendzRigImporter : ScriptedImporter
    {
        // ─── Nested types ───────────────────────────────────────────────────

        [System.Serializable]
        public class Animation
        {
            public string Name = "Unknown";
            public AnimationClip Clip;
            public float Speed = 1.0f;
            public bool ApplyFootIK = true;
        }

        [System.Serializable]
        public class Bone
        {
            public bool Enabled;
            public Transform Transform;
        }

        [System.Serializable]
        public class SkinEntry
        {
            public bool Enabled;
            public SkinnedMeshRenderer SkinnedMeshRenderer;
        }

        [System.Serializable]
        public struct CurveBinding
        {
            public string CurveName;
            public string BoneName;
            public int Component; // 0=x, 1=y, 2=z
        }

        [System.Serializable]
        public struct VirtualBone
        {
            public string Name;
            public string ParentName;
            public string PinTarget;
        }

        internal struct AdditiveInfo
        {
            public string AdditiveClipName;
            public string BaseClipName;
            public AdditiveType Type;
            public int BaseFrame;
        }

        // ─── Fields ────────────────────────────────────────────────────────

        public GameObject SkeletalMesh;
        public List<Animation> Animations = new();
        public bool AnimationNames = true;
        public List<SkinEntry> Skins = new();
        public List<Bone> Bones = new();
        public bool BoneNames = true;

        [Tooltip("Settings object (ScriptableObject) containing AnimationClip fields. Can use [Additive] attribute.")]
        public ScriptableObject Settings;
        public List<CurveBinding> CurveBindings = new();
        public List<VirtualBone> VirtualBones = new();

        public List<string> ExtraCurveNames = new();
    }
}
#endif
