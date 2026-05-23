# REVV

**Turn your phone into a wireless steering wheel — no hardware, no cables.**

REVV streams gyroscope data from your phone over LAN to a virtual Xbox 360 controller on your PC. Any racing game that supports a controller works instantly, with no per-game setup.

---

## How it works

Hold your phone portrait, tilt it sideways like a real steering wheel. The gyroscope feeds into a virtual Xbox 360 controller on the PC — games see it as real hardware and steer accordingly.

```
Phone (MAUI)                         PC (WinForms)
  Gyroscope (Y-axis)
  → normalize to -1.0 … +1.0   UDP   → Virtual Xbox 360 Controller
  → broadcast over LAN      ────────► → Left Stick X axis → your game
```

**Discovery is automatic.** The phone broadcasts on the local network; the PC finds it and ACKs. No IP addresses to type, no pairing screens.

---

## Requirements

### PC
- Windows 10/11
- [ViGEm Bus Driver](https://github.com/nefarius/ViGEmBus/releases) — installs a virtual gamepad bus (one-time setup)
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
5. Tilt and drive

**Recenter:** double-tap the wheel graphic to snap back to zero if drift accumulates.

---

## Tuning

Adjust these in the phone app's settings panel (swipe/tap the gear icon):

| Setting | Default | What it does |
|---|---|---|
| Sensitivity | 1.0 | How aggressively the gyro maps to steering angle |
| Range | 45° | Physical tilt required for full lock |

The PC side applies a smoothing filter (EMA) to iron out network jitter before the input reaches the game.

---

## Roadmap

- [ ] Touch pedals (throttle / brake overlay)
- [ ] Multiple steering profiles
- [ ] Haptic feedback at full lock
- [ ] Landscape hold mode
- [ ] Settings persistence between sessions
- [ ] iOS support
- [ ] Packet loss / latency debug overlay

---

## Architecture notes

For contributors and developers, see [CLAUDE.md](CLAUDE.md) for the full internal architecture, class-by-class breakdown, and design rationale.

---

## License

MIT
