# Easy Grass Painter: Scripting & Gameplay Integration Guide 🌾

This reference guide documents all external gameplay hooks, C# APIs, and components for integrating **Easy Grass Painter** into your gameplay scripts (characters, combat systems, weapons, explosives, vehicles, spells, and environmental systems).

---

## 📑 Table of Contents
1. [Namespace Setup](#-namespace-setup)
2. [🏃 Player & Object Interaction (Walking & Trampling)](#1--player--object-interaction-walking--trampling)
3. [✂️ Grass Cutting & Slashing (Weapons, Tools, Projectiles)](#2-️-grass-cutting--slashing-weapons-tools-projectiles)
4. [💥 Explosions & Expanding Shockwaves](#3--explosions--expanding-shockwaves)
5. [🌪️ Dynamic Wind Zones & Wind Blasts](#4-️-dynamic-wind-zones--wind-blasts)
6. [🗺️ Open-World Chunk Streaming & Player Tracking](#5-️-open-world-chunk-streaming--player-tracking)
7. [🌍 Ground Blending & Runtime Terrain Baker](#6--ground-blending--runtime-terrain-baker)

---

## 📦 Namespace Setup
All runtime components and manager APIs live under the `ModernGrassTool` namespace:

```csharp
using ModernGrassTool;
```

---

## 1. 🏃 Player & Object Interaction (Walking & Trampling)
Makes grass bend smoothly away from moving entities with distance-decoupled damped harmonic oscillation (spring wobble) and persistent walking footprints.

### Method A: Inspector Component
Attach the `ModernGrassInteractor` component to any GameObject (Player, NPC, Mount, Vehicle, Rolling Boulder):

1. Select your character or object.
2. Click **Add Component** > search for **Modern Grass Interactor** (or `ModernGrassInteractor`).
3. Configure the inspector properties:
   - **`Radius`** (default: `1.2m`): Physical interaction bubble reach.
   - **`Strength`** (default: `1.0`): How forcefully grass pushes away.
   - **`Trail Spacing`** (default: `0.15m`): Spacing between footprint breadcrumb samples.

### Method B: Adding via C# Script
```csharp
using UnityEngine;
using ModernGrassTool;

public class CharacterSetup : MonoBehaviour
{
    private void Awake()
    {
        var interactor = gameObject.AddComponent<ModernGrassInteractor>();
        interactor.radius = 1.3f;
        interactor.strength = 1.2f;
    }
}
```

> [!IMPORTANT]
> **Per-Layer Activation**: In the Grass Painter window or Grass Type Inspector, expand **🏃 Player & Object Interaction** and ensure **`Enable Interaction`** is checked for each grass species you want to be trampleable.

---

## 2. ✂️ Grass Cutting & Slashing (Weapons, Tools, Projectiles)
Cuts tall grass blades down to short flat stubble and spawns recycled particle shred bursts from a GPU-instanced object pool.

### Method A: Component (`ModernGrassCutter`)
Attach to sword bones, spinning lawn mower blades, or projectile triggers:

```csharp
// Attach to weapon/projectile GameObject:
var cutter = swordObject.AddComponent<ModernGrassCutter>();
cutter.cutRadius = 1.8f;              // Radius of the cut sphere
cutter.stubbleHeight = 0.15f;         // Height of stubble left behind
cutter.continuousCutting = true;      // Continuously cut along movement path
```

### Method B: Direct C# Static API (Recommended for Combat Systems)
Call directly from hit-boxes, animation events, weapon collision callbacks, or melee combos:

```csharp
using UnityEngine;
using ModernGrassTool;

public class MeleeWeapon : MonoBehaviour
{
    // Call from an Animation Event or OnTriggerEnter
    public void ExecuteSlashAttack(Vector3 attackPosition)
    {
        float cutRadius = 2.2f;
        float stubbleHeight = 0.18f; // Flat cut stump height near ground

        ModernGrassManager.CutGrassAt(attackPosition, cutRadius, stubbleHeight);
    }
}
```

### Per-Layer Cut Immunity
- In the Grass Type Inspector, you can toggle **`Can Be Cut`** on or off.
- If unchecked, that specific species (e.g. tough reeds, decorative bushes, wooden stakes) will be immune to cuts while surrounding grass is cleanly sliced.
- You can also assign a custom **`Cut Particle Prefab`** per layer.

---

## 3. 💥 Explosions & Expanding Shockwaves
Dispatches GPU-accelerated expanding ring wavefronts that violently knock down grass along the blast crest and spring back elastically in the wake.

### Method A: Component (`ModernGrassShockwave`)
Attach to grenade, rocket, or explosion effect prefabs:

- **`Radius`** (default: `10m`): Maximum wavefront expansion reach.
- **`Force`** (default: `2.0`): Flattening knockdown force.
- **`Speed`** (default: `25 m/s`): Velocity of the expanding wavefront ring.
- **`Thickness`** (default: `2.5m`): Ring wave crest thickness.
- **`Trigger On Start`** (default: `true`): Triggers automatically when instantiated.
- **`Trigger On Enable`** (default: `true`): Triggers when pooled objects are re-enabled.

### Method B: C# Static API
Call from bomb detonation scripts, spell impacts, ground slams, or boss landing routines:

```csharp
using UnityEngine;
using ModernGrassTool;

public class ExplosiveBarrel : MonoBehaviour
{
    public void Explode()
    {
        Vector3 blastOrigin = transform.position;

        // 1. (Optional) Cut grass right at the blast epicenter
        ModernGrassManager.CutGrassAt(blastOrigin, radius: 2.5f, stubbleHeight: 0.1f);

        // 2. Trigger expanding shockwave wavefront
        ModernGrassManager.TriggerShockwave(
            position: blastOrigin,
            radius: 12f,          // Total blast distance (meters)
            force: 2.2f,          // Knockdown strength
            speed: 26f,           // Wave expansion speed (m/s)
            thickness: 2.5f       // Crest ring thickness (meters)
        );

        Destroy(gameObject);
    }
}
```

---

## 4. 🌪️ Dynamic Wind Zones & Wind Blasts
High-performance dynamic wind emitters supporting conical directional blasts (fans, air cannons, wind spells) and 360° radial downward wash (helicopters, VTOL jets, charging energy auras).

### Wind Modes Overview
| Mode | Behavior | Best Used For |
| :--- | :--- | :--- |
| **`Directional`** | Conical stream forward along `transform.forward` | Wind spells, fans, jet thrusters, air blasts |
| **`Omnidirectional`** | 360° radial horizontal wash with vertical reach cylinder | Helicopters hovering, player charging aura, ground slam |

### Duration Modes & Spring Recoil
- **`Continuous`**: Blows indefinitely while active.
  - **Damped Harmonic Spring Oscillation**: Disabling the component (`enabled = false`) or turning off its GameObject (`gameObject.SetActive(false)`) **does not snap grass back**. It seamlessly triggers a $1.6\text{s}$ elastic harmonic spring recoil where grass oscillates back and forth and settles naturally!
- **`TimedBurst`**: Blows for `burstDuration` and automatically transitions into the damped spring recoil phase.

### Method A: Component (`ModernGrassWindZone`)
Attach to a Helicopter, Player, Drone, or Fan GameObject:

```csharp
using UnityEngine;
using ModernGrassTool;

public class HelicopterController : MonoBehaviour
{
    private ModernGrassWindZone _rotorWash;

    private void Start()
    {
        // Add downward 360 rotor wash to helicopter
        _rotorWash = gameObject.AddComponent<ModernGrassWindZone>();
        _rotorWash.mode = GrassWindMode.Omnidirectional;
        _rotorWash.durationMode = GrassWindDuration.Continuous;
        _rotorWash.radius = 16f;           // Horizontal spread radius
        _rotorWash.verticalRange = 20f;    // Distance from air to ground
        _rotorWash.force = 2.8f;           // Bending force
        _rotorWash.flutterSpeed = 24f;     // Rapid spinning blade ripples
    }

    private void OnEngineShutdown()
    {
        // Grass will smoothly oscillate and settle when turned off!
        _rotorWash.enabled = false;
    }
}
```

### Method B: C# Static One-Shot Bursts
Trigger instant directional gusts or radial pulses without needing persistent GameObjects:

```csharp
// 1. Directional Cone Wind Blast (Forward along camera or character forward)
Vector3 origin = player.transform.position + Vector3.up;
Vector3 direction = player.transform.forward;

ModernGrassManager.TriggerWindBurst(
    origin: origin,
    direction: direction,
    range: 18f,          // Reach (meters)
    force: 2.8f,         // Bending strength
    duration: 2.0f,      // Active blow duration (seconds)
    coneAngle: 65f,      // Cone spread angle (degrees)
    flutterSpeed: 20f    // Flutter ripple frequency
);

// 2. 360° Omnidirectional Pulse / Radial Wash
ModernGrassManager.TriggerOmniWindBurst(
    origin: player.transform.position,
    radius: 12f,         // Wash radius (meters)
    force: 2.4f,         // Force
    duration: 1.5f,      // Active blow duration
    verticalRange: 10f,  // Vertical cylinder reach
    flutterSpeed: 22f    // Flutter speed
);
```

---

## 5. 🗺️ Open-World Chunk Streaming & Player Tracking
Easy Grass Painter automatically partitions painted grass into $64\text{m} \times 64\text{m}$ spatial grid chunks and streams active chunks around the player.

### Assigning Viewer / Player Reference
By default, the system tracks `Camera.main` or any active `ModernGrassInteractor`. To explicitly assign the primary player Transform:

```csharp
using UnityEngine;
using ModernGrassTool;

public class PlayerSpawner : MonoBehaviour
{
    public void OnPlayerSpawned(Transform playerTransform)
    {
        if (ModernGrassManager.Instance != null)
        {
            ModernGrassManager.Instance.playerReference = playerTransform;
        }
    }
}
```

### Manual Streaming Trigger (e.g. After Fast Travel / Teleportation)
If you teleport the player across the map, force-refresh the chunks instantly:

```csharp
public void TeleportPlayer(Vector3 newWorldPosition)
{
    player.transform.position = newWorldPosition;

    if (ModernGrassManager.Instance != null)
    {
        ModernGrassManager.Instance.UpdateStreaming(newWorldPosition, force: true);
    }
}
```

---

## 6. 🌍 Ground Blending & Runtime Terrain Baker
Ensures grass roots melt seamlessly into terrain ground textures with zero harsh line intersections.

- Handled automatically by `ModernGrassManager.Awake()`.
- If your game alters terrain textures or heights at runtime (e.g. winter snow falling, dynamic procedural terrain sculpting):

```csharp
using ModernGrassTool;

// Re-bake ground texture map
var baker = ModernGrassManager.Instance?.EnsureTerrainBaker();
baker?.SetupAndBake();
```

---

## 💡 Best Practices & Performance Tips
1. **Zero Allocations**: All shockwaves, wind zones, and interactors use GPU constant buffers in L1 cache ($0\text{ B}$ GC allocations per frame).
2. **Layer Interactivity**: For decorative or background grass layers (such as cliff flowers), keep `Enable Interaction` disabled to maximize GPU throughput.
3. **Layer Cutting**: Toggle `Can Be Cut` off for rigid obstacles, tree moss, or shrub layers.
