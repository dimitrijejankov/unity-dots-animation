using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Deformations;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace AnimationSystem
{
    /// <summary>
    /// Combines parent-to-root pose conversion and skin matrix computation into a single system.
    ///
    /// SkinFromBonePoseJob  — for entities where BonePose and SkinMatrix are on the same entity.
    /// SkinParentFromPoseJob — for child skinned mesh entities that reference the parent's BonePose via SkinRef.
    /// </summary>
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(DeformationsInPresentation))]
    public partial struct SkinMatrixFromBonePoseSystem : ISystem
    {
        BufferLookup<BonePose> m_BonePoseLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_BonePoseLookup = state.GetBufferLookup<BonePose>(true);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state) { }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_BonePoseLookup.Update(ref state);

            // Step 1: Convert parent-space BonePose → root-space (component space) in place.
            // Runs on entities that have both BonePose and SkeletonRef.
            var poseToRootHandle = new PoseToRootJob().ScheduleParallel(state.Dependency);

            // Step 2a: Compute skin matrices for entities where SkinMatrix is on the same entity as BonePose.
            var skinSameEntityHandle = new SkinFromBonePoseJob().ScheduleParallel(poseToRootHandle);

            // Step 2b: Compute skin matrices for child skinned mesh entities (SkinRef pattern).
            var skinParentHandle = new SkinParentFromPoseJob
            {
                BonePoseLookup = m_BonePoseLookup,
            }.ScheduleParallel(poseToRootHandle);

            state.Dependency = Unity.Jobs.JobHandle.CombineDependencies(skinSameEntityHandle, skinParentHandle);
        }

        /// <summary>
        /// Converts BonePose from parent-space to root-space (component space) in place.
        /// Assumes parent indices are stored in topological order (parent index < child index).
        /// </summary>
        [BurstCompile]
        [WithNone(typeof(Parent))]
        partial struct PoseToRootJob : IJobEntity
        {
            void Execute(ref DynamicBuffer<BonePose> pose, in SkeletonRef skeletonRef)
            {
                ref var skeleton = ref skeletonRef.Value.Value;
                for (int i = 0; i < skeleton.BoneCount; i++)
                {
                    int parent = skeleton.BoneParentIndices[i];
                    if (parent == -1) continue;
                    pose.ElementAt(i).Value = pose[parent].Value.TransformTransform(pose[i].Value);
                }
            }
        }

        /// <summary>
        /// Computes skin matrices from root-space BonePose when SkinMatrix is on the same entity.
        /// </summary>
        [BurstCompile]
        partial struct SkinFromBonePoseJob : IJobEntity
        {
            void Execute(ref DynamicBuffer<SkinMatrix> skinMatrices, in DynamicBuffer<BonePose> pose, in SkeletonRef skeletonRef)
            {
                ref var skeleton = ref skeletonRef.Value.Value;
                var skin = skeleton.Skins[0];
                for (int bindIndex = skin.Begin; bindIndex < skin.End; bindIndex++)
                {
                    int boneIdx = skeleton.SkinBoneIndices[bindIndex];
                    if (boneIdx == -1)
                    {
                        skinMatrices.ElementAt(bindIndex - skin.Begin) = new SkinMatrix
                        {
                            Value = ToFloat3x4(float4x4.identity)
                        };
                        continue;
                    }
                    var mat = math.mul(pose[boneIdx].Value.ToMatrix(), skeleton.SkinBindPoses[bindIndex]);
                    skinMatrices.ElementAt(bindIndex - skin.Begin) = new SkinMatrix
                    {
                        Value = ToFloat3x4(mat)
                    };
                }
            }
        }

        /// <summary>
        /// Computes skin matrices for a child skinned mesh entity that references a parent's BonePose via SkinRef.
        /// Also updates the LocalTransform of the skin entity to follow the root bone.
        /// </summary>
        [BurstCompile]
        partial struct SkinParentFromPoseJob : IJobEntity
        {
            [ReadOnly] public BufferLookup<BonePose> BonePoseLookup;

            void Execute(ref DynamicBuffer<SkinMatrix> skinMatrices, ref LocalTransform transform, in Parent parent, in SkinRef skinRef)
            {
                if (!BonePoseLookup.TryGetBuffer(parent.Value, out var pose))
                    return;

                ref var skeleton = ref skinRef.Skeleton.Value;
                var skin = skeleton.Skins[skinRef.SkinIndex];

                var localToRoot = pose[skin.Root];
                var rootToLocal = localToRoot.Value.ToInverseMatrix();
                transform.Position = localToRoot.Value.Position;
                transform.Rotation = localToRoot.Value.Rotation;

                for (int bindIndex = skin.Begin; bindIndex < skin.End; bindIndex++)
                {
                    int boneIdx = skeleton.SkinBoneIndices[bindIndex];
                    if (boneIdx == -1)
                    {
                        skinMatrices.ElementAt(bindIndex - skin.Begin) = new SkinMatrix
                        {
                            Value = ToFloat3x4(rootToLocal)
                        };
                        continue;
                    }
                    var poseTransform = math.mul(rootToLocal, pose[boneIdx].Value.ToMatrix());
                    var mat = math.mul(poseTransform, skeleton.SkinBindPoses[bindIndex]);
                    skinMatrices.ElementAt(bindIndex - skin.Begin) = new SkinMatrix
                    {
                        Value = ToFloat3x4(mat)
                    };
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float3x4 ToFloat3x4(float4x4 value) =>
            new(value.c0.xyz, value.c1.xyz, value.c2.xyz, value.c3.xyz);
    }
}
