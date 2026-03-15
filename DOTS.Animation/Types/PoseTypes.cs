using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

namespace AnimationSystem
{
    /// <summary>
    /// Position and rotation of a bone.
    /// </summary>
    [BurstCompile]
    public struct RigidTransform
    {
        public float3 Position;
        public quaternion Rotation;

        public static readonly RigidTransform Identity = new RigidTransform { Rotation = quaternion.identity };

        public static RigidTransform FromMatrix(float4x4 matrix)
        {
            var position = matrix.c3.xyz;
            float3x3 normalizedRotationMatrix = math.orthonormalize(new float3x3(matrix));
            var rotation = new quaternion(normalizedRotationMatrix);
            var transform = default(RigidTransform);
            transform.Position = position;
            transform.Rotation = rotation;
            return transform;
        }

        public static RigidTransform FromPositionRotation(float3 position, quaternion rotation) =>
            new() { Position = position, Rotation = rotation };

        public static RigidTransform FromPosition(float3 position) =>
            new() { Position = position, Rotation = quaternion.identity };

        public static RigidTransform FromRotation(quaternion rotation) =>
            new() { Position = float3.zero, Rotation = rotation };

        public float3 Right() => TransformDirection(math.right());
        public float3 Up() => TransformDirection(math.up());
        public float3 Forward() => TransformDirection(math.forward());

        public float3 TransformPoint(float3 point) => Position + math.rotate(Rotation, point);
        public float3 InverseTransformPoint(float3 point) => math.rotate(math.conjugate(Rotation), point - Position);
        public float3 TransformDirection(float3 direction) => math.rotate(Rotation, direction);
        public float3 InverseTransformDirection(float3 direction) => math.rotate(math.conjugate(Rotation), direction);
        public quaternion TransformRotation(quaternion rotation) => math.mul(Rotation, rotation);
        public quaternion InverseTransformRotation(quaternion rotation) => math.mul(math.conjugate(Rotation), rotation);

        public RigidTransform TransformTransform(in RigidTransform transformData) => new()
        {
            Position = TransformPoint(transformData.Position),
            Rotation = TransformRotation(transformData.Rotation),
        };

        public RigidTransform InverseTransformTransform(in RigidTransform transformData) => new()
        {
            Position = InverseTransformPoint(transformData.Position),
            Rotation = InverseTransformRotation(transformData.Rotation),
        };

        public RigidTransform Inverse()
        {
            var inverseRotation = math.conjugate(Rotation);
            return new()
            {
                Position = -math.rotate(inverseRotation, Position),
                Rotation = inverseRotation,
            };
        }

        public float4x4 ToMatrix() => float4x4.TRS(Position, Rotation, 1.0f);
        public float4x4 ToInverseMatrix() => Inverse().ToMatrix();

        public RigidTransform WithPosition(float3 position) => new() { Position = position, Rotation = Rotation };
        public RigidTransform WithRotation(quaternion rotation) => new() { Position = Position, Rotation = rotation };
        public RigidTransform Translate(float3 translation) => new() { Position = Position + translation, Rotation = Rotation };
        public RigidTransform Rotate(quaternion rotation) => new() { Position = Position, Rotation = math.mul(Rotation, rotation) };
        public RigidTransform RotateX(float angle) => Rotate(quaternion.RotateX(angle));
        public RigidTransform RotateY(float angle) => Rotate(quaternion.RotateY(angle));
        public RigidTransform RotateZ(float angle) => Rotate(quaternion.RotateZ(angle));

        public bool Equals(in RigidTransform other) =>
            Position.Equals(other.Position) && Rotation.Equals(other.Rotation);

        public override string ToString() => $"Position={Position} Rotation={Rotation}";
    }

    /// <summary>
    /// Lightweight index into Motion.Animations array.
    /// </summary>
    [Serializable]
    public struct AnimationIndex
    {
        [UnityEngine.SerializeField]
        internal int m_Value;

        public int Value => m_Value;

        public static AnimationIndex Default => new();
        public static bool operator ==(AnimationIndex lhs, AnimationIndex rhs) => lhs.m_Value == rhs.m_Value;
        public static bool operator !=(AnimationIndex lhs, AnimationIndex rhs) => lhs.m_Value != rhs.m_Value;
        public bool Equals(AnimationIndex other) => m_Value == other.m_Value;
        public override bool Equals(object other) => other is AnimationIndex && Equals((AnimationIndex)other);
        public override int GetHashCode() => m_Value.GetHashCode();
        public static AnimationIndex FromIndex(int index) => new() { m_Value = index };
    }

    /// <summary>
    /// Precomputed powers of time for polynomial inertia evaluation.
    /// </summary>
    public struct TimePower
    {
        public float Value;
        public float4 Powers;

