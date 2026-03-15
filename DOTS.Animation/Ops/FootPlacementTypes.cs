using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Physics;

namespace AnimationSystem
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Foot Placement Support Types
    //
    // Shared structs and collectors used by FootPlacementOps and FootGroundCastOps.
    // ─────────────────────────────────────────────────────────────────────────────

    // Result of a ground cast. SlopeAlpha is acos(|dot(castDir, normal)|):
    // smaller = flatter surface = better candidate for foot placement.
    public struct GroundHit
    {
        public float3 Point;
        public float3 Normal;
        public float  SlopeAlpha;
        public bool   IsValid;

        public GroundHit(float3 point, float3 normal, float3 castDir)
        {
            Point      = point;
            Normal     = normal;
            SlopeAlpha = math.acos(math.abs(math.dot(castDir, normal)));
            IsValid    = true;
        }

        // Represents "no hit". IsValid is false by default construction.
        public static readonly GroundHit Invalid = default;
    }

    /// <summary>
    /// ALS-style foot lock state. Keeps a foot planted in component space
    /// while the capsule moves underneath.
    /// </summary>
    [System.Serializable]
    public struct FootLockState
    {
        public float Alpha;              // Current lock blend (0-1, can only decrease or snap to 1)
        [MarshalAs(UnmanagedType.U1)]
        public bool UseFootLockCurve;    // Hysteresis flag
        public float3 Location;          // Locked position in component space
        public quaternion Rotation;      // Locked rotation in component space
    }

    /// <summary>
    /// ALS-style foot offset state for terrain adaptation via raycasting.
    /// </summary>
    [System.Serializable]
    public struct FootOffsetState
    {
        public float3 LocationTarget;    // Raw raycast offset target (used for pelvis calculation)
        public float3 LocationOffset;    // Smoothed location offset (component space additive)
        public quaternion RotationOffset;// Smoothed rotation offset (component space additive)
    }

    /// <summary>
    /// Combined runtime state for ALS-style foot IK (foot locking + foot offsets + pelvis).
    /// </summary>
    [System.Serializable]
    public struct FootIKRuntimeState
    {
        [MarshalAs(UnmanagedType.U1)]
        public bool IsInitialized;
        public FootLockState LockL;
        public FootLockState LockR;
        public FootOffsetState OffsetL;
        public FootOffsetState OffsetR;
        public float3 PelvisOffset;      // Smoothed pelvis offset (component space)
        public float PelvisAlpha;
    }

    // Accumulates the closest collider-cast hit over the full sweep.
    public struct ClosestHitColliderCastCollector : ICollector<ColliderCastHit>
    {
        public bool  EarlyOutOnFirstHit => false;
        public float MaxFraction        { get; }
        public int   NumHits            { get; private set; }
        public ColliderCastHit ClosestHit;
        private float _closestFraction;

        public ClosestHitColliderCastCollector(float maxFraction)
        {
            MaxFraction      = maxFraction;
            NumHits          = 0;
            ClosestHit       = default;
            _closestFraction = maxFraction;
        }

        public bool AddHit(ColliderCastHit hit)
        {
            if (hit.Fraction < _closestFraction)
            {
                _closestFraction = hit.Fraction;
                ClosestHit       = hit;
            }
            NumHits++;
            return true;
        }
    }

    // Accumulates the closest raycast hit over the full sweep.
    public struct ClosestHitCollector : ICollector<RaycastHit>
    {
        public bool  EarlyOutOnFirstHit => false;
        public float MaxFraction        { get; }
        public int   NumHits            { get; private set; }
        public RaycastHit ClosestHit;
        private float _closestFraction;

        public ClosestHitCollector(float maxFraction)
        {
            MaxFraction      = maxFraction;
            NumHits          = 0;
            ClosestHit       = default;
            _closestFraction = maxFraction;
        }

        public bool AddHit(RaycastHit hit)
        {
            if (hit.Fraction < _closestFraction)
            {
                _closestFraction = hit.Fraction;
                ClosestHit       = hit;
            }
            NumHits++;
            return true;
        }
    }

    // Euler angle utilities. Used externally for foot lock yaw compensation.
    [BurstCompile]
    public static class MathUtil
    {
        // Decomposes a quaternion into XYZ Euler angles (pitch, yaw, roll) in radians.
        public static float3 ToEuler(quaternion q)
        {
            float4 v = q.value;

            float sinr_cosp = 2f * (v.w * v.x + v.y * v.z);
            float cosr_cosp = 1f - 2f * (v.x * v.x + v.y * v.y);
            float pitch = math.atan2(sinr_cosp, cosr_cosp);

            float sinp = 2f * (v.w * v.y - v.z * v.x);
            float yaw  = math.abs(sinp) >= 1f
                ? math.PI * 0.5f * math.sign(sinp)
                : math.asin(sinp);

            float siny_cosp = 2f * (v.w * v.z + v.x * v.y);
            float cosy_cosp = 1f - 2f * (v.y * v.y + v.z * v.z);
            float roll = math.atan2(siny_cosp, cosy_cosp);

            return new float3(pitch, yaw, roll);
        }
    }
}
