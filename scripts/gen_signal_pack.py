#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Génère le pack de signaux de test téléchargeables de RoomEq (v1.9.0).

Sortie : <racine source>/signaux/ (dans l'arbre source canonique)
  - impulsion-dirac.wav      : 5 impulsions brèves (réflexions, latence)
  - bruit-gris-10s.wav       : bruit gris (pondération d'égale sonie), 10 s
  - multi-tones-10s.wav      : 15 sinus simultanés type SMPTE, 10 s
  - balayage-lent-30s.wav    : balayage logarithmique 20 Hz → 20 kHz, 30 s
  - manifest.json            : catalogue (id, nom, description, taille, sha256)

Le manifeste est lu par l'application (SignalManifest.TryParse) ; les règles
de validation C# doivent rester satisfaites : id [a-z0-9-], fileName sûr,
bytes ≤ 25 Mo, sha256 hex 64.
"""
import hashlib
import json
import math
import os
import struct
import sys
import wave

import numpy as np

RATE = 44100
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "workspace", "roomq-source", "RoomEq", "signaux")


def write_wav(name, samples):
    """Écrit un WAV mono 16 bits normalisé (crête ≤ 0,98)."""
    peak = np.max(np.abs(samples))
    if peak > 0.98:
        samples = samples / peak * 0.98
    pcm = (samples * 32767.0).astype(np.int16)
    path = os.path.join(OUT, name)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())
    return path


def a_weight_inverse(f):
    """Gain inverse de la pondération A (pour fabriquer du bruit gris : après
    pondération A à l'écoute, l'énergie par bande est constante)."""
    f = np.maximum(f, 1e-6)
    ra_num = 12194.0 ** 2 * f ** 2
    ra_den = (f ** 2 + 20.6 ** 2) * np.sqrt((f ** 2 + 107.7 ** 2) * (f ** 2 + 737.9 ** 2)) * (f ** 2 + 12194.0 ** 2)
    ra = ra_num / ra_den  # réponse en amplitude (0..1)
    return 1.0 / np.maximum(ra, 1e-9)


def grey_noise(duration):
    """Bruit gris : spectre blanc repondéré par l'inverse de la courbe A.
    Après pondération A (approximation de l'égale sonie), l'énergie par bande
    est constante — chaque bande PARAÎT de même niveau."""
    n = int(RATE * duration)
    half = n // 2 + 1
    freqs = np.fft.rfftfreq(n, 1.0 / RATE)
    mag = np.zeros(half)
    band = (freqs >= 20.0) & (freqs <= 20000.0)
    mag[band] = a_weight_inverse(freqs[band])   # blanc × 1/A = égale sonie
    phase = np.random.default_rng(42).uniform(0, 2 * np.pi, half)
    spec = mag * np.exp(1j * phase)
    spec[0] = 0
    samples = np.fft.irfft(spec, n)
    samples /= np.std(samples)          # RMS = 1
    samples *= 10 ** (-14 / 20.0)       # RMS cible −14 dBFS (rescalé si crête > 0,98)
    return samples[:n]


def multi_tones(duration):
    """15 sinus simultanés (inspiré SMPTE), −24 dBFS par tonalité."""
    n = int(RATE * duration)
    t = np.arange(n) / RATE
    freqs = [40, 63, 100, 160, 250, 400, 630, 1000, 1600, 2500, 4000, 6300, 8000, 10000, 12500]
    amp = 10 ** (-24 / 20.0)
    samples = sum(amp * np.sin(2 * np.pi * f * t) for f in freqs)
    return samples


def slow_sweep(duration=30.0, f1=20.0, f2=20000.0, amp=0.5):
    """Balayage logarithmique 20 Hz → 20 kHz, phase en forme fermée."""
    n = int(RATE * duration)
    t = np.arange(n) / RATE
    ratio = f2 / f1
    phase = 2 * np.pi * f1 * duration / math.log(ratio) * (ratio ** (t / duration) - 1.0)
    return amp * np.sin(phase)


def impulses(total=1.0, positions=None):
    """Impulsions de Dirac (1 échantillon à −3 dBFS) espacées régulièrement."""
    n = int(RATE * total)
    samples = np.zeros(n)
    if positions is None:
        positions = [0.1, 0.3, 0.5, 0.7, 0.9]
    for p in positions:
        samples[int(p * RATE)] = 10 ** (-3 / 20.0)
    return samples


def main():
    os.makedirs(OUT, exist_ok=True)

    files = [
        ("impulsion-dirac.wav", impulses(),
         "Impulsion (Dirac ×5)",
         "Cinq clics très brefs (1 échantillon, −3 dBFS) espacés de 200 ms : vérification de la latence, des réflexions et de l'écho de la pièce."),
        ("bruit-gris-10s.wav", grey_noise(10.0),
         "Bruit gris (10 s)",
         "Bruit pondéré par la courbe d'égale sonie : toutes les bandes audibles paraissent de même niveau. Confortable pour régler le volume d'écoute final."),
        ("multi-tones-10s.wav", multi_tones(10.0),
         "Multi-tones SMPTE (10 s)",
         "Quinze sinus simultanés (40 Hz → 12,5 kHz, −24 dBFS chacun) : test d'intermodulation et d'équilibre spectral rapide."),
        ("balayage-lent-30s.wav", slow_sweep(),
         "Balayage lent 20 Hz → 20 kHz (30 s)",
         "Balayage logarithmique de 30 secondes : analyse fine des modes de pièce et des résonances entre 20 Hz et 20 kHz."),
    ]

    signals = []
    for name, data, display, description in files:
        write_wav(name, data)
        full = os.path.join(OUT, name)
        size = os.path.getsize(full)
        sha = hashlib.sha256(open(full, "rb").read()).hexdigest()
        signals.append({
            "id": name[:-4],
            "displayName": display,
            "description": description,
            "fileName": name,
            "bytes": size,
            "sha256": sha,
        })
        print(f"  {name:26s} {size:>10,d} o  sha256={sha[:16]}…")

    manifest = {"version": 1, "signals": signals}
    path = os.path.join(OUT, "manifest.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
        f.write("\n")

    total = sum(s["bytes"] for s in signals)
    print(f"\nManifeste : {path}")
    print(f"{len(signals)} signaux, {total/1e6:.1f} Mo au total")


if __name__ == "__main__":
    main()
