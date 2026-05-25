# REVV — Project Context for Claude Code

## Codebase Navigation — Use graphify-out as RAG

A pre-built knowledge graph of this repo lives in `graphify-out/`. **Always consult it first** before grepping or exploring source files for architecture, dependency, or "where is X" questions.

### Which file to use

| Question type | File to read |
|---|---|
| "What are the core abstractions?" / "What touches X?" | `graphify-out/COMPASS.md` — token-optimized, start here |
| "Which community/domain owns this?" / god nodes / bridge edges | `graphify-out/GRAPH_REPORT.md` |
| Specific node lookup, edge traversal, programmatic queries | `graphify-out/graph.json` |
| AI-derived architectural insights | `graphify-out/intelligence.json` |
| Refactoring or improvement ideas | `graphify-out/suggestions.json` |

### How to use it

1. Read `graphify-out/COMPASS.md` first for a fast orientation.
2. If you need community membership or cross-cutting connections, read `graphify-out/GRAPH_REPORT.md`.
3. Only fall back to direct `Grep`/`Glob` on source files when the graph doesn't have enough detail.

## What is REVV?

REVV is a cross-platform mobile-to-PC steering wheel simulator. The user holds their phone like a steering wheel; the phone reads gyroscope data and streams it over LAN to a Windows PC app, which feeds it into a virtual gamepad. No hardware, no cables, no per-game configuration.

**Status:** Core pipeline is end-to-end working — phone connects, signals stream, ViGEm gamepad receives steering. In-game binding is handled by the game itself (bind Left Stick X to steer); no additional key-mapping layer needed.

---

## Architecture

```
[Revv — MAUI Android/iOS]                    [Revv.Windows — WinForms]
  Gyroscope (AngularVelocity.Z)                UDP Listener (RevvReceiver)
  → SteeringController                  UDP    → RevvGamepad (ViGEm)
  → Normalize -1.0 … +1.0           ─────────► → Xbox 360 Left Stick X axis
  → RevvBroadcaster → LAN                      → RevvSession (glues it all)
                                               → MainForm (tray icon + UI)
```

**Transport:** UDP — low latency, drop-tolerant, no stale packet queuing  
**Discovery:** UDP broadcast — phone broadcasts `REVV_HERE:<port>` on `255.255.255.255:5554`, PC ACKs, no manual IP entry  
**PC input:** ViGEm (`Nefarius.ViGEm.Client` NuGet) — virtual Xbox 360 controller, analog axis steering  
**Gyro axis:** `AngularVelocity.Z` — phone held portrait, rotated clockwise/counter-clockwise like a real steering wheel

---

## Project Structure

```
/Revv
  Revv.slnx
  /Revv.Shared                  ← net10.0 class library, no MAUI dependency
    /Networking
      RevvPacket.cs
      RevvDiscovery.cs
      RevvBroadcaster.cs        ← phone-side UDP (discovery + streaming)
      RevvReceiver.cs           ← PC-side UDP (discovery ACK + ingestion)
  /Revv                         ← MAUI Android/iOS only
    /Shared
      SteeringController.cs     ← gyro integration (MAUI API, stays here)
    MainPage.xaml / .cs
    MauiProgram.cs
  /Revv.Windows                 ← WinForms net10.0-windows
    Program.cs                  ← entry point
    MainForm.cs                 ← tray icon + steering wheel UI
    RevvSession.cs              ← top-level glue: Receiver → Gamepad
    /Input
      RevvGamepad.cs            ← ViGEm virtual Xbox 360 controller
```

---

## DI Registration (MauiProgram.cs — phone only)

```csharp
builder.Services.AddSingleton<SteeringController>();
builder.Services.AddSingleton<RevvBroadcaster>();
builder.Services.AddTransient<MainPage>();
builder.Services.AddTransient<AppShell>();
```

ViGEm lives only in `Revv.Windows` — it is never referenced by the MAUI project.

---

## Revv.Shared — SteeringController

Integrates gyroscope angular velocity → steering angle → normalized output.

**Key properties (all public, user-configurable):**

| Property | Default | Range | Effect |
|---|---|---|---|
| `Sensitivity` | 1.0 | 0.1 – 3.0 | Multiplier on angular velocity |
| `RangeDegrees` | 45° | 15° – 90° | Physical tilt for full lock |
| `DeadZone` | 0.02 | 0.0 – 0.1 | Noise floor, rad/s |
| `RecenterThreshold` | 0.05 | — | Input below this activates spring |
| `RecenterStrength` | 0.97 | 0.90 – 0.99 | Spring bleed-to-center rate |

**Key methods:**
- `Start(SensorSpeed)` — wires gyroscope, call on page appear
- `Stop()` — unwires gyroscope, call on page disappear
- `Recenter()` — snaps angle to 0, fires `Recentered` event
- `ProcessReading(GyroscopeData)` — can be called manually for testing

**Events:**
- `SteeringChanged` → `float` (-1.0 to +1.0) — fires every gyro reading
- `Recentered` — fires on manual recenter

**Drift mitigation (no Madgwick needed):**
1. Dead zone filters noise when phone is still
2. Passive spring (`*= 0.97f`) pulls toward center when input is idle
3. `RangeDegrees` clamp bounds maximum drift naturally

---

## Revv.Shared — Networking

### RevvPacket
4 bytes, float32 little-endian. `-1.0` = full left, `+1.0` = full right. No overhead.

