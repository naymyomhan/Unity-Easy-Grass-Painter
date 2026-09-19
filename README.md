# Easy Grass Painter for Unity URP 🌾

A high-performance, GPU-driven procedural stylized grass and foliage system designed for **Unity Universal Render Pipeline (URP)**.

Inspired by the visual aesthetic of *The Legend of Zelda: Breath of the Wild* and Studio Ghibli, **Easy Grass Painter** leverages compute shaders and GPU indirect drawing (`DrawProceduralIndirect`) to render hundreds of thousands of grass blades at silky-smooth framerates with real-time cutting, player interaction, and artist-friendly in-editor painting tools.

---

## ✨ Features

- **🚀 GPU-Driven Procedural Generation**:
  - Procedural blade mesh synthesis generated entirely on the GPU via compute shaders.
  - Zero CPU overhead for blade transforms or vertex generation.
  - Millions of blades rendered effortlessly using `Graphics.DrawProceduralIndirect`.
- **✂️ Real-Time Grass Cutting**:
  - Cut grass dynamically with weapons, tools, lawnmowers, or projectiles using `ModernGrassCutter`.
  - Auto-pooled cut blade particle VFX (`ModernGrassCutPool`) that spawn grass shreds flying in the air.
  - Paintable in-editor "Re-grow" brush to restore cut grass patches.
- **🏃 Dynamic Character & Object Interaction**:
  - Push and flatten grass as characters, animals, or vehicles move through fields using `ModernGrassInteractor`.
  - Smooth spring-based recovery back to upright position.
- **🎨 Stylized Art Direction & Shading**:
  - Root-to-tip two-tone gradient coloring.
  - World-space Perlin color noise for sweeping field color variations.
  - World-space height noise to create natural meadow rolling mounds.
  - Normal-up blending for voluminous, cartoon-style foliage illumination.
  - Subsurface scattering (translucency) & rim lighting.
  - Slope-aware **Upright Intensity** slider (blends between ground-normal aligned and sky-facing vertical growth).
- **📉 Performance & LOD**:
  - Hierarchical spatial chunking & quadtree frustum culling.
  - Distance-based alpha fade and max draw distance culling.
  - Segment LOD: dynamically reduces vertex counts for distant blades.
- **🖌️ In-Editor Scene Painter**:
  - Full-featured paint tool window (`Tools > Easy Grass Painter > Grass Painter` or `Window > Easy Grass Painter > Grass Painter`).
  - Paint, Erase, and Re-grow brushes with adjustable radius, density, and falloff.
  - Raycasts against Unity Terrains and Mesh Colliders.
  - Live 3D blade preview directly inside the Inspector window.
- **📦 Multi-Type & Preset Architecture**:
  - Layer-based multi-species grass painting (mix flowers, wild weeds, tall meadow grass, and lawn grass).
  - ScriptableObject presets (`ModernGrassType`) that can be cloned and shared across scenes and projects.
  - Support for Custom 3D Foliage Meshes and Procedural Cutout Alpha Textures.

---

## 📋 Requirements

- **Unity**: `2022.3 LTS` or newer
- **Render Pipeline**: **Universal Render Pipeline (URP)** `14.0.0` or higher
- **Graphics API**: Direct3D 11 / 12, Metal, Vulkan (Compute Shader support required)

---

## 📥 Installation

### Method 1: Install via Unity Package Manager (Git URL)

1. In the Unity Editor, open the **Package Manager** (`Window > Package Manager`).
2. Click the **`+`** icon in the top-left corner and select **"Add package from git URL..."**.
3. Enter the repository URL:
   ```text
   https://github.com/naymyomhan/Unity-Easy-Grass-Painter.git
   ```
4. Click **Add**. Unity will automatically download, compile, and configure the package.

### Method 2: Manual / Submodule Installation

Clone or extract this repository into your project's `Packages` folder:
```bash
cd YourUnityProject/Packages
git clone https://github.com/naymyomhan/Unity-Easy-Grass-Painter.git com.naymyomhan.easygrasspainter
```

---

## 🚀 Quick Start Guide

### 1. Add ModernGrassManager to your Scene
1. Create an empty GameObject in your scene (e.g., name it `GrassManager`).
2. Add the **`ModernGrassManager`** component to it.
3. Shaders and compute kernels are automatically resolved.

### 2. Open the Grass Painter Window
1. Open the painter window via menu:
   ```text
   Tools > Easy Grass Painter > Grass Painter
   ```
   *(or `Window > Easy Grass Painter > Grass Painter`)*
