# BaseDome

**Oxide/uMod Plugin for Rust** — Visualizes the building privilege area of your base as a transparent sphere/dome.

![Version](https://img.shields.io/badge/version-1.0.2-blue?style=flat-square)
![Rust](https://img.shields.io/badge/game-Rust-orange?style=flat-square)
![Oxide](https://img.shields.io/badge/framework-Oxide%2FuMod-green?style=flat-square)
[![Discord](https://img.shields.io/badge/Discord-GameZoneOne-5865F2?style=flat-square&logo=discord&logoColor=white)](https://discord.gg/dx2q8wNM9U)

---

## Features

- Draws a **transparent sphere** around your base showing the exact building privilege radius
- Accounts for the fact that TC privilege extends **beyond the visible block shell** (~16 m)
- Configurable **padding, color, refresh interval** and minimum sphere size
- Auto-grants to the default group on load (configurable)
- Cleans up automatically on disconnect or unload

## Installation

1. Copy `BaseDome.cs` into your `oxide/plugins/` folder
2. The plugin will compile and load automatically
3. Permission `basedome.use` is auto-granted to the `default` group by default

## Permissions

| Permission | Description |
|---|---|
| `basedome.use` | Allows activating the dome visualization |

## Configuration

```json
{
  "AutoGrantDefaultGroup": true,
  "PaddingMeters": 2.0,
  "PrivilegeBeyondBlocksMeters": 16.0,
  "MinimumSphereRadiusMeters": 18.0,
  "DrawIntervalSeconds": 0.25,
  "DdrawDurationSeconds": 0.4,
  "FallbackRadiusMeters": 16.0,
  "SphereColorRgba": [0.35, 0.85, 0.45, 0.85]
}
```

| Field | Default | Description |
|---|---|---|
| `AutoGrantDefaultGroup` | `true` | Auto-grant permission to the default group |
| `PaddingMeters` | `2.0` | Extra padding beyond calculated radius |
| `PrivilegeBeyondBlocksMeters` | `16.0` | How far TC privilege extends past blocks |
| `MinimumSphereRadiusMeters` | `18.0` | Minimum sphere size |
| `SphereColorRgba` | green | RGBA color of the sphere |

## Author

Made by **[GameZoneOne](https://discord.gg/dx2q8wNM9U)**  
📧 info@gamezoneone.de
