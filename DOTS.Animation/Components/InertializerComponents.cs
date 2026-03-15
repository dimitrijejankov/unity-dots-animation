using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystem
{
    /// <summary>
    /// Stores the parent-space pose from the previous frame.
    /// This is needed because the BonePose buffer gets transformed to root-space by SkinMatrixFromBonePoseSystem,
    /// but inertialization needs to compare poses in the same coordinate space.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct BoneHistory0<T> : IBufferElementData where T : unmanaged
    {
        public RigidTransform Value;
        public static BoneHistory0<T> Default => new() { Value = RigidTransform.Identity };
    }

    /// <summary>
    /// Stores the parent-space pose from two frames ago.
    /// Used to calculate bone velocities for inertialization.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct BoneHistory1<T> : IBufferElementData where T : unmanaged
    {
        public RigidTransform Value;
        public static BoneHistory1<T> Default => new() { Value = RigidTransform.Identity };
    }

    /// <summary>
    /// Inertia coefficients for smooth transition blending (GDC 2018 inertialization).
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct BoneInertia<T> : IBufferElementData where T : unmanaged
    {
        public Float3Inertia Position;
        public QuaternionInertia Rotation;
        public static BoneInertia<T> Default => new()
        {
            Rotation = new QuaternionInertia { Axis = math.right() }
        };
    }

    /// <summary>
    /// Generic inertializer component. The tag 'T' allows storing multiple
    /// identical components on the same entity (e.g. Loco, Overlay).
    /// 
    /// Usage:
    /// 1. Create a marker struct: public struct MyLayer {}
    /// 2. Register the component in your project: [assembly: RegisterGenericComponentType(typeof(Inertializer<MyLayer>))]
    /// 3. Add the component to your entity: entityManager.AddComponent<Inertializer<MyLayer>>(entity);
    /// 4. In your system, when a transition occurs:
    ///    - set inertializer.PendingDuration = fadeDuration;
    ///    - call InertializerOps.Inertialize<MyLayer>(...) in the same frame the transition logic triggers
    ///    - in the update loop, call InertializerOps.ApplyInertialization<MyLayer>(ref inertializer, ...)
    /// </summary>
    public struct Inertializer<T> : IComponentData where T : unmanaged
    {
        public float Duration;
        public float Time;
        public bool InTransition;
        public float PendingDuration;

        public static Inertializer<T> Default(float duration = 0.3f) => new()
        {
            Duration = duration,
        };
    }
}