### RevvDiscovery (static constants)
```
BroadcastPort   = 5554   ← PC listens for beacons here
PhoneListenPort = 5555   ← Phone listens for ACK here
DataPort        = 5556   ← PC listens for steering data here
```

### Handshake sequence
```
Phone → 255.255.255.255:5554   "REVV_HERE:5556"   (every 1s until ACK)
PC   → phone:5555              "REVV_ACK"
Phone begins streaming float32 packets → PC:5556 at 60Hz
```

### RevvBroadcaster (phone side)
- States: `Idle → Discovering → Streaming`
- Auto-reconnects: if streaming fails, re-enters discovery automatically
- `SetSteering(float)` — thread-safe, call from gyro event
- Events: `Connected(ip)`, `Disconnected`, `ErrorOccurred`

### RevvReceiver (PC side)
- States: `Idle → WaitingForPhone → Receiving`
- Stale watchdog: if no packet for `StaleTimeoutMs` (3000ms default), fires `PhoneDisconnected` and re-enters discovery
- Events: `PhoneConnected(ip)`, `PhoneDisconnected`, `SteeringReceived(float)`, `ErrorOccurred`

---

## Revv.Windows — Input

### RevvGamepad
Wraps ViGEm virtual Xbox 360 controller.

**Key properties:**
- `Smoothing` (0.85) — EMA filter on steering, irons out network jitter
- `Deadband` (0.02) — snaps near-zero output to 0, prevents ghost input

**Key methods:**
- `Connect()` — plugs in virtual controller (requires ViGEm Bus Driver installed)
- `Disconnect()` — zeros all inputs, unplugs controller
- `SetSteering(float)` — applies smoothing + deadband, submits report
- `SetThrottle(float)` — right trigger (0.0–1.0), future touch pedals
- `SetBrake(float)` — left trigger (0.0–1.0), future touch pedals
- `ZeroAllInputs()` — call immediately on phone disconnect

**Prerequisite:** ViGEm Bus Driver must be installed on the PC.
Download: https://github.com/nefarius/ViGEmBus/releases

### RevvSession
Top-level glue class. The only thing the PC UI touches.

```csharp
var session = new RevvSession();
session.PhoneConnected += (_, ip) => ...;
session.PhoneDisconnected += (_, _) => ...;
session.SteeringUpdated += (_, value) => ...;  // for debug needle in UI
await session.StartAsync();
```

Wires: `RevvReceiver.SteeringReceived → RevvGamepad.SetSteering`  
On disconnect: calls `ZeroAllInputs()` automatically — no stuck steering in-game.

---

## Revv.Phone — MainPage

Dark motorsport aesthetic. Black background, red accents, monospaced readouts.

**Layout:**
```
Row 0 — Status bar      "REVV" wordmark | connection pill | ⚙ settings toggle
Row 1 — Steering area   Wheel graphic (pure XAML shapes) + angle readout + gesture layer
Row 2 — Settings panel  Collapsed by default; sensitivity slider, range slider, recenter button
```

**Behaviours:**
- Wheel graphic rotates via `WheelRoot.Rotation = normalizedValue * RangeDegrees`
- Angle label turns red when `abs(value) > 0.85` (near full lock warning)
- Double-tap anywhere on wheel area → `_steering.Recenter()`
- Recenter fires a scale-pulse animation on the wheel hub
- `DeviceDisplay.KeepScreenOn = true` while page is active
- Settings panel collapses automatically after recenter button is pressed

**Colors:**
```
Background  #0A0A0A
Surface     #141414
SurfaceHigh #1F1F1F
AccentRed   #E8001D
TextPrimary #F0F0F0
TextMuted   #555555
ConnectedGreen #00E87A
```

**Pending:** `icon_settings.png` needed in `Resources/Images/` — 32×32 white gear icon,
or replace `ImageButton` with `Text="⚙"` temporarily.

---

## Key Decisions & Rationale

- **UDP over TCP:** Real-time input — a dropped frame is better than a delayed one
- **ViGEm over key mapping:** Analog steering axis; works in every racing game without per-game config. Users bind Left Stick X in-game — no SendInput/keyboard simulation layer needed
- **Integration + clamp over Madgwick filter:** Simpler; Range setting bounds drift; passive spring handles the rest
- **MAUI for both apps:** Single codebase; Windows app is intentionally lean
- **UDP broadcast discovery:** Zero config, no IP typing, LAN-only
- **`#if WINDOWS` guards in MauiProgram.cs:** ViGEm must never be referenced on mobile targets
- **RevvSession as the single PC entry point:** UI only talks to RevvSession, never directly to Receiver or Gamepad

---

## What's NOT built yet

- [ ] Touch overlay for throttle / brake (on-screen pedals) — `SetThrottle` / `SetBrake` are ready on RevvGamepad, just need UI
- [ ] Multiple steering profiles (save/load sensitivity + range presets)
- [ ] Packet loss / latency debug overlay on phone
- [ ] iOS support (Android first)
- [ ] Haptic feedback on phone when near full lock
- [ ] Landscape hold mode (`AngularVelocity.X` axis)
- [ ] Settings persistence (save sliders between sessions via `Preferences`)
- [ ] PC tray icon (minimize to tray, show connection status)
- [ ] `icon_settings.png` in `Resources/Images/` (32×32 white gear) — currently using `Text="⚙"` as placeholder

---

## Naming & Branding

**REVV** — always all caps. Red on black. Aggressive, minimal, motorsport.