using System;
using Unity.Burst;
using Unity.Mathematics;

namespace AnimationSystem.Ops
{
    [BurstCompile]
    public static class InertializerOps
    {
        /// <summary>
        /// Computes polynomial inertialization coefficients using the library's
        /// Float3Inertia/QuaternionInertia (GDC 2018 approach).
        /// The inertia represents the additive error (source - target) that decays to zero.
        /// </summary>
        public static void Inertialize<T>(
            ReadOnlySpan<RigidTransform> previousPose,
            ReadOnlySpan<RigidTransform> sourcePose,
            ReadOnlySpan<RigidTransform> targetPose,
            float deltaTime,
            float duration,
            Span<BoneInertia<T>> inertias)
            where T : unmanaged
        {
            for (int i = 0; i < inertias.Length; i++)
            {
                inertias[i] = new BoneInertia<T>
                {
                    Position = Float3Inertia.Create(
                        previousPose[i].Position, sourcePose[i].Position, targetPose[i].Position,
                        deltaTime, duration),
                    Rotation = QuaternionInertia.Create(
                        previousPose[i].Rotation, sourcePose[i].Rotation, targetPose[i].Rotation,
                        deltaTime, duration),
                };
            }
        }

        /// <summary>
        /// Applies polynomial inertialization additively to the pose.
        /// Each bone's position/rotation is offset by the decaying error polynomial.
        /// </summary>
        public static void ApplyInertialization<T>(
            ref float timeState,
            ref bool inTransition,
            float duration,
            Span<BoneInertia<T>> inertias,
            float deltaTime,
            Span<RigidTransform> pose)
            where T : unmanaged
        {
            if (!inTransition) return;

            timeState += deltaTime;
            float time = math.min(timeState, duration);
            var timePower = new TimePower(time);

            // Only inertialize skeleton bones (inertias.Length <= pose.Length)
            for (int i = 0; i < inertias.Length; i++)
            {
                pose[i].Position += inertias[i].Position.Evaluate(timePower);
                pose[i].Rotation = math.normalize(
                    math.mul(inertias[i].Rotation.Evaluate(timePower), pose[i].Rotation));
            }

            if (time >= duration)
                inTransition = false;
        }

        public static void ApplyInertialization<T>(
            ref Inertializer<T> state,
            Span<BoneInertia<T>> inertias,
            float deltaTime,
            Span<RigidTransform> pose)
            where T : unmanaged
        {
            ApplyInertialization<T>(
                ref state.Time,
                ref state.InTransition,
                state.Duration,
                inertias,
                deltaTime,
                pose);
        }
    }
}
