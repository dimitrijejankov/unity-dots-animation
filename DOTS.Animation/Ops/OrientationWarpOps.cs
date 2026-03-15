using System;
using Unity.Burst;
using Unity.Mathematics;

namespace AnimationSystem
{
    [BurstCompile]
    public static class OrientationWarpOps
    {
        // calculates target warp angle (shortest delta)
        private static float WrapAngle(float angle)
        {
            return ((angle + 180f) % 360f + 360f) % 360f - 180f;
        }

        public static unsafe void Apply(
            Span<RigidTransform> pose,
            in OrientationWarpSettings settings,
            float locomotionAngle,
            float animationAngle,
            float alpha)
        {
            if (alpha < 0.0001f)
                return;

            var targetAngle = WrapAngle(locomotionAngle - animationAngle);

            // 1. Clamp by max warp angle
            var angle = math.clamp(targetAngle, -settings.maxWarpAngle, settings.maxWarpAngle);
            var angleRad = math.radians(angle) * alpha;

            if (math.abs(angleRad) < 0.0001f)
                return;

            var upAxis = new float3(0, 1, 0);
            var warpRotation = quaternion.AxisAngle(upAxis, angleRad);

            // 1. Apply to Root/Pelvis (Component Space)
            if (settings.rootBoneIndex < 0 || settings.rootBoneIndex >= pose.Length) return;

            ref var rootTransform = ref pose[settings.rootBoneIndex];

            rootTransform.Rotation = math.mul(warpRotation, rootTransform.Rotation);

            // 4. Apply to Spine (Counter-Rotate relative to Model-Space Up)
            if (settings.SpineBones.Count <= 0) return;

            var counterAnglePerBone = -angleRad / settings.SpineBones.Count;
            var counterRotModel = quaternion.AxisAngle(upAxis, counterAnglePerBone);

            // Calculate the parent rotation for spine bones
            // If root is not the direct parent (e.g., Root -> Hips -> Spine), include the hip rotation
            var currentParentCs = rootTransform.Rotation;
            if (settings.hipBoneIndex >= 0 && settings.hipBoneIndex < pose.Length && settings.hipBoneIndex != settings.rootBoneIndex)
            {
                ref var hipTransform = ref pose[settings.hipBoneIndex];
                currentParentCs = math.mul(currentParentCs, hipTransform.Rotation);
            }

            for (var i = 0; i < settings.SpineBones.Count; i++)
            {
                var boneIndex = settings.SpineBones.Indices[i];
                if (boneIndex < 0 || boneIndex >= pose.Length) continue;
                ref var spineTransform = ref pose[boneIndex];

                // Transform Model-Space counter-rotation into local parent-relative space
                var worldToParent = math.conjugate(currentParentCs);
                var counterRotLocal = math.mul(math.mul(worldToParent, counterRotModel), currentParentCs);

                spineTransform.Rotation = math.mul(counterRotLocal, spineTransform.Rotation);

                // Update chain tracker
                currentParentCs = math.mul(currentParentCs, spineTransform.Rotation);
            }
        }
    }
}
