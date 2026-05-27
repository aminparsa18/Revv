# REVV

<img width="128" height="128" alt="splash" src="https://github.com/user-attachments/assets/ad6c4d5e-1128-4735-bf5b-819e15574a0b" />


**Turn your phone into a wireless steering wheel — no hardware, no cables.**

REVV streams motion data from your phone over LAN to a virtual Xbox 360 controller on your PC. Any racing game that supports a controller works instantly, with no per-game setup.

---

## How it works

Hold your phone in landscape and rotate it like a real steering wheel. The phone's orientation sensor feeds a fused absolute angle into a virtual Xbox 360 controller on the PC — games see it as real hardware and steer accordingly.

```
Phone (MAUI)                              PC (WinForms)
  OrientationSensor (fused quaternion)
  → extract steering angle               UDP    → Virtual Xbox 360 Controller
  → normalize to -1.0 … +1.0         ─────────► → Left Stick X axis → your game
  → broadcast over LAN
```

**Discovery is automatic.** The phone broadcasts on the local network; the PC finds it and ACKs. No IP addresses to type, no pairing screens.

---

## Requirements

### PC
- Windows 10/11
- [ViGEm Bus Driver](https://github.com/nefarius/ViGEmBus/releases) — installs a virtual gamepad bus (one-time setup, archived project but stable)
- .NET 10 Runtime

### Phone
- Android (iOS coming later)
- On the same Wi-Fi network as the PC

---

## Quick Start

1. Install the ViGEm Bus Driver on your PC (link above)
2. Run **Revv.Windows** on your PC — it starts listening immediately
3. Open **REVV** on your phone — it discovers the PC automatically
4. In your game, go to controller settings and bind **Left Stick X** to steering
5. Hold your phone in landscape, rotate, and drive

**Recenter:** double-tap the wheel graphic to set your current hold position as the neutral center.

---

## Tuning

Adjust these in the phone app's settings panel (tap the gear icon):

| Setting | Default | What it does |
|---|---|---|
| Sensitivity | 1.4 | Output multiplier — higher means full lock with less rotation |
| Range | 45° | Physical rotation angle that maps to full lock |
| Expo | 1.2 | Center precision curve — higher gives more control near straight-ahead |
| Center Assist | 0.02 | Strength of the self-centering spring |
| Auto Calibrate | Off | Learns your actual rotation range during play and adjusts Range automatically |

The PC side applies a spring-damper filter before input reaches the game, smoothing out any network jitter.

---

## Roadmap

- [ ] Haptic feedback at full lock
- [ ] Settings persistence between sessions
- [ ] iOS support

---

## Architecture notes

For contributors and developers, see [CLAUDE.md](CLAUDE.md) for the full internal architecture, class-by-class breakdown, and design rationale.

---

## License

MIT
