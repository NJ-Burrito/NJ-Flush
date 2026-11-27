"""Mixes the twelve 1.3 scene soundtracks (scene-cpu-0..3, scene-gpu-0..3, scene-all-0..3.wav) from the real Mixkit
recordings in sfx/ (ids listed in sfx/CREDITS.md; the raw wavs are not committed, see .gitignore).

The cue sheet is design/1.3-scenes.md. Every time in it is REAL milliseconds from the moment the scene is first on
screen (no SPEED factor, unlike make_sfx.py), and the scene's wav is played from that same moment. Each wav is as long
as the variant plus a 300 ms tail. Change a cue in the sheet / src/Scenes/*.cs -> change it here.
Placement: each recording is trimmed to its first audible sample (3% of its peak), so the sound STARTS on the cue.
Level: each track peaks at 10% full scale or lower (Nader 2026-11-23: "10%, smooth, not loud, just sfx").
Beds (fan hum, shower, steam hiss) sit about 6 dB under the one-shots (clicks, squeaks, chimes).
Needs ffmpeg on PATH (decode / tempo only). Stdlib otherwise.
"""
import array, math, subprocess, wave
from pathlib import Path

HERE = Path(__file__).parent
SR = 44100
PEAK = 0.10
SFX = {p.name.split("_")[0]: p for p in (HERE / "sfx").glob("mk-*.wav")}
TAIL_MS = 300


def db(g):
    return 10 ** (g / 20)


def load(key, start=0.0, end=None, fade_in=0.004, fade_out=0.03, stretch=None, rms=None):
    """Decode a trim to mono floats with short fades. Leading silence is cut at 3% of peak so the sound starts at
    sample 0. Peak-normalised to 1.0, or to `rms` dBFS (for beds). `stretch` changes length by that factor (ffmpeg atempo)."""
    args = ["ffmpeg", "-v", "error", "-ss", str(start), "-i", str(SFX[key])]
    if end is not None:
        args += ["-t", str(end - start)]
    if stretch:
        args += ["-af", f"atempo={1 / stretch:.4f}"]
    raw = subprocess.run(args + ["-ac", "1", "-ar", str(SR), "-f", "s16le", "-"], capture_output=True, check=True).stdout
    x = [v / 32768 for v in array.array("h", raw)]
    top = max(abs(v) for v in x) or 1
    on = next((i for i, v in enumerate(x) if abs(v) > 0.03 * top), 0)
    x = x[on:]
    if rms is None:
        x = [v / top for v in x]
    else:
        r = math.sqrt(sum(v * v for v in x) / len(x)) or 1
        x = [v / r * db(rms) for v in x]
    fi, fo = int(fade_in * SR), int(fade_out * SR)
    for i in range(min(fi, len(x))):
        x[i] *= i / fi
    for i in range(min(fo, len(x))):
        x[-1 - i] *= (i / fo) ** 2
    return x


def ramp(x, pts):
    """Piecewise-linear dB gain over time: pts = [(seconds, dB), ...] (held before the first and after the last)."""
    out = []
    for i, v in enumerate(x):
        t = i / SR
        if t <= pts[0][0]:
            g = pts[0][1]
        elif t >= pts[-1][0]:
            g = pts[-1][1]
        else:
            for (t0, g0), (t1, g1) in zip(pts, pts[1:]):
                if t0 <= t <= t1:
                    g = g0 + (g1 - g0) * (t - t0) / (t1 - t0)
                    break
        out.append(v * db(g))
    return out


def tile(x, seconds, xf=0.2):
    """Loop a short recording to `seconds` with equal-power crossfades (a steady hiss bed from a 1 s recording)."""
    n, k, out = int(seconds * SR), int(xf * SR), list(x)
    while len(out) < n:
        for i in range(k):
            a = i / k
            out[len(out) - k + i] = out[len(out) - k + i] * math.cos(a * math.pi / 2) + x[i] * math.sin(a * math.pi / 2)
        out += x[k:]
    return out[:n]


AUDIBLE = 0.01     # -40 dBFS in the finished track: the level at which a sound counts as having started
MAX_ADVANCE_MS = 250


def render(events, length, advance):
    out = [0.0] * length
    for (cue, x, g), adv in zip(events, advance):
        s, k = int((cue - adv) * SR / 1000), db(g)
        for i, v in enumerate(x):
            if 0 <= s + i < length:
                out[s + i] += v * k
    return out


def advances(events, sc):
    """Per event: ms to start early so that its audible start (first sample at -40 dBFS in the track, which is what
    ffmpeg silencedetect -40dB reports) meets the cue."""
    adv = []
    for cue, x, g in events:
        k = next((i for i, v in enumerate(x) if abs(v) * db(g) * sc >= AUDIBLE), None)
        adv.append(0 if k is None else min(k * 1000 / SR, MAX_ADVANCE_MS))
    return adv


