# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
