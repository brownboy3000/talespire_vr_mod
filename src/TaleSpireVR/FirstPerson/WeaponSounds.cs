using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// Shot sounds TaleSpire doesn't have (it has arrow hits and misses, but no bow being loosed), made at first use like
    /// the hand powers' sounds: a bow's twang and a magic wand's zap.
    /// </summary>
    internal static class WeaponSounds
    {
        private const int Rate = 44100;
        private static AudioClip _twang, _zap;

        /// <summary>
        /// A bowstring let go, 0.6 s: a plucked string (Karplus-Strong at ~150 Hz, its pitch dropping a little as it
        /// settles, a bright attack that dulls fast), the wooden thump of the limbs, and the arrow's short "fwip".
        /// </summary>
        public static AudioClip Twang
        {
            get
            {
                if (_twang != null) return _twang;
                int n = (int)(0.6f * Rate);
                var data = new float[n];
                var r = new System.Random(311);
                // The string: a delay line filled with noise, averaged as it loops (a plucked string).
                int period = Rate / 150;
                var line = new float[period * 2];
                for (int i = 0; i < line.Length; i++) line[i] = (float)(r.NextDouble() * 2.0 - 1.0);
                int len = period, at = 0;
                float lp = 0f, thump = 0f, whoosh = 0f, band = 0f, low = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / Rate;
                    // The pitch sinks ~8% over the first 0.1 s (the string slackening).
                    len = Mathf.Clamp(Mathf.RoundToInt(period * (1f + 0.08f * Mathf.Clamp01(t / 0.1f))), 2, line.Length - 1);
                    int next = (at + 1) % len;
                    float damp = t < 0.03f ? 0.5f : 0.499f;
                    float v = line[at];
                    line[at] = damp * (line[at] + line[next]);
                    at = next;
                    float str = v * Mathf.Exp(-t / 0.18f);
                    // The limbs: a low thump in the first 40 ms.
                    float w = (float)(r.NextDouble() * 2.0 - 1.0);
                    thump += (w - thump) * 0.02f;
                    float th = thump * 8f * Mathf.Exp(-t / 0.03f);
                    // The arrow leaving: band-passed noise sweeping 3 kHz -> 1 kHz over 0.15 s.
                    float hz = Mathf.Lerp(3000f, 1000f, Mathf.Clamp01(t / 0.15f));
                    float f = 2f * Mathf.Sin(Mathf.PI * hz / Rate);
                    low += f * band;
                    band += f * (w - low - 0.8f * band);
                    whoosh = band * 0.35f * Mathf.Exp(-t / 0.07f) * Mathf.Clamp01(t / 0.01f);
                    lp += (str - lp) * 0.6f;
                    data[i] = lp + th + whoosh;
                }
                Normalize(data, 0.9f);
                _twang = AudioClip.Create("TaleSpireVR_BowTwang", n, 1, Rate, false);
                _twang.SetData(data, 0);
                return _twang;
            }
        }

        /// <summary>A magic zap, 0.5 s: a bright tone rising and shimmering (two detuned sines) over a soft hiss, fading out.</summary>
        public static AudioClip Zap
        {
            get
            {
                if (_zap != null) return _zap;
                int n = (int)(0.5f * Rate);
                var data = new float[n];
                var r = new System.Random(95);
                double p1 = 0, p2 = 0;
                float hiss = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / Rate;
                    float hz = Mathf.Lerp(600f, 1800f, Mathf.Sqrt(Mathf.Clamp01(t / 0.25f)));
                    p1 += 2.0 * Mathf.PI * hz / Rate;
                    p2 += 2.0 * Mathf.PI * hz * 1.503 / Rate;
                    float shimmer = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 22f * t);
                    float env = Mathf.Clamp01(t / 0.015f) * Mathf.Exp(-t / 0.16f);
                    float w = (float)(r.NextDouble() * 2.0 - 1.0);
                    hiss += (w - hiss) * 0.5f;
                    data[i] = env * ((float)System.Math.Sin(p1) * 0.6f + (float)System.Math.Sin(p2) * 0.35f * shimmer + (w - hiss) * 0.15f);
                }
                Normalize(data, 0.8f);
                _zap = AudioClip.Create("TaleSpireVR_MagicZap", n, 1, Rate, false);
                _zap.SetData(data, 0);
                return _zap;
            }
        }

        private static void Normalize(float[] d, float peak)
        {
            float p = 0f;
            foreach (float v in d) p = Mathf.Max(p, Mathf.Abs(v));
            if (p > 1e-6f) for (int i = 0; i < d.Length; i++) d[i] *= peak / p;
        }

        /// <summary>A short burst of coloured light at a point (a wand's or staff's shot), gone after a moment.</summary>
        public static void Flash(Vector3 at, Color color, float range, float seconds = 0.2f)
        {
            var go = new GameObject("TaleSpireVR_ShotLight");
            go.transform.position = at;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = 3.5f;
            l.shadows = LightShadows.None;
            Object.Destroy(go, seconds);
        }
    }
}