def plan(events, length):
    """Advances and the final scale; iterated because advancing an event changes the track peak and so the scale."""
    adv = [0] * len(events)
    for _ in range(4):
        sc = PEAK / (max(abs(v) for v in render(events, length, adv)) or 1)
        adv = advances(events, sc)
    return adv, PEAK / (max(abs(v) for v in render(events, length, adv)) or 1)


def mix(name, ms, events):
    """events: (cue_ms, samples, gain_db). The sound is AUDIBLE (-40 dBFS in the finished track) on the cue: a soft
    attack is placed up to 250 ms early so that its audible start, not its first faint sample, meets the cue.
    Writes scene-<name>.wav, ms + 300 ms long, at PEAK."""
    length = int((ms + TAIL_MS) * SR / 1000)
    advance, sc = plan(events, length)
    out = render(events, length, advance)
    data = array.array("h", (int(v * sc * 32767) for v in out))
    with wave.open(str(HERE / f"scene-{name}.wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(data.tobytes())
    print(f"scene-{name}.wav  {length / SR:.2f}s  peak {PEAK:.2f}")


# ---- the sounds (Mixkit ids; titles in sfx/CREDITS.md) -------------------------------------------------------------
BED = -18          # rms dBFS a bed is normalised to; one-shots are peak-normalised, so beds sit ~6 dB under them
pull = lambda: load("mk-1891", 0.0, 0.55, fade_out=0.1)                                  # pull cord: pull + snap back
beep = lambda: load("mk-1076", 0.0, 0.18, fade_in=0.01, fade_out=0.06)                   # soft alarm beep
chime = lambda n=2.0: load("mk-3108", 0.0, n, fade_out=0.9)                              # crystal chime
glint = lambda: load("mk-871", 0.2, 0.9, fade_out=0.3)                                   # tiny sparkle
whoosh = lambda a=0.0, b=1.0: load("mk-1489", a, b, fade_out=0.3)                        # soft air whoosh
vortex = lambda a, b: load("mk-1493", a, b, fade_in=0.02, fade_out=0.25)                 # swirling whoosh, rises
steam = lambda a, b: load("mk-1458", a, b, fade_out=0.3)                                 # steam swoosh
winddown = lambda n: load("mk-1811", 0.0, n, fade_out=0.4)                               # motor winding down
whine = lambda n: load("mk-1895", 0.0, n, fade_in=0.08, fade_out=0.4)                    # strong spin-up (hair dryer on)
rattle = lambda n: load("mk-1844", 0.0, n, fade_out=0.15)                                # sputter / rattle
squeak = lambda n=0.5: load("mk-1018", 0.0, n, fade_out=0.15)                            # valve squeak
spritz = lambda a=0.0: load("mk-3215", a, a + 0.28, fade_out=0.1)                        # spray bottle
drip = lambda: load("mk-1879", 0.88, 1.25, fade_out=0.1)                                 # one water plink
pop = lambda: load("mk-732", 0.0, 0.66, fade_out=0.2)                                    # bubbles popping
foam = lambda: load("mk-2925", 0.0, 1.1, fade_out=0.3)                                   # soap bubbles
gurgle = lambda: load("mk-1873", 0.0, 1.3, fade_in=0.05, fade_out=0.5)                   # drain gurgle


def fan(secs, pts, a=1.5):
    """Extractor-fan hum bed with a spin-up / steady / spin-down gain curve."""
    return ramp(load("mk-1813", a, a + secs, fade_in=0.1, fade_out=0.1, rms=BED), pts)


def hiss(secs, g=0.0):
    """Steam hiss bed: a 1 s steam swoosh looped."""
    return tile(load("mk-1458", 0.3, 1.3, fade_in=0.0, fade_out=0.0, rms=BED), secs)


def shower(secs, fade=0.5):
    return load("mk-1874", 1.0, 1.0 + secs, fade_in=0.15, fade_out=fade, rms=BED)


def wipe(secs, squeaks=True):
    """Squeegee on glass: a soft wipe stretched to the sweep length, with rubber squeaks on top."""
    ev = [(0, load("mk-3087", 0.0, 0.9, stretch=secs / 0.9, fade_in=0.02, fade_out=0.08), -3)]
    if squeaks:
        ev.append((60, load("mk-1009", 0.0, min(secs, 0.8), fade_out=0.15), -9))
    return ev


def place(base, evs):
    return [(base + c, x, g) for c, x, g in evs]


def cpu_ice():
    return load("mk-2834", 0.0, 1.5, fade_out=0.5)


# ---- CPU: the exhaust fan ------------------------------------------------------------------------------------------
mix("cpu-0", 2600, [
    (620, pull(), -5),
    (650, fan(1.6, [(0, -8), (0.5, -2), (1.0, -8), (1.6, -20)]), -3),
    (900, whoosh(0.0, 0.8), -4),
    (2300, glint(), -10),
])

mix("cpu-1", 3400, [
    (670, pull(), -5),
    (700, fan(2.2, [(0, -8), (1.2, 0), (1.6, 0), (2.2, -20)]), -2),
    (1300, whoosh(0.0, 1.2), -5),
    (2700, winddown(0.9), -4),
    (3000, chime(), -7),
    (3140, glint(), -11),
    (3280, glint(), -11),
])

mix("cpu-2", 4600, [
    (200, beep(), -7), (500, beep(), -7),
    (1120, pull(), -5),
    (1150, rattle(0.5), -3),
    (1920, pull(), -5),
    (1950, whine(1.6), -6),
    (1950, fan(1.6, [(0, -8), (1.0, -2), (1.6, -6)]), -2),
    (2300, vortex(1.0, 2.4), -4),
    (2700, steam(0.2, 1.0), -8),
    (3700, winddown(1.1), -4),
    (4100, chime(), -7),
    *[(4100 + 130 * i, glint(), -11) for i in range(1, 4)],
])

mix("cpu-3", 6400, [
    *[(t, beep(), -7) for t in (200, 600, 1000, 1400, 1800)],
    (200, hiss(2.8), 1),
    (1320, pull(), -5),
    (1350, load("mk-3158", 0.0, 0.6, fade_out=0.2), -4),
    (1350, rattle(0.5), -7),
    (2000, load("mk-2980", 0.0, 1.2, fade_out=0.6), -4),
    (2400, whine(1.4), -7),
    (2400, fan(2.9, [(0, -3), (0.9, -2), (2.4, -2), (2.9, -8)]), -2),
    (3000, vortex(1.0, 2.4), -3),
    (3600, cpu_ice(), -3),
    (5300, fan(1.1, [(0, -2), (1.1, -20)]), -2),
    (5300, winddown(1.1), -4),
    (5800, chime(), -7),
    *[(5800 + 130 * i, glint(), -11) for i in range(1, 4)],
])

# ---- GPU: the mirror -----------------------------------------------------------------------------------------------
mix("gpu-0", 2200, [
    *place(700, wipe(0.9)),
    (1700, chime(1.4), -7),
])

mix("gpu-1", 2900, [
    *place(720, wipe(1.3)),
    (1000, drip(), -6), (1500, drip(), -8),
    (2200, chime(1.6), -7),
])

mix("gpu-2", 4000, [
    (450, spritz(0.0), -4), (650, spritz(0.5), -5), (850, spritz(1.0), -4),
    *place(1100, wipe(1.1)),
    *place(2350, wipe(1.0)),
    (3350, chime(1.6), -7),
])

mix("gpu-3", 5200, [
    (200, load("mk-2548", 0.0, 1.3, fade_in=0.05, fade_out=0.35), -6),
    (1500, load("mk-2593", 0.0, 0.45, fade_out=0.2), -9),
    (2000, load("mk-3085", 0.0, 1.2, fade_out=0.3), -5),
    (2100, load("mk-1009", 0.0, 0.9, fade_out=0.2), -10),
    (3400, load("mk-2600", 0.0, 1.6, fade_in=0.05, fade_out=0.5), -5),
    (4300, chime(1.6), -7),
])

# ---- All: the shower -----------------------------------------------------------------------------------------------
mix("all-0", 2800, [
    (300, squeak(), -4),
    (600, shower(1.4, 0.7), -3),
    (1600, drip(), -6), (1850, drip(), -9),
    (2000, chime(1.0), -7),
])

mix("all-1", 3800, [
    (300, squeak(), -4),
    (550, shower(2.2, 0.5), -3),
    (900, foam(), -5), (1150, foam(), -9),
    (1800, pop(), -5), (1950, pop(), -8), (2100, pop(), -10),
    (2600, squeak(0.35), -5),
    (2700, gurgle(), -5),
    (3100, chime(1.2), -7),
])

SCRUB = lambda a: load("mk-3086", a, a + 0.6, fade_in=0.02, fade_out=0.15)
mix("all-2", 5200, [
    (300, squeak(), -4),
    (550, shower(3.4, 0.5), -3),
    (1000, SCRUB(0.0), -3), (1700, SCRUB(1.0), -3), (2400, SCRUB(2.0), -3),
    (3200, foam(), -5), (3450, pop(), -7),
    (3800, squeak(0.35), -5),
    (3900, gurgle(), -5),
    (4500, chime(1.4), -7),
])

splash = lambda: load("mk-1311", 0.25, 1.2, fade_out=0.35)
mix("all-3", 6600, [
    (200, squeak(), -4),
    (450, shower(2.2, 0.5), -3),
    (700, load("mk-1321", 0.0, 1.1, fade_in=0.02, fade_out=0.3), -10),
    (1500, load("mk-1321", 0.0, 1.1, fade_in=0.02, fade_out=0.3), -5),
    (2500, squeak(0.35), -5),
    *[(t, load("mk-1883", 0.05, 0.36, fade_out=0.06), -2) for t in (3440, 3940, 4440)],
    (4700, splash(), -3),
    (4700, gurgle(), -7),
    (5800, chime(1.4), -7),
])
