# Easy Grass Painter 🌾

A high-performance, GPU-driven procedural stylized grass system for **Unity Universal Render Pipeline (URP)**.

![Easy Grass Painter Demo](Documentation~/grass-painter-example.gif)

## ✨ What You Can Do

- 🖌️ **In-Editor Scene Painting**: Paint, erase, and sculpt multi-species grass layers directly on terrains and meshes with customizable slope limits.
- ⚡ **GPU-Driven Performance**: Render hundreds of thousands of procedural grass blades at silky-smooth framerates using compute shaders and indirect instancing.
- ✂️ **Real-Time Cutting & Regrowth**: Cut grass dynamically with weapons or tools with flying particle shred VFX and regrowth brush support.
- 🔥 **Wildfire & Cellular Automaton Burning**: 2D GPU simulation of spreading fire, wind propagation, glowing charcoal embers, heat-singed perimeter grass, and synchronized Fire VFX.
- 💥 **Explosions & Shockwaves**: Radial expanding wavefronts that violently flatten grass at the crest with damped harmonic spring recoil in the wake.
- 🌪️ **Wind Zones & Rotor Wash**: Conical directional wind blasts and 360° continuous downward wash for helicopters, fans, and aura charging with smooth release recoil.
- 🏃 **Dynamic Player Interaction**: Grass bends and pushes away realistically as characters, animals, and objects move through the field.
- 🎨 **Deep Visual Customization**: Full artist control over wind waves, two-tone color gradients, blade curvature, clumping, and distance LOD culling.

![Easy Grass Painter Editor](Documentation~/editor-preview.png)

## 📥 Installation (Unity Package Manager)

1. In Unity, open **Package Manager** (`Window > Package Manager`).
2. Click **`+`** > **Add package from git URL...**
3. Paste the following URL:
```text
https://github.com/naymyomhan/Unity-Easy-Grass-Painter.git
```

## 🎮 Gameplay & Scripting Integration

Easy Grass Painter includes high-performance gameplay components and C# static APIs for seamless integration into combat, abilities, characters, and environmental mechanics:

- **🏃 Player & NPC Interaction**: Add `ModernGrassInteractor` to any moving entity.
- **✂️ Grass Cutting & Slashing**: Call `ModernGrassManager.CutGrassAt(...)` or add `ModernGrassCutter`.
- **🔥 Wildfire & Ground Scorching**: Call `ModernGrassManager.IgniteAt(...)` or `ModernGrassManager.ScorchAt(...)`.
- **💥 Explosions & Shockwaves**: Call `ModernGrassManager.TriggerShockwave(...)` or add `ModernGrassShockwave`.
- **🌪️ Wind Blasts & Rotor Wash**: Add `ModernGrassWindZone` (Directional / 360° Omnidirectional) or call `ModernGrassManager.TriggerWindBurst(...)`.

👉 **[Read the Full Scripting & Gameplay Integration Guide](INTEGRATION_GUIDE.md)** and **[External API Reference](README_API_GUIDE.md)** for copy-paste C# examples and API references.
👉 **[Read the 10-Phase Architectural Roadmap](GPU_GRASS_DEVELOPMENT_ROADMAP.md)** to learn how the entire GPU system was built from scratch.

## 📄 License

MIT License. Free to use in commercial and personal projects.
