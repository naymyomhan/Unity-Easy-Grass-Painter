# Modern Grass Tool — External Gameplay API & Integration Guide

This guide documents how outside gameplay scripts, weapons, player abilities, and environment systems interact with the Modern Grass Tool without tight coupling.

---

## 1. Grass Fire, Burning & Charring System (မီးလောင်ခြင်းနှင့် ပြာဖြစ်ကျွမ်းသွားခြင်း စနစ်)

### Overview
- Grass catches fire on GPU via a high-performance 2D burn map compute simulation.
- Burning grass blazes with hot glowing embers (`flame > 0`), consumes fuel, and shrinks blade height down into charred black ash stubble (`scorch = 1.0`).
- Adjacent flammable grass patches ignite organically based on distance and wind direction.
- Non-flammable grass layers (or gaps larger than `fireMaxSpreadGap`) naturally stop fire propagation.

### C# API
```csharp
using ModernGrassTool;

// 1. Ignite grass at a world-space location (e.g. from Torch, Molotov, Fireball)
ModernGrassManager.IgniteAt(Vector3 worldPosition, float radius = 1.8f);

// 2. Scorch/Char grass instantly into black ash without spreading fire (e.g. Bomb Blast, Meteor Epicenter)
ModernGrassManager.ScorchAt(Vector3 worldPosition, float radius = 2.5f, float intensity = 1.0f);
```

### External Contact Timer (Torch Example)
To implement a mechanic where a torch or flame only catches fire after staying in contact for $x$ seconds:
```csharp
// Outside gameplay script (e.g. ThirdPersonPlayerController or TorchWeapon.cs)
if (isTorchActive)
{
    torchContactTimer += Time.deltaTime;
    if (torchContactTimer >= ignitionDelaySeconds) // e.g. 0.75s
    {
        ModernGrassManager.IgniteAt(torchTip.position, 1.8f);
        torchContactTimer = ignitionDelaySeconds - 0.35f; // continuous burn pulse
    }
}
else
{
    torchContactTimer = 0f;
}
```

### Per-Grass-Type Settings (in Inspector)
- **Can Catch Fire**: Toggle whether this species can catch fire.
- **Burn Duration (s)**: Seconds before burning grass completely burns down into ash (e.g. 3.5s).
- **Spread Radius (m)**: Propagation radius to neighboring blades (e.g. 1.5m).
- **Max Spread Gap (m)**: Maximum distance gap between grass blades beyond which fire will not jump.
- **Charred Ash Color**: The HDR color of the charred ash stubble (defaults to deep charcoal black).

---

## 2. Explosions, Shockwaves & Ground Scorching (ဗုံးပေါက်ကွဲခြင်းနှင့် Shockwave စနစ်)

### Overview
Combines radial cutting/shaving, instant ground charring, and harmonic spring shockwave propagation.

### C# API
```csharp
using ModernGrassTool;

public void OnBombDetonated(Vector3 explosionPos)
{
    // 1. Cut/shave grass blades at the blast epicenter into short stumps
    ModernGrassManager.CutGrassAt(explosionPos, radius: 2.0f, stubbleHeight: 0.12f);

    // 2. Scorch the blast crater into blackened charred ash
    ModernGrassManager.ScorchAt(explosionPos, radius: 2.6f, intensity: 1.0f);

    // 3. Trigger harmonic spring physical shockwave ripple
    ModernGrassManager.TriggerShockwave(
        origin: explosionPos, 
        radius: 10.0f,     // shockwave travel distance
        force: 2.0f,       // bend force intensity
        speed: 22.0f,      // wave propagation speed (m/s)
        thickness: 2.5f    // wave crest width
    );
}
```

---

## 3. Directional Wind Gusts & 360 Aura Wash (လေပြင်းတိုက်ခတ်ခြင်း စနစ်)

### Point-to-Direction Wind Gust (ဦးတည်ရာတစ်ခုသို့ လေတိုက်ခတ်ခြင်း)
```csharp
// Fires a forward wind blast (e.g. Magic spell, Dragon roar, Shotgun blast)
ModernGrassManager.TriggerWindBurst(
    origin: transform.position,
    direction: transform.forward,
    range: 18.0f,
    force: 2.8f,
    duration: 2.0f,
    coneAngle: 65.0f,
    flutterSpeed: 20.0f
);
```

### 360 Continuous Wash with Harmonic Recoil (ဥပမာ - Helicopter Rotor Wash, Player Power Charge)
Attach or spawn `ModernGrassWindZone`:
```csharp
var windZone = gameObject.AddComponent<ModernGrassWindZone>();
windZone.mode = GrassWindMode.Omnidirectional; // 360 radial wash
windZone.durationMode = GrassWindDuration.Continuous;
windZone.radius = 12.0f;
windZone.force = 2.2f;
windZone.flutterSpeed = 22.0f;

// When the helicopter shuts off or power charge stops:
// Simply disable the GameObject or component. 
// The grass will NOT snap back abruptly — it automatically executes a smooth Harmonic Spring Oscillation recoil!
windZone.gameObject.SetActive(false);
```

---

## 4. Grass Cutting & Regrowth (မြက်ပင်ခုတ်ပိုင်းခြင်းနှင့် ပြန်ပေါက်ခြင်း စနစ်)

### Cutting API
```csharp
// Slash cut grass in front of weapon
ModernGrassManager.CutGrassAt(
    center: swordHitPoint, 
    radius: 2.4f, 
    stubbleHeight: 0.18f // flat stump height left near ground
);
```

### Regrowth Modes (`ModernGrassManager.regrowthMode`)
- **Timer**: Automatically regrows cut grass after `regrowDelaySeconds` (e.g. 8s).
- **Distance**: Regrows when player moves beyond `regrowDistance` (e.g. 45m).
- **ManualOnly**: Never auto-regrows; waiting for code to call `ModernGrassManager.Instance.RestoreAllCuts()`.
- **None**: Permanent cuts (for farming/lawnmower games).

---

## 5. Player & Character Interaction (လူ/သတ္တဝါ ဖြတ်လျှောက်တိုးဝှေ့ခြင်း)

Simply attach `ModernGrassInteractor` to any player, NPC, vehicle, or animal GameObject:
```csharp
// ModernGrassInteractor handles:
// 1. Direct physical push aside
// 2. Velocity-based bending in walk direction
// 3. Elastic Spring Oscillation (grass wobbles and shakes as it snaps back)
```
In Grass Layer / Grass Type inspector:
- **Enable Interaction**: Toggle interaction for this species.
- **Spring Wobble**: Set oscillation amplitude ($0$ to $10$).

---

## 6. Open-World Spatial Chunk Streaming (ကမ္ဘာပတ် ချန့်ခ်စနစ်)

In `ModernGrassManager`:
- **Enable Chunk Streaming**: `true`
- **Chunk Size**: `64` meters
- **Stream Radius**: `120` meters

Automatically unloads far grass chunks and streams in active ones around `playerReference` or `Camera.main` to keep VRAM constant regardless of world size.
