"""Turn 16-bit PCM .wav files into 20 ms frame levels, the way voice chat's automatic gain control
measures them: high-passed at 100 Hz so rumble doesn't count, then the RMS of each frame."""
import array
import math
import sys
import wave
from pathlib import Path

MIN_LEVEL = -96.0
HIGH_PASS = 100.0
Q = 0.7071


def high_pass(samples, rate):
    """RBJ cookbook high-pass in transposed direct form II, the same as Sandbox.Audio.Biquad"""
    w0 = 2.0 * math.pi * HIGH_PASS / rate
    cos, alpha = math.cos(w0), math.sin(w0) / (2.0 * Q)
    a0 = 1 + alpha
    b0, b1, b2 = (1 + cos) / 2 / a0, -(1 + cos) / a0, (1 + cos) / 2 / a0
    a1, a2 = -2 * cos / a0, (1 - alpha) / a0

    z1 = z2 = 0.0
    out = []
    for x in samples:
        y = b0 * x + z1
        z1 = b1 * x - a1 * y + z2
        z2 = b2 * x - a2 * y
        out.append(y)
    return out


def frame_levels(path):
    with wave.open(str(path), "rb") as w:
        channels, width, rate = w.getnchannels(), w.getsampwidth(), w.getframerate()
        if width != 2:
            raise SystemExit(f"{path.name}: expected 16-bit samples, got {width * 8}-bit")
        samples = array.array("h", w.readframes(w.getnframes()))

    # Average the channels down to mono, like SDL does for a mono capture stream
    mono = [sum(samples[i:i + channels]) / channels / 32768.0 for i in range(0, len(samples), channels)]
    mono = high_pass(mono, rate)

    frame = rate // 50
    levels = []
    for start in range(0, len(mono) - frame + 1, frame):
        chunk = mono[start:start + frame]
        rms = math.sqrt(sum(s * s for s in chunk) / frame)
        levels.append(max(20.0 * math.log10(rms + 1e-9), MIN_LEVEL))

    return rate, channels, frame, levels


def main(source_dir, out_dir):
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)

    for path in sorted(Path(source_dir).glob("*.wav")):
        rate, channels, frame, levels = frame_levels(path)
        lines = [
            f"# {path.name}: {rate} Hz, {channels} channel(s) averaged to mono, {len(levels)} frames of 20 ms ({frame} samples)",
            f"# RMS level of each frame in dBov (a full scale square wave is 0) after a {HIGH_PASS:.0f} Hz high-pass, floored at -96, as VoiceManager.LevelMeter does it",
        ]
        lines += [f"{level:.1f}" for level in levels]
        (out / f"{path.stem}.txt").write_bytes(("\r\n".join(lines) + "\r\n").encode("utf-8"))
        print(f"{path.stem}: {len(levels)} frames, min {min(levels):.1f}, max {max(levels):.1f}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
