using System;
using System.Runtime.InteropServices;

namespace AnimationSystem.Settings
{
    [Serializable]
    public struct FootIKSettings
    {
        public float FootHeight;                    // Height of foot above ground (default 0.135)
        public float IK_TraceDistanceAboveFoot;     // Trace start above foot (default 0.5)
        public float IK_TraceDistanceBelowFoot;     // Trace end below foot (default 0.45)
        public float FootOffsetInterpSpeedUp;       // Interp speed when raising foot (default 15.0)
        public float FootOffsetInterpSpeedDown;     // Interp speed when lowering foot (default 30.0)
        public float FootOffsetRotationInterpSpeed; // Rotation interp speed (default 30.0)
        public float PelvisInterpSpeedUp;           // Pelvis interp speed going up (default 10.0)
        public float PelvisInterpSpeedDown;         // Pelvis interp speed going down (default 15.0)
        public float ResetInterpSpeed;              // Speed for in-air reset (default 15.0)
        public float FootOffsetLateralReduction;    // Max reduction when moving sideways (default 0.8)
        public float MaxGroundAngleForDetection;    // Max angle to still detect as ground (default 81 degrees)
        public float MaxGroundAngleForAdaptation;   // Max angle feet will adapt to (default 79 degrees)

        // Dual-cast settings for ground detection
        public float FootBoxWidth;      // Width of foot box (default 0.1)
        public float FootBoxLength;     // Length of foot box (default 0.25)
        public float FootBoxHeight;     // Height of foot box (default 0.02)
        public float FootSphereRadius;  // Radius of foot sphere (default 0.02)

        public uint GroundLayerMask;
        [MarshalAs(UnmanagedType.U1)]
        public bool IgnoreTriggers;
        [MarshalAs(UnmanagedType.U1)]
        public bool EnableFootIK;                   // Master enable
    }
}
