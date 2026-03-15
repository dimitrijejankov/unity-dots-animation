using System;
using AnimationSystem.Settings;
using Unity.Burst;
using Unity.Mathematics;

namespace AnimationSystem.Ops
{
    [BurstCompile]
    public static class StrideWarpOps
    {
        // Basic stride warping - applies foot IK positions and hip adjustment directly to the pose
        public static void CalculateFootIKTargets(
            Span<RigidTransform> pose,
            in StrideWarpSettings strideSettings,
            in IKSettings ikSettings,
            float strideScale,
            float3 warpDirection)
        {
            // Get component-space transforms for limbs
            var leftLimbCS = FootIKOps.ComputeLimbComponentSpace(
                pose,
                ikSettings.RootBoneIndex,
                ikSettings.HipBoneIndex,
                ikSettings.LeftThighBoneIndex,
                ikSettings.LeftShinBoneIndex,
                ikSettings.LeftFootBoneIndex);

            var rightLimbCS = FootIKOps.ComputeLimbComponentSpace(
                pose,
                ikSettings.RootBoneIndex,
                ikSettings.HipBoneIndex,
                ikSettings.RightThighBoneIndex,
                ikSettings.RightShinBoneIndex,
                ikSettings.RightFootBoneIndex);

            // Stage 1: Calculate stride pivot
            var rootTransform = pose[ikSettings.RootBoneIndex];

            var pivotRotation = quaternion.LookRotation(math.normalize(warpDirection), new float3(0, 1, 0));
            var pivotPosition = rootTransform.Position;
            if (strideSettings.ProjectToGround) pivotPosition.y = 0f;

            // Add offset
            var offset = math.mul(pivotRotation, new float3(0, 0, strideSettings.PivotOffset));
            pivotPosition += offset;

            var stridePivotTransform = new RigidTransform
            {
                Position = pivotPosition,
                Rotation = pivotRotation
            };

            // Stage 2: Process each limb (using component-space positions)
            var newLeftFootPos = ProcessStride(strideScale, leftLimbCS.ThighPosition, leftLimbCS.FootPosition,
                stridePivotTransform);
            var newRightFootPos = ProcessStride(strideScale, rightLimbCS.ThighPosition, rightLimbCS.FootPosition,
                stridePivotTransform);

            var leftHeightDelta = newLeftFootPos.y - leftLimbCS.FootPosition.y;
            var rightHeightDelta = newRightFootPos.y - rightLimbCS.FootPosition.y;

            // Stage 3: Calculate hip adjustment to prevent floating
            var highestDelta = math.max(leftHeightDelta, rightHeightDelta);
            var hipAdjust = -highestDelta * strideSettings.HipAdjustmentRatio;

            // Stage 4: Adjust feet based on hip adjustment
            if (rightHeightDelta > leftHeightDelta)
                newLeftFootPos.y += rightHeightDelta - leftHeightDelta;
            else
                newRightFootPos.y += leftHeightDelta - rightHeightDelta;

            // Stage 5: Apply hip adjustment directly to the hip bone
            if (ikSettings.HipBoneIndex >= 0 && ikSettings.HipBoneIndex < pose.Length)
            {
                ref var hipTransform = ref pose[ikSettings.HipBoneIndex];
                hipTransform.Position += new float3(0, hipAdjust, 0);
            }

            // Stage 6: Apply foot IK targets to virtual bones
            // The calculated positions are in component space. Virtual bones ik_foot_l/r are children of
            // ik_foot_root which is a child of Root. We need to transform from component space to the
            // local space of ik_foot_root (which is the parent of the virtual IK bones).

            // Calculate component space transform of Root
            var rootCs = rootTransform;

            if (ikSettings.IKFootLIndex >= 0 && ikSettings.IKFootLIndex < pose.Length)
            {
                // Transform from component space to local space relative to Root
                var localPos = math.mul(math.inverse(rootCs.Rotation), newLeftFootPos - rootCs.Position);

                ref var ikFootL = ref pose[ikSettings.IKFootLIndex];
                ikFootL.Position = localPos;
            }

            if (ikSettings.IKFootRIndex >= 0 && ikSettings.IKFootRIndex < pose.Length)
            {
                // Transform from component space to local space relative to Root
                var localPos = math.mul(math.inverse(rootCs.Rotation), newRightFootPos - rootCs.Position);

                ref var ikFootR = ref pose[ikSettings.IKFootRIndex];
                ikFootR.Position = localPos;
            }
        }

        private static float3 ProcessStride(
            float strideScale,
            float3 thighPos,
            float3 footPos,
            RigidTransform stridePivot)
        {
            // Transform foot to stride pivot space
            var footInPivotSpace = math.mul(math.inverse(stridePivot.Rotation), footPos - stridePivot.Position);

            // Scale the stride (Z axis in pivot space is forward)
            footInPivotSpace.z *= strideScale;

            // Transform back to world space
            var newFootPos = stridePivot.Position + math.mul(stridePivot.Rotation, footInPivotSpace);

            // Prevent leg extension - push foot back towards thigh if needed
            var currentLimbLength = math.length(footPos - thighPos);
            var newVector = newFootPos - thighPos;
            var newLength = math.length(newVector);

            if (newLength > currentLimbLength)
                newFootPos -= math.normalize(newVector) * (newLength - currentLimbLength);

            return newFootPos;
        }
    }
}