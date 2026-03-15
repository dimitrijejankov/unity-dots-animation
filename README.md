# Simple DOTS Animation

High-performance, stateless animation library for the Unity DOTS stack.

## Overview

This package provides a fast, Burst-compatible animation runtime built on top of the Entity Component System (ECS). It focuses on a **stateless pose pipeline**, where animations are sampled into a buffer and then modified by various operations (blending, IK, warping) before being sent to the skinning system.

## Key Features

- **Blob-Based Assets:** Animations (`Motion`) and Rigs (`Skeleton`) are stored as efficient, serialized `BlobAssetReference` data.
- **Stateless Pose Operations:** A library of `Ops` (in `AnimationSystem.Ops`) for blending, IK, and warping that operate on `Span<RigidTransform>` pose buffers.
- **Advanced Blending:** Support for local/mesh-space blending, additive blending, and layered blending based on bone hierarchy.
- **Inertialization:** High-quality, high-performance transitions based on GDC 2018 techniques.
- **Inverse Kinematics (IK):** Built-in Two-Bone IK and Foot IK for terrain adaptation.
- **Procedural Warping:** Stride Warping and Orientation Warping for realistic locomotion.
- **Virtual Bones & Curves:** Support for custom curve data stored alongside bone tracks.
- **Optimized Skinning:** Parallelized, Burst-compiled skinning systems for handling thousands of characters.

## Installation

Add the package via the Unity Package Manager using the git URL of this repository.

### Dependencies
- `com.unity.entities`: 1.0.11+
- `com.unity.entities.graphics`: 1.0.11+

## Usage

### 1. Rig Setup
1. Use the **Bendz Rig Importer** (in the Unity Editor) to bake your FBX animations into a `BendzRig` asset containing `Motion` and `Skeleton` blobs.
2. Add the `BendzRigAuthoring` component to your root character entity.
3. Assign the `BendzRig` asset to the `Rig` field.

### 2. Skinning Setup
1. On each child GameObject with a `SkinnedMeshRenderer`, add the `BendzSkinAuthoring` component.
2. Assign the same `BendzRig` asset.
3. Ensure the `SkinIndex` matches the correct skin in the rig (usually 0).

### 3. Sampling & Pose Pipeline
The system is designed to be sampled manually within your own ECS systems. This allows for complex state machines or procedural animation logic.

```csharp
using AnimationSystem;
using AnimationSystem.Ops;

// Inside an ISystem.OnUpdate or Job
var rig = SystemAPI.GetSharedComponent<SkeletonRef>(entity);
var motion = SystemAPI.GetSharedComponent<MotionRef>(entity);
var poseBuffer = SystemAPI.GetBuffer<BonePose>(entity);

// Convert buffer to Span for Ops
var poseSpan = poseBuffer.AsTransformArray().AsSpan();

// Sample an animation at time T
PoseOps.SamplePoseAtTime(
    ref motion.Value.Value, 
    animationIndex, 
    time, 
    SamplingMode.Interpolated,
    poseSpan);
```

### 4. Advanced Operations
Use the `Ops` library to apply procedural modifications:

```csharp
// Apply Two-Bone IK to a leg
TwoBoneIKOps.ApplyTwoBoneIK(
    poseSpan, 
    upperIdx, midIdx, lowerIdx, 
    targetPos, 
    poleVector, 
    skeleton.Value.Value.ParentIndices);
```

## Core Components

- `MotionRef`: Shared component holding the `Motion` blob (animation tracks).
- `SkeletonRef`: Shared component holding the `Skeleton` blob (hierarchy, bind poses, skins).
- `BonePose`: Buffer element storing the current local-space pose of each bone.
- `BoneInertia`: Buffer element for tracking inertialization state.
- `SkinRef`: Shared component on skinned meshes to link them to a rig.

## Pose Pipeline Logic

1. **Sample:** Sample raw animation(s) into the `BonePose` buffer.
2. **Modify:** Use `Ops` to apply blending, additive layers, IK, and warping.
3. **Inertialize:** (Optional) If transitioning, apply `InertializerOps` for a smooth pop-free result.
4. **Skin:** `SkinMatrixFromBonePoseSystem` automatically processes all entities with a `BonePose` buffer and `SkinRef` component.
