using System;
using AnimationSystem;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace AnimationSystem.Authoring
{
    /// <summary>
    /// Bakes MotionRef, SkeletonRef, and BonePose buffer onto the entity.
    /// Provides TryFindAnimationIndex / TryFindBoneIndex helpers.
    /// </summary>
    public class BendzRigAuthoring : MonoBehaviour
    {
        [SerializeField] public BendzRig Rig;

        public bool TryFindAnimationIndex(string name, out AnimationIndex index)
        {
            if (Rig == null) { index = AnimationIndex.Default; return false; }
            return Rig.TryFindAnimationIndex(name, out index);
        }

        public bool TryFindBoneIndex(string name, out int index)
        {
            if (Rig == null) { index = -1; return false; }
            return Rig.TryFindBoneIndex(name, out index);
        }

        public class BendzRigAuthoringBaker : Baker<BendzRigAuthoring>
        {
            public override void Bake(BendzRigAuthoring authoring)
            {
                if (authoring.Rig == null)
                    return;

                var entity = GetEntity(TransformUsageFlags.Dynamic);

                var motion = authoring.Rig.GetOrCreateMotion();
                var skeleton = authoring.Rig.GetOrCreateSkeleton();

                if (!motion.IsCreated || !skeleton.IsCreated)
                    return;

                // Add shared components for animation data
                AddSharedComponent(entity, new MotionRef { Value = motion });
                AddSharedComponent(entity, new SkeletonRef { Value = skeleton });

                // Pre-size the BonePose buffer
                int boneCount = skeleton.Value.BoneCount;
                var poseBuffer = AddBuffer<BonePose>(entity);
                poseBuffer.ResizeInitialized(boneCount, BonePose.Default);
            }
        }
    }
}
