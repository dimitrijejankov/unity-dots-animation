using Unity.Collections;
using Unity.Mathematics;
using System;
using static Unity.Mathematics.math;

namespace AnimationSystem
{
    /// <summary>
    /// 2D triangulated blend space for sampling animation indices using barycentric coordinates.
    /// </summary>
    public unsafe struct BlendSpace2D : IDisposable
    {
        NativeArray<AnimationIndex> m_Points;
        NativeArray<float2> m_Positions;
        NativeList<int3> m_Triangles;
        NativeList<int2> m_Boundaries;

        public BlendSpace2D(ReadOnlySpan<AnimationIndex> animations, ReadOnlySpan<float2> positions, Allocator allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (animations.Length != positions.Length)
                throw new InvalidOperationException("Animations and positions length must match.");
#endif
            int n = animations.Length;

            m_Points = new NativeArray<AnimationIndex>(n, allocator);
            animations.CopyTo(m_Points);

            m_Positions = new NativeArray<float2>(n, allocator);
            positions.CopyTo(m_Positions);

            m_Triangles = new NativeList<int3>(n, allocator);
            m_Boundaries = new NativeList<int2>(n, allocator);

            for (int i = 0; i < n - 2; i++)
            {
                for (int j = i + 1; j < n - 1; j++)
                {
                    for (int k = j + 1; k < n; k++)
                    {
                        float2 a = m_Positions[i];
                        float2 b = m_Positions[j];
                        float2 c = m_Positions[k];

                        float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

                        if (abs(cross) < EPSILON)
                            continue;

                        if (cross <= 0)
                        {
                            float2 temp = a;
                            a = b;
                            b = temp;
                        }

                        bool valid = true;
                        for (int m = 0; m < n; m++)
                        {
                            if (m == i || m == j || m == k)
                                continue;
                            if (IsPointInCircumcircle(a, b, c, m_Positions[m]))
                            {
                                valid = false;
                                break;
                            }
                        }
                        if (valid)
                            m_Triangles.Add(new int3(i, j, k));
                    }
                }
            }

            var edgeCounts = new NativeHashMap<int2, int>(n * 3, Allocator.Temp);

            foreach (var triangle in m_Triangles)
            {
                Span<int2> edges = stackalloc int2[3]
                {
                    triangle.x <= triangle.y ? int2(triangle.x, triangle.y) : int2(triangle.y, triangle.x),
                    triangle.y <= triangle.z ? int2(triangle.y, triangle.z) : int2(triangle.z, triangle.y),
                    triangle.z <= triangle.x ? int2(triangle.z, triangle.x) : int2(triangle.x, triangle.z),
                };

                for (int e = 0; e < 3; e++)
                {
                    int2 edge = edges[e];
                    if (edgeCounts.TryGetValue(edge, out int count))
                        edgeCounts[edge] = count + 1;
                    else
                        edgeCounts.Add(edge, 1);
                }
            }

            foreach (var edge in edgeCounts)
            {
                if (edge.Value == 1)
                    m_Boundaries.Add(edge.Key);
            }

            edgeCounts.Dispose();
        }

        /// <summary>
        /// Evaluates the blend space at the given 2D position and returns three animation indices
        /// with their barycentric weights.
        /// </summary>
        public (AnimationIndex, AnimationIndex, AnimationIndex, float3) Evaluate(float2 position)
        {
            foreach (var triangle in m_Triangles)
            {
                float3 barycentric = Barycentric(m_Positions[triangle.x], m_Positions[triangle.y], m_Positions[triangle.z], position);
                if (all(barycentric >= 0f & barycentric <= 1f))
                    return (m_Points[triangle.x], m_Points[triangle.y], m_Points[triangle.z], barycentric);
            }

            float minDistanceSq = float.MaxValue;
            int2 nearestLine = default;
            float nearestT = default;
            foreach (var line in m_Boundaries)
            {
                float2 a = m_Positions[line.x];
                float2 b = m_Positions[line.y];
                float2 ab = b - a;
                float2 ap = position - a;
                float t = dot(ap, ab) / dot(ab, ab);
                t = clamp(t, 0f, 1f);
                float2 closest = a + t * ab;
                float distanceSq = distancesq(position, closest);
                if (minDistanceSq > distanceSq)
                {
                    minDistanceSq = distanceSq;
                    nearestLine = line;
                    nearestT = t;
                }
            }
            return (m_Points[nearestLine.x], m_Points[nearestLine.y], AnimationIndex.Default, new float3(1 - nearestT, nearestT, 0));
        }

        public void Dispose()
        {
            m_Points.Dispose();
            m_Positions.Dispose();
            m_Triangles.Dispose();
            m_Boundaries.Dispose();
        }

        static bool IsPointInCircumcircle(float2 a, float2 b, float2 c, float2 p)
        {
            float ax = a.x - p.x, ay = a.y - p.y;
            float bx = b.x - p.x, by = b.y - p.y;
            float cx = c.x - p.x, cy = c.y - p.y;
            float det = (ax * ax + ay * ay) * (bx * cy - cx * by)
                      - (bx * bx + by * by) * (ax * cy - cx * ay)
                      + (cx * cx + cy * cy) * (ax * by - bx * ay);
            return det > 0f;
        }

        static float3 Barycentric(float2 a, float2 b, float2 c, float2 p)
        {
            float2 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = dot(v0, v0), d01 = dot(v0, v1), d11 = dot(v1, v1);
            float denom = d00 * d11 - d01 * d01;
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (abs(denom) < EPSILON)
                throw new InvalidOperationException("Points in blend space form degenerate triangle.");
#endif
            float d20 = dot(v2, v0), d21 = dot(v2, v1);
            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            return new float3(1f - v - w, v, w);
        }
    }
}