        public TimePower(float time)
        {
            Value = time;
            Powers.x = Value * time;
            Powers.y = Powers.x * time;
            Powers.z = Powers.y * time;
            Powers.w = Powers.z * time;
        }
    }

    /// <summary>
    /// Optimized single float inertia coefficients.
    /// Based on https://www.gdcvault.com/play/1025331/Inertialization-High-Performance-Animation-Transitions.
    /// </summary>
    public struct InertializationCoefficientsOptimized
    {
        public float4 DCBA;
        public float x0;
        public float v0;
        public float t1;

        static readonly float4x3 Constant = new float4x3(
            new float4(0, -3, 3, -1),
            new float4(-4, -6, 8, -3),
            new float4(-10, -10, 15, -6));

        public float Evaluate(in TimePower t)
        {
            if (t.Value >= t1)
                return 0;
            return math.dot(DCBA, t.Powers) + v0 * t.Value + x0;
        }

        public static InertializationCoefficientsOptimized Create(float x0, float v0, float t1)
        {
            if (math.abs(v0) > math.EPSILON)
                t1 = math.min(t1, math.abs(5 * x0 / v0));

            float v0t1 = v0 * t1;
            float a0 = (-4 * v0t1 - 10 * x0);

            float4 numerator = math.mul(Constant, new float3(a0, v0t1, x0));

            float4 denominator;
            denominator.x = t1 * t1;
            denominator.y = denominator.x * t1;
            denominator.z = denominator.y * t1;
            denominator.w = denominator.z * t1;

            return new InertializationCoefficientsOptimized
            {
                DCBA = numerator / denominator,
                x0 = x0,
                v0 = v0,
                t1 = t1,
            };
        }
    }

    /// <summary>
    /// Single float3 inertia data.
    /// Based on https://www.gdcvault.com/play/1025331/Inertialization-High-Performance-Animation-Transitions.
    /// </summary>
    public struct Float3Inertia
    {
        public InertializationCoefficientsOptimized Magnitude;
        public float3 Axis;

        public float3 Evaluate(in TimePower t)
        {
            float magnitude = Magnitude.Evaluate(t);
            return magnitude * Axis;
        }

        public static Float3Inertia Create(float3 previous, float3 source, float3 target, float deltaTime, float duration)
        {
            var translation = source - target;
            var magnitude = math.length(translation);
            var direction = math.normalizesafe(translation);
            var previousTranslation = previous - target;
            var previousMagnitude = math.dot(previousTranslation, direction);
            var velocity = deltaTime != 0 ? (magnitude - previousMagnitude) / deltaTime : 0;
            return new Float3Inertia
            {
                Magnitude = InertializationCoefficientsOptimized.Create(magnitude, velocity, duration),
                Axis = direction,
            };
        }
    }

    /// <summary>
    /// Single quaternion inertia data.
    /// Based on https://www.gdcvault.com/play/1025331/Inertialization-High-Performance-Animation-Transitions.
    /// </summary>
    public struct QuaternionInertia
    {
        const float EpsilonNormalSqrt = 1e-15f;

        public InertializationCoefficientsOptimized Angle;
        public float3 Axis;

        public quaternion Evaluate(in TimePower t)
        {
            float angle = Angle.Evaluate(t);
            return quaternion.AxisAngle(Axis, angle);
        }

        public static QuaternionInertia Create(quaternion previous, quaternion source, quaternion target, float deltaTime, float duration)
        {
            var inverseTarget = math.inverse(target);

            var rotation = math.normalize(math.mul(source, inverseTarget));
            var (axis, angle) = ToAxisAngle(rotation);

            if (angle > math.PI)
            {
                angle = math.PI2 - angle;
                axis = -axis;
            }

            var previousRotation = math.mul(previous, inverseTarget);
            var previousAngle = UnwindOnce(2.0f * math.atan2(math.dot(previousRotation.value.xyz, axis), previousRotation.value.w));
            var velocity = UnwindOnce(deltaTime != 0 ? (angle - previousAngle) / deltaTime : 0);

            return new QuaternionInertia
            {
                Angle = InertializationCoefficientsOptimized.Create(angle, velocity, duration),
                Axis = axis,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (float3 axis, float angle) ToAxisAngle(quaternion q)
        {
            float denom = math.sqrt(1f - q.value.w * q.value.w);
            return (math.select(q.value.xyz * math.rcp(denom), math.float3(1f, 0f, 0f), math.abs(denom) < EpsilonNormalSqrt), 2f * math.acos(math.clamp(q.value.w, -1f, 1f)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float UnwindOnce(float a)
        {
            if (a < -math.PI) a += math.PI2;
            if (a > math.PI) a -= math.PI2;
            return a;
        }
    }

    /// <summary>
    /// Interpolation mode for pose sampling.
    /// </summary>
    public enum SamplingMode
    {
        Interpolated,
        Nearest,
    }
}
