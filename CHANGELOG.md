# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2026-09-21

### Added
- **Dynamic Wildfire & Cellular Automaton Burn Simulation**:
  - `ModernGrassBurnSim.compute` real-time 4-channel ping-pong simulation: Scorch ($R$), Flame ($G$), Fuel ($B$), and Heat ($A$).
  - Multi-harmonic organic burn shape with wind-driven propagation and turbulent edge noise.
  - Decoupled **Heat-Singed Perimeter Tall Grass Effect**: Outer crater/perimeter standing tall grass retains full height while developing realistic heat-singed tips and charred upper gradients.
  - Realistic ember color calibration and dark charcoal blending under flame (suppressed neon saturation).
  - Static APIs: `ModernGrassManager.IgniteAt(...)`, `ModernGrassManager.ScorchAt(...)`, `ModernGrassManager.ExtinguishFire(...)`, `ModernGrassManager.ResetBurnSimulation()`.
- **Synchronized Fire VFX Node Pool**:
  - `ModernGrassFirePool.cs` zero-allocation pooling system tracking active flame wavefront directly from simulation.
  - Included particle prefab `GrassFire_VFX.prefab` with soft puff flame, smoke, and ember sparks.
  - Layer-level custom fire particle prefab support (`layer.fireParticlePrefab`).
- **Comprehensive Documentation & Architectural Guides**:
  - `GPU_GRASS_DEVELOPMENT_ROADMAP.md`: Complete 10-phase architectural guide from procedural buffers to wildfire CA.
  - `README_API_GUIDE.md`: Dedicated external gameplay and combat API integration handbook.
  - Expanded Starter Presets: `BOTW_LushGreen_Preset`, `Lawn_Grass`, `Lee_Grass`, `RedAutumnGrass`, `Small_Grass`, `TallWildGrass`, `Tsushima_Pampas_Grass`.

## [1.1.0] - 2026-09-20

### Added
- **Explosion & Expanding Shockwave System**:
  - `ModernGrassShockwave` component and `ModernGrassManager.TriggerShockwave(...)` static API.
  - Expanding ring wavefront deformation with violent radial crest knockdown on procedural and custom 3D mesh foliage.
  - Blast wake damped harmonic spring recoil physics ($\sim 1.6\text{s}$).
  - Zero-allocation GPU architecture (up to 8 concurrent shockwaves in GPU L1 cache).
- **Dynamic Wind Blast & 360° Continuous Rotor/Aura Wash**:
  - `ModernGrassWindZone` component supporting `Directional` (cone gusts) and `Omnidirectional` (360° downward rotor wash).
  - Continuous and TimedBurst modes with interactive Scene view Gizmos.
  - High-frequency dynamic flutter ripples travelling along the blast vector.
  - `ModernGrassManager.TriggerWindBurst` and `ModernGrassManager.TriggerOmniWindBurst` static APIs.
- **Wind Release Damped Harmonic Spring Oscillation**:
  - When wind zones are disabled or bursts end, grass smoothly transitions into elastic harmonic spring recoil instead of snapping upright.

## [1.0.0] - 2026-09-19

### Added
- **GPU Instanced Blade Rendering**: Compute shader powered procedural blade generation (`ModernGrassBlades.compute`) with indirect drawing (`DrawProceduralIndirect`).
- **Interactive Grass Cutting**:
  - `ModernGrassCutter` component for weapons, projectiles, and tools.
  - Sphere and directional sweep raycast cutting.
  - Blade shred particle pooling (`ModernGrassCutPool`) with auto-recycled cut particles.
- **Dynamic Player Interaction**:
  - `ModernGrassInteractor` for player footsteps and vehicles.
  - Multi-actor push deformation with smooth spring recovery.
- **Spatial Optimization & Culling**:
  - Distance culling with smooth alpha fade (`minFadeDistance`, `maxDrawDistance`).
  - Quadtree frustum culling (`ModernCullingTreeNode`).
  - Distance-based blade segment LOD (`enableSegmentLOD`).
- **Stylized Shading & Artistic Controls**:
  - Two-tone gradient coloring (root to tip).
  - World-space Perlin color tint noise (Ghibli / BOTW meadow patterns).
  - World-space height noise (rolling high/low grass patches).
  - Normal-up blend for smooth, voluminous stylized lighting.
  - Subsurface scattering / translucency simulation.
  - Edge highlighting and shadow receiver density control.
  - Slope-aware upright growth intensity slider (`uprightIntensity`).
- **Editor Tooling**:
  - In-scene painter window (`ModernGrassPainterWindow` under `Tools / Window > Easy Grass Painter > Grass Painter`).
  - Paint, Erase, and Re-grow brush modes with radius, density, and falloff controls.
  - 3D interactive real-time blade preview in inspector.
  - Reusable GrassType preset system with clone & save features.
  - Support for custom 3D foliage meshes and procedural cutout textures.
- **Starter Presets**:
  - `Default_Stylized_Grass.asset`
  - `Meadow_Field_Grass.asset`