2. Click **"+ Add New Grass Layer"** to create a grass layer.
3. Assign one of the included starter presets from `Presets/`:
   - `Default_Stylized_Grass.asset`
   - `Meadow_Field_Grass.asset`

### 3. Paint Grass in the Scene
1. Toggle **"Enable Paint Mode"** in the Painter window.
2. Adjust **Brush Radius** and **Brush Density**.
3. Hold **Left Click and Drag** on your terrain or collider to paint grass.
4. Hold **Shift + Left Click** to erase grass.

### 4. Setup Player Interaction
To make grass bend when the player walks through it:
1. Select your Player GameObject.
2. Add the **`ModernGrassInteractor`** component.
3. Configure the **Radius** (e.g., `0.7`) and **Strength** (e.g., `1.2`).

### 5. Setup Grass Cutting (Weapons & Lawnmowers)
To allow weapons or swords to slash grass:
1. Select your weapon GameObject or blade tip.
2. Add the **`ModernGrassCutter`** component.
3. Set **Cut Radius** (e.g., `0.8`), **Sweep Radius**, and **Cut Mode** (`Sphere` or `DirectionalSweep`).
4. Activate the cutter during attack animations by setting `cutter.isCutting = true;`.

---

## 📁 Package Structure

```text
Unity-Easy-Grass-Painter/
├── package.json                         # UPM Package manifest
├── README.md                            # Documentation & guide
├── CHANGELOG.md                         # Version history
├── LICENSE.md                           # MIT License
├── Runtime/
│   ├── EasyGrassPainter.Runtime.asmdef  # Runtime Assembly Definition
│   ├── ModernGrassManager.cs            # Master GPU instancing & chunk manager
│   ├── GrassLayer.cs                    # Layer data & buffer management
│   ├── GrassPoint.cs                    # GPU point layout struct (stride 48)
│   ├── ModernGrassChunk.cs              # Spatial partitioning chunk
│   ├── ModernGrassCutter.cs             # Real-time cutting component
│   ├── ModernGrassCutPool.cs            # Auto-pooled cut particles
│   ├── ModernGrassInteractor.cs         # Character interaction pusher
│   ├── ModernGrassType.cs               # Grass blade preset ScriptableObject
│   ├── ModernGrassPreset.cs             # Preset data carrier
│   ├── ModernCullingTreeNode.cs         # Quadtree culling node
│   ├── ModernRenderTerrainMap.cs        # Terrain heightmap sampler
│   ├── ModernGrassBlades.compute        # Compute shader for procedural blade generation
│   ├── ModernGrassShader.shader         # Procedural stylized URP blade shader
│   └── ModernFoliageMeshShader.shader   # Custom 3D mesh mode shader
├── Editor/
│   ├── EasyGrassPainter.Editor.asmdef   # Editor Assembly Definition
│   ├── ModernGrassPainterWindow.cs      # Interactive scene painting window
│   ├── ModernGrassTypeEditor.cs         # Inspector with live 3D blade preview
│   ├── ModernGrassManagerEditor.cs      # ModernGrassManager custom inspector
│   ├── ModernGrassUI.cs                 # UI styling & foldout helpers
│   └── GrassBladePreviewHelper.cs       # Real-time preview render utility
├── Presets/
│   ├── Default_Stylized_Grass.asset     # Balanced starter preset
│   └── Meadow_Field_Grass.asset         # Taller wild meadow preset
└── Prefabs/
    ├── GrassCutParticle_Material.mat    # Particle material
    ├── GrassCutParticle_StylizedBlades.prefab # Cut blade shred prefab
    └── GrassBladeShred.png              # Cut particle sprite
```

---

## 🛠️ Performance Tips

- **Segment LOD**: Keep `enableSegmentLOD` enabled to automatically drop blade segments down to 1 when far away.
- **Clumping**: Use `bladesPerClump` (e.g., 4–6) rather than individual grass points. This allows dense lawns with fewer point evaluations.
- **Draw Distance**: Set `maxDrawDistance` to `80–120m` with a smooth `minFadeDistance` of `50–70m` for optimal balance of fidelity and performance.
- **Shadows**: Disable `castShadows` on dense field grass layers and rely on the shader's internal ambient occlusion and root shading for a clean stylized aesthetic.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE.md). Feel free to use it in commercial and non-commercial projects!
