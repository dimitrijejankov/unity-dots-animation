using Unity.Entities;
using Unity.Transforms;

namespace AnimationSystem
{
    /// <summary>
    /// System group for animation playback systems.
    /// Runs before TransformSystemGroup to ensure poses are computed before transform propagation.
    /// </summary>
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial class AnimationPlaybackSystemGroup : ComponentSystemGroup { }

    /// <summary>
    /// System group for the main character animation pipeline (sampling, blending, IK, etc.).
    /// Runs before pose-to-root conversion and skinning.
    /// </summary>
    [UpdateInGroup(typeof(AnimationPlaybackSystemGroup))]
    public partial class PlayerSystemGroup : ComponentSystemGroup { }
}
