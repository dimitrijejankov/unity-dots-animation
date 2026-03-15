using Unity.Burst;
using Unity.Mathematics;

namespace AnimationSystem.Ops
{
    [BurstCompile]
    public static class MathOps
    {
        public static float InterpTo(float current, float target, float deltaTime, float interpSpeed)
        {
            if (interpSpeed <= 0f) return target;
            float dist = target - current;
            if (math.abs(dist) < 0.0001f) return target;
            return current + dist * (1.0f - math.exp(-interpSpeed * deltaTime));
        }

        public static float4 InterpTo(float4 current, float4 target, float deltaTime, float interpSpeed)
        {
            if (interpSpeed <= 0f) return target;
            float4 dist = target - current;
            return current + dist * (1.0f - math.exp(-interpSpeed * deltaTime));
        }

        public static float3 InterpTo(float3 current, float3 target, float deltaTime, float interpSpeed)
        {
            if (interpSpeed <= 0f) return target;
            float3 dist = target - current;
            return current + dist * (1.0f - math.exp(-interpSpeed * deltaTime));
        }

        public static float2 InterpTo(float2 current, float2 target, float deltaTime, float interpSpeed)
        {
            if (interpSpeed <= 0f) return target;
            float2 dist = target - current;
            return current + dist * (1.0f - math.exp(-interpSpeed * deltaTime));
        }

        public static float MapRangeClamped(float value, float inRangeA, float inRangeB, float outRangeA, float outRangeB)
        {
            float t = (value - inRangeA) / (inRangeB - inRangeA);
            t = math.clamp(t, 0.0f, 1.0f);
            return math.lerp(outRangeA, outRangeB, t);
        }

        // Shortest-arc quaternion from direction `from` to direction `to`.
        public static quaternion FromToRotation(float3 from, float3 to)
        {
            float dot = math.dot(from, to);

            if (dot > 0.99999f) return quaternion.identity;

            if (dot < -0.99999f) // antiparallel — pick any perpendicular axis
            {
                float3 perp = math.abs(from.x) < 0.9f ? new float3(1f, 0f, 0f) : new float3(0f, 1f, 0f);
                float3 axis = math.normalize(math.cross(from, perp));
                return new quaternion(axis.x, axis.y, axis.z, 0f);
            }

            float3 cross = math.cross(from, to);
            return math.normalize(new quaternion(cross.x, cross.y, cross.z, 1f + dot));
        }

        // Spherically interpolates toward target at a fixed rate. 
        public static quaternion InterpTo(quaternion current, quaternion target, float deltaTime, float interpSpeed)
        {
            if (interpSpeed <= 0f) return target;
            float t = 1.0f - math.exp(-interpSpeed * deltaTime);
            return math.slerp(current, target, math.saturate(t));
        }
    }
}
