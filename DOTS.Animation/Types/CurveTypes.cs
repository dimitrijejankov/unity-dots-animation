using System;

namespace AnimationSystem
{
    /// <summary>
    /// Absolute flat-float index into the pose viewed as float[].
    /// For curve i stored in bone at boneIdx with floatOffset: Index = boneIdx * 7 + floatOffset.
    /// Used for "ATTR_X" virtual bones (7 floats each: float3 pos + quaternion rot).
    /// </summary>
    [Serializable]
    public struct CurveIndices
    {
        /// <summary>
        /// Absolute flat-float index into the pose viewed as float[].
        /// For curve i stored in bone at boneIdx: Index = boneIdx * 7 + floatOffset.
        /// </summary>
        public int Index;

        public bool IsValid => Index >= 0;
        public static CurveIndices Invalid => new() { Index = -1 };
    }

    /// <summary>
    /// Represents a single keyframe in an animation curve.
    /// Used for stride blend curves and other baked runtime curves.
    /// </summary>
    [Serializable]
    public struct CurveKeyFrame
    {
        public float Time;
        public float Value;
        public float InTangent;
        public float OutTangent;
    }
}
