using System;

namespace AnimationSystem
{
    /// <summary>
    /// Specifies the type of additive blending to use.
    /// Ported from TorchlitTales for use in the generic animation package.
    /// </summary>
    public enum AdditiveType
    {
        /// <summary>
        /// Local space additive: delta = base^-1 * additive, applied as current * delta
        /// </summary>
        Local,

        /// <summary>
        /// Mesh/component space additive: delta computed in world space relative to root,
        /// useful for effects like leaning where the result should be in world orientation.
        /// </summary>
        MeshSpace
    }

    /// <summary>
    /// Marks an AnimationClip field as an additive animation that should be preprocessed
    /// during import. The additive delta is computed relative to the specified base animation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class AdditiveAttribute : Attribute
    {
        /// <summary>
        /// Name of the base animation clip field (e.g., "Run_BasePose")
        /// </summary>
        public string Base { get; }

        /// <summary>
        /// Type of additive blending to use
        /// </summary>
        public AdditiveType Type { get; }

        /// <summary>
        /// Specific frame index in the base animation to use for baking.
        /// -1 (default) = use default behavior (single-frame or synchronized sampling)
        /// >= 0 = use the specified frame index
        /// </summary>
        public int BaseFrame { get; }

        public AdditiveAttribute(string baseName, AdditiveType type = AdditiveType.Local, int baseFrame = -1)
        {
            Base = baseName;
            Type = type;
            BaseFrame = baseFrame;
        }
    }
}
