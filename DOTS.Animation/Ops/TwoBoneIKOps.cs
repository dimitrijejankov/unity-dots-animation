using Unity.Burst;
using Unity.Mathematics;

namespace AnimationSystem
{
    [BurstCompile]
    public static class TwoBoneIKOps
    {
        // Two bone IK solver
        // a - base bone (thigh)
        // b - mid bone (shin)
        // c - tip bone (foot)
        // t - IK target position
        // a_gr - base bone global rotation
        // b_gr - mid bone global rotation
        // a_lr - base bone local rotation (modified)
        // b_lr - mid bone local rotation (modified)
        public static void Solve(
            float3 a, float3 b, float3 c, float3 t,
            quaternion a_gr, quaternion b_gr,
            ref quaternion a_lr, ref quaternion b_lr,
            float eps = 0.01f)
        {
            if (math.distancesq(t, c) < 0.00001f)
                return;

            var l_ab = math.length(b - a);
            var l_cb = math.length(b - c);
            var l_at = math.clamp(math.length(t - a), eps, l_ab + l_cb - eps);

            var ac_ab_0 = math.acos(math.clamp(math.dot(math.normalize(c - a), math.normalize(b - a)), -1f, 1f));
            var ba_bc_0 = math.acos(math.clamp(math.dot(math.normalize(a - b), math.normalize(c - b)), -1f, 1f));
            var ac_at_0 = math.acos(math.clamp(math.dot(math.normalize(c - a), math.normalize(t - a)), -1f, 1f));

            var ac_ab_1 =
                math.acos(math.clamp((l_cb * l_cb - l_ab * l_ab - l_at * l_at) / (-2f * l_ab * l_at), -1f, 1f));
            var ba_bc_1 =
                math.acos(math.clamp((l_at * l_at - l_ab * l_ab - l_cb * l_cb) / (-2f * l_ab * l_cb), -1f, 1f));

            var axis0 = math.normalize(math.cross(c - a, b - a));
            var axis1 = math.normalize(math.cross(c - a, t - a));

            var r0 = quaternion.AxisAngle(math.mul(math.inverse(a_gr), axis0), ac_ab_1 - ac_ab_0);
            var r1 = quaternion.AxisAngle(math.mul(math.inverse(b_gr), axis0), ba_bc_1 - ba_bc_0);
            var r2 = quaternion.AxisAngle(math.mul(math.inverse(a_gr), axis1), ac_at_0);

            a_lr = math.mul(a_lr, math.mul(r0, r2));
            b_lr = math.mul(b_lr, r1);
        }
    }
}
