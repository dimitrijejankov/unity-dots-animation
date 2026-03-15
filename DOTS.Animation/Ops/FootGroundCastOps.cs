using AnimationSystem.Settings;
using AnimationSystem;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace AnimationSystem.Ops
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Foot Ground Casting
    //
    // iStep-style dual cast used by FootPlacementOps.SetFootTerrainOffset:
    //   1. Box cast  — foot-shaped, primary surface detection.
    //   2. Sphere cast — slightly above the box start, refines the normal.
    //
    // The result whose normal is flatter (smaller SlopeAlpha) is selected.
    // Normals that face sideways (y < 0.1) are snapped to up() to prevent wall
    // contacts from producing bad foot placements.
    // ─────────────────────────────────────────────────────────────────────────────

    [BurstCompile]
    public static class FootGroundCastOps
    {
        public static GroundHit CastGround(
            float3 traceStart,
            float3 traceEnd,
            quaternion boxOrientation,
            in FootIKSettings settings,
            in CollisionFilter filter,
            in PhysicsWorld physicsWorld)
        {
            float3 traceDir = math.normalize(traceEnd - traceStart);

            // Box cast — foot-shaped collider oriented with the character
            float3 boxSize    = new float3(settings.FootBoxWidth, settings.FootBoxHeight, settings.FootBoxLength);
            var    boxCollider = BoxCollider.Create(
                new BoxGeometry { Center = float3.zero, Orientation = boxOrientation, Size = boxSize }, filter);
            var  boxCollector = new ClosestHitColliderCastCollector(1f);
            bool boxHit       = physicsWorld.CollisionWorld.CastCollider(
                new ColliderCastInput(boxCollider, traceStart, traceEnd), ref boxCollector);
            boxCollider.Dispose();

            GroundHit boxResult = GroundHit.Invalid;
            if (boxHit && boxCollector.NumHits > 0)
            {
                float3 contact = math.lerp(traceStart, traceEnd, boxCollector.ClosestHit.Fraction)
                               - new float3(0f, settings.FootBoxHeight * 0.5f, 0f);
                float3 normal  = boxCollector.ClosestHit.SurfaceNormal;
                if (normal.y < 0.1f) normal = math.up();
                boxResult = new GroundHit(contact, math.normalize(normal), traceDir);
            }

            // Sphere cast — started slightly above box origin to avoid starting inside geometry
            float3 sphereStart  = traceStart + new float3(0f, 0.02f, 0f);
            var    sphereCollider = SphereCollider.Create(
                new SphereGeometry { Radius = settings.FootSphereRadius }, filter);
            var  sphereCollector = new ClosestHitColliderCastCollector(1f);
            bool sphereHit       = physicsWorld.CollisionWorld.CastCollider(
                new ColliderCastInput(sphereCollider, sphereStart, traceEnd), ref sphereCollector);
            sphereCollider.Dispose();

            GroundHit sphereResult = GroundHit.Invalid;
            if (sphereHit && sphereCollector.NumHits > 0)
            {
                float3 contact = math.lerp(sphereStart, traceEnd, sphereCollector.ClosestHit.Fraction)
                               - new float3(0f, settings.FootSphereRadius, 0f);
                float3 normal  = sphereCollector.ClosestHit.SurfaceNormal;
                if (normal.y < 0.1f) normal = math.up();
                sphereResult = new GroundHit(contact, math.normalize(normal), traceDir);
            }

            // Select the result with the flatter normal (lower SlopeAlpha)
            if (!boxResult.IsValid && !sphereResult.IsValid) return GroundHit.Invalid;
            if (!boxResult.IsValid)    return sphereResult;
            if (!sphereResult.IsValid) return boxResult;
            return sphereResult.SlopeAlpha <= boxResult.SlopeAlpha ? sphereResult : boxResult;
        }

        // Converts a ground hit into a component-space position + rotation offset.
        // Handles slope clamping and lateral-movement blend reduction.
        public static void ComputeFootTarget(
            in GroundHit hit,
            float3 footFloor,
            in LocalTransform characterTransform,
            in FootIKSettings settings,
            float3 velocity,
            out float3 locationTarget,
            out quaternion rotationTarget)
        {
            float3 impactPoint  = hit.Point;
            float3 impactNormal = hit.Normal;

            float groundAngle = math.degrees(math.acos(math.saturate(impactNormal.y)));

            // Surfaces steeper than the detection limit are ignored
            if (groundAngle > settings.MaxGroundAngleForDetection)
            {
                locationTarget = float3.zero;
                rotationTarget = quaternion.identity;
                return;
            }

            // Surfaces steeper than the adaptation limit have their normal clamped
            if (groundAngle > settings.MaxGroundAngleForAdaptation)
                impactNormal = ClampNormalToAngle(impactNormal, settings.MaxGroundAngleForAdaptation);

            // World-space offset: (surface contact + ankle height) − (flat-floor + ankle height)
            quaternion invCharRot  = math.inverse(characterTransform.Rotation);
            float3     worldOffset = (impactPoint + impactNormal * settings.FootHeight)
                                   - (footFloor   + new float3(0f, settings.FootHeight, 0f));

            locationTarget = math.mul(invCharRot, worldOffset);
            rotationTarget = MathOps.FromToRotation(math.mul(invCharRot, math.up()), math.mul(invCharRot, impactNormal));

            // When strafing across a slope, reduce adaptation to avoid unnatural foot orientation
            float lateralBlend = ComputeLateralBlendWeight(velocity, impactNormal, settings.FootOffsetLateralReduction);
            locationTarget *= lateralBlend;
            rotationTarget  = math.slerp(quaternion.identity, rotationTarget, lateralBlend);
        }

        // Returns 1 when moving up/down slope, and reduces toward (1 − maxReduction)
        // when moving purely sideways across the slope.
        private static float ComputeLateralBlendWeight(float3 velocity, float3 slopeNormal, float maxReduction)
        {
            if (math.lengthsq(velocity) <= 0.01f)
                return 1f;

            float3 rawRight = math.cross(slopeNormal, math.up());
            if (math.lengthsq(rawRight) <= 0.001f) // flat ground — no lateral component
                return 1f;

            float3 slopeRight   = slopeNormal.y > 0.999f ? math.right() : math.normalize(rawRight);
            float3 slopeForward = math.normalize(math.cross(slopeRight, slopeNormal));
            float3 velDir       = math.normalize(velocity);

            float lateral  = math.abs(math.dot(velDir, slopeRight));
            float forward  = math.abs(math.dot(velDir, slopeForward));
            float total    = lateral + forward;

            float lateralFraction = total > 0.01f ? lateral / total : 0f;
            return math.saturate(1f - lateralFraction * maxReduction);
        }

        // Tilts a surface normal so that its angle to vertical equals targetAngleDeg.
        private static float3 ClampNormalToAngle(float3 normal, float targetAngleDeg)
        {
            float3 horizontal = normal - new float3(0f, normal.y, 0f);
            if (math.lengthsq(horizontal) < 1e-6f)
                return math.up();

            float  targetRad = math.radians(targetAngleDeg);
            float3 slopeDir  = math.normalize(horizontal);
            return math.normalize(new float3(
                slopeDir.x * math.sin(targetRad),
                math.cos(targetRad),
                slopeDir.z * math.sin(targetRad)));
        }
    }
}
