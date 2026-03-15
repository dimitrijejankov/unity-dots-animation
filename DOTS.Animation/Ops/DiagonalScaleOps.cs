using System;
using AnimationSystem.Settings;
using Unity.Burst;
using Unity.Mathematics;

namespace AnimationSystem.Ops
{
    /// <summary>
    /// Diagonal scaling operation that scales the IK foot bones on X/Y axes during diagonal movement.
    /// which spreads the feet apart during high-speed turns to enhance the visual effect.
    ///
    /// In ALS, this targets the ik_foot_root bone with additive scaling in component space.
    /// In this implementation, we scale the virtual IK foot bone positions (IKFootL/IKFootR),
    /// which are used as targets by the IK system. This spreads the IK targets apart,
    /// and the IK solver will adjust the leg positions accordingly.
    /// </summary>
    [BurstCompile]
    public static class DiagonalScaleOps
    {
        private const float MaxAdditiveScale = 0.4f; // Adds up to 0.4 to the base scale of 1.0

        /// <summary>
        /// Applies diagonal scaling by spreading the IK foot target bones apart.
        /// </summary>
        /// <param name="pose">The pose to modify (in local space)</param>
        /// <param name="ikSettings">IK settings containing bone indices</param>
        /// <param name="diagonalScaleAmount">Scale amount from 0 (no scaling) to 1 (full 1.4x scale on X/Y)</param>
        public static void Apply(
            Span<RigidTransform> pose,
            in IKSettings ikSettings,
            float diagonalScaleAmount)
        {
            // Early out if no scaling needed
            if (diagonalScaleAmount < 0.001f)
                return;

            // Calculate the scale multiplier
            // At full strength (alpha = 1.0), adds 0.4 to the base scale (1.0 -> 1.4)
            float scaleMultiplier = 1.0f + (MaxAdditiveScale * diagonalScaleAmount);

            // Scale the IK foot bone positions to spread them apart
            // These are virtual bones used as IK targets, children of the root
            // Scaling their positions on X/Y will spread the IK targets apart
            ScaleBonePosition(pose, ikSettings.IKFootLIndex, scaleMultiplier);
            ScaleBonePosition(pose, ikSettings.IKFootRIndex, scaleMultiplier);
        }

        private static void ScaleBonePosition(Span<RigidTransform> pose, int boneIndex, float scaleMultiplier)
        {
            if (boneIndex < 0 || boneIndex >= pose.Length)
                return;

            ref var transform = ref pose[boneIndex];

            // Scale the position on X and Y axes (horizontal plane)
            // Z is forward, so we don't scale it to avoid changing stride length
            var scaledPosition = new float3(
                transform.Position.x * scaleMultiplier,
                transform.Position.y * scaleMultiplier,
                transform.Position.z
            );

            transform.Position = scaledPosition;
        }
    }
}
