using System;

namespace AnimationSystem.Settings
{
    [Serializable]
    public struct StrideWarpSettings
    {
        public float PivotOffset;
        public float HipAdjustmentRatio;
        public bool ProjectToGround;
    }
}
