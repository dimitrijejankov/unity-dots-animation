using System;

namespace AnimationSystem
{
    /// <summary>
    /// Bone indices for spine chain used by OrientationWarpOps.
    /// </summary>
    public unsafe struct SpineBoneIndices
    {
        public fixed int Indices[8];
        public int Count;
    }

    /// <summary>
    /// Settings for orientation warping (shortest path rotation adjustment).
    /// </summary>
    [Serializable]
    public struct OrientationWarpSettings
    {
        public int rootBoneIndex;
        public int hipBoneIndex;
        public SpineBoneIndices SpineBones;
        public float maxWarpAngle;
    }
}
