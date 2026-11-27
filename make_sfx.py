"""Mixes the four tier soundtracks (flush-light/medium/heavy/clogged.wav) from the real Mixkit recordings in sfx/.

Each sound sits on a cue frame of the overlay animation (src/Scenes/Toilet.cs, Overlay.Run); the clock starts when the overlay
shows and the tier's wav is played at that same moment. Change a cue in src/Scenes/Toilet.cs -> change it here.
Level: each tier peaks at 10% full scale or lower (Nader 2026-11-23: "10%, smooth, not loud, just sfx").
Needs ffmpeg on PATH (decode only). Stdlib otherwise.
"""
import array, subprocess, wave
from pathlib import Path

HERE = Path(__file__).parent
SR = 44100
SFX = {p.name.split("_")[0]: p for p in (HERE / "sfx").glob("mk-*.wav")}


def load(key, start=0.0, end=None, fade_in=0.004, fade_out=0.02):
    """Decode a trim to mono float samples, with short fades so no trim edge clicks."""
    args = ["ffmpeg", "-v", "error", "-ss", str(start), "-i", str(SFX[key])]
    if end is not None:
        args += ["-t", str(end - start)]
    raw = subprocess.run(args + ["-ac", "1", "-ar", str(SR), "-f", "s16le", "-"], capture_output=True, check=True).stdout
    x = [v / 32768 for v in array.array("h", raw)]
    fi, fo = int(fade_in * SR), int(fade_out * SR)
    for i in range(min(fi, len(x))):
        x[i] *= i / fi
    for i in range(min(fo, len(x))):
        x[-1 - i] *= (i / fo) ** 2
    return x


def db(g):
    return 10 ** (g / 20)


SPEED = 0.6   # must equal Overlay.Speed in src/Scenes/ToiletParts.cs: the animation runs at 60% of its designed timing


def mix(name, events, length_s, peak):
    """events: (designed_cue_ms, real_offset_ms, samples, gain_db). A cue is scaled by SPEED (it is an animation time);
    the offset is not (it is where the sound's own attack sits inside the recording). Writes flush-<name>.wav at `peak`."""
    out = [0.0] * int(length_s * SR)
    for cue, off, x, g in events:
        s, k = int((cue * SPEED + off) * SR / 1000), db(g)
        for i, v in enumerate(x):
            if 0 <= s + i < len(out):
                out[s + i] += v * k
    top = max(abs(v) for v in out) or 1
    data = array.array("h", (int(v / top * peak * 32767) for v in out))
    with wave.open(str(HERE / f"flush-{name}.wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(data.tobytes())
    print(f"flush-{name}.wav  {length_s:.1f}s  peak {peak:.2f}")


# onsets measured with silencedetect (-40 dB): button 0.108 s, toilet-flush/hard 0.10 s, squish 0.108 s, splash peak ~0.35 s
click = load("mk-1830", 0.0, 0.40, fade_out=0.15)
press = lambda cue: (cue + 140, -108)            # contact = cue + 140 ms of animation (wind-up 70, squash 200)
squish = load("mk-1883", 0.05, 0.36, fade_out=0.06)
splash = load("mk-1311", 0.0, 1.0, fade_out=0.35)
bub1, bub2 = load("mk-1317", 0.0, 0.25, fade_out=0.08), load("mk-3000", 0.0, 0.42, fade_out=0.15)

mix("light", [
    (*press(420), click, -6),
    (600, -100, load("mk-1871", 0.0, 1.5, fade_in=0.1, fade_out=0.6), -4),
], 2.2, 0.08)

mix("medium", [
    (*press(450), click, -5),
    (620, 0, load("mk-1889", 0.0, 2.2, fade_in=0.06, fade_out=0.8), 0),
], 2.8, 0.10)

mix("heavy", [
    (*press(450), click, -5),
    (*press(760), click, -3),
    (950, -100, load("mk-1872", 0.0, 2.5, fade_in=0.02, fade_out=0.8), 0),
    (3250, -350, splash, -8),
], 3.2, 0.10)

mix("clogged", [
    *[(t, 0, b, -10) for t, b in zip((300, 850, 1350, 1950, 2500, 3050, 3600, 4050), (bub1, bub2) * 4)],  # bubbling, water rising
    (*press(1400), click, -5),
    (1640, 0, bub2, -4),                                                 # the failed flush: one glug
    *[(t + 240, -108, squish, -2) for t in (2700, 3250, 3800)],          # plunger contacts
    (4400, -350, splash, -3),                                            # the clog bursts free
    (4400, -100, load("mk-1872", 0.0, 2.2, fade_in=0.02, fade_out=0.8), 0),
], 4.8, 0.10)