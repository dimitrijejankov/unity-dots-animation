using AnimationSystem;
using Unity.Entities;
using UnityEngine;

namespace AnimationSystem.Authoring
{
    /// <summary>
    /// Add to a skinned mesh child GameObject to set up the SkinRef and SkinMatrix buffer
    /// needed by SkinMatrixFromBonePoseSystem.SkinParentFromPoseJob.
    /// The root character GameObject must have BendzRigAuthoring.
    /// </summary>
    public class BendzSkinAuthoring : MonoBehaviour
    {
        [Tooltip("The rig asset shared with the root character's BendzRigAuthoring.")]
        public BendzRig Rig;

        [Tooltip("Index into Rig.Skeleton.Skins (usually 0 for single-mesh characters).")]
        public int SkinIndex;

        public class BendzSkinAuthoringBaker : Baker<BendzSkinAuthoring>
        {
            public override void Bake(BendzSkinAuthoring authoring)
            {
                if (authoring.Rig == null)
                    return;

                var skeleton = authoring.Rig.GetOrCreateSkeleton();
                if (!skeleton.IsCreated)
                    return;

                ref var arm = ref skeleton.Value;
                if (authoring.SkinIndex < 0 || authoring.SkinIndex >= arm.Skins.Length)
                    return;

                var entity = GetEntity(TransformUsageFlags.Dynamic);

                // SkinMatrix buffer is already added by SkinnedMeshRendererBaker — only add SkinRef.
                AddSharedComponent(entity, new SkinRef
                {
                    Skeleton = skeleton,
                    SkinIndex = authoring.SkinIndex,
                });
            }
        }
    }
}
