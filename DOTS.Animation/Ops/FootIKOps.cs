using System;
using Unity.Burst;
using Unity.Mathematics;

namespace AnimationSystem
{
    [BurstCompile]
    public static class FootIKOps
    {
        // Helper struct to hold component-space transforms for a limb
        public struct LimbComponentSpace
        {
            public float3 ThighPosition;
            public quaternion ThighRotation;
            public float3 ShinPosition;
            public quaternion ShinRotation;
            public float3 FootPosition;
            public quaternion FootRotation;
        }

        // Compute component-space transforms for a limb chain
        // Assumes: Root -> Hip -> Thigh -> Shin -> Foot hierarchy
        public static LimbComponentSpace ComputeLimbComponentSpace(
            ReadOnlySpan<RigidTransform> pose,
            int rootIndex,
            int hipIndex,
            int thighIndex,
            int shinIndex,
            int footIndex)
        {
            // Start with root (already in component space since it has no parent)
            var rootCS = pose[rootIndex];

            // Hip in component space
            var hipLocal = pose[hipIndex];
            var hipCS = new RigidTransform
            {
                Position = rootCS.Position + math.mul(rootCS.Rotation, hipLocal.Position),
                Rotation = math.mul(rootCS.Rotation, hipLocal.Rotation)
            };

            // Thigh in component space
            var thighLocal = pose[thighIndex];
            var thighCS = new RigidTransform
            {
                Position = hipCS.Position + math.mul(hipCS.Rotation, thighLocal.Position),
                Rotation = math.mul(hipCS.Rotation, thighLocal.Rotation)
            };

            // Shin in component space
            var shinLocal = pose[shinIndex];
            var shinCS = new RigidTransform
            {
                Position = thighCS.Position + math.mul(thighCS.Rotation, shinLocal.Position),
                Rotation = math.mul(thighCS.Rotation, shinLocal.Rotation)
            };

            // Foot in component space
            var footLocal = pose[footIndex];
            var footCS = new RigidTransform
            {
                Position = shinCS.Position + math.mul(shinCS.Rotation, footLocal.Position),
                Rotation = math.mul(shinCS.Rotation, footLocal.Rotation)
            };

            return new LimbComponentSpace
            {
                ThighPosition = thighCS.Position,
                ThighRotation = thighCS.Rotation,
                ShinPosition = shinCS.Position,
                ShinRotation = shinCS.Rotation,
                FootPosition = footCS.Position,
                FootRotation = footCS.Rotation
            };
        }
    }
}
