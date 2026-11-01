using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// A hand power's sound, made here (TaleSpire has no fierce fire or ice sound - only its gentle campfire crackle).
    /// Flame: a whoosh as it ignites, then a roaring fire - a low turbulent roar, the rush of the flame, a hiss and dense
    /// crackles. Ice: a frosty rush as it starts, then a cold, airy hiss with tinkling crystals and cracking ice. Either
    /// gets louder and a little higher as the power grows and fades quickly when you let go. It comes from your right
    /// hand, through TaleSpire's sound-effects volume.
    /// </summary>
    internal sealed class FlameSound
    {
        private const int Rate = 44100;
        private static AudioClip _roar, _whoosh, _frost, _frostBurst;

        private readonly bool _ice;
        private GameObject _go;
        private AudioSource _loop, _burst;
        private float _level;
        private bool _wasHeld;

        public FlameSound(bool ice = false) { _ice = ice; }

        /// <summary>The flame's ignition whoosh (also the fire staff's shot).</summary>
        public static AudioClip Whoosh => _whoosh != null ? _whoosh : (_whoosh = MakeWhoosh());

        private AudioClip LoopClip => _ice ? _frost : _roar;
        private AudioClip BurstClip => _ice ? _frostBurst : _whoosh;

        /// <summary>Each frame: held = the flame is burning; power = its growth 0..1; at = your hand.</summary>
        public void Update(bool held, float power, Vector3 at, float dt)
        {
            if (held && !_wasHeld && Ensure())
            {
                _go.transform.position = at;
                _burst.PlayOneShot(BurstClip, 1f);
            }
            _wasHeld = held;
            float target = held ? Mathf.Lerp(0.45f, 0.95f, power) : 0f;
            _level = Mathf.MoveTowards(_level, target, dt / (held ? 0.15f : 0.35f));
            if (_go == null) return;
            _go.transform.position = at;
            if (_level <= 0f)
            {
                if (_loop.isPlaying) _loop.Stop();
                return;
            }
            _loop.volume = _level;
            _loop.pitch = 0.92f + 0.16f * power;
            if (!_loop.isPlaying)
            {
                _loop.Play();
                _loop.time = Random.Range(0f, _loop.clip.length * 0.9f);
            }
        }

        public void Stop()
        {
            _level = 0f;
            _wasHeld = false;
            if (_loop != null) _loop.Stop();
            if (_burst != null) _burst.Stop();
        }

        private bool Ensure()
        {
            if (_go != null) return true;
            try
            {
                if (_ice)
                {
                    if (_frost == null) _frost = MakeFrost();
                    if (_frostBurst == null) _frostBurst = MakeFrostBurst();
                }
                else
                {
                    if (_roar == null) _roar = MakeRoar();
                    if (_whoosh == null) _whoosh = MakeWhoosh();
                }
                _go = new GameObject(_ice ? "TaleSpireVR_IceSound" : "TaleSpireVR_FlameSound");
                Object.DontDestroyOnLoad(_go);
                _loop = NewSource(_go);
                _loop.clip = LoopClip;
                _loop.loop = true;
                _burst = NewSource(_go);
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Flame sound failed: " + e);
                return false;
            }
        }

        private static AudioSource NewSource(GameObject go)
        {
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0.6f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1000f;   // (as WeaponFx: board units can be tiny in first person)
            src.maxDistance = 2000f;
            var group = WeaponFx.SfxGroup();
            if (group != null) src.outputAudioMixerGroup = group;
            return src;
        }

        // ---- Making the sounds -------------------------------------------------------------------------------------

        /// <summary>One-pole low-pass coefficient for a cut-off frequency.</summary>
        private static float Lp(float hz) => 1f - Mathf.Exp(-2f * Mathf.PI * hz / Rate);

        /// <summary>A smooth random swell between 0 and 1: a new target every 1/rate s or so, eased towards.</summary>
        private sealed class Swell
        {
            private readonly System.Random _r;
            private readonly float _rate, _ease;
            private float _v, _target;
            private int _left;
            public Swell(System.Random r, float rate) { _r = r; _rate = rate; _ease = Lp(rate * 1.5f); _v = _target = 0.5f; }
            public float Next()
            {
                if (--_left <= 0)
                {
                    _target = (float)_r.NextDouble();
                    _left = (int)(Rate / _rate * (0.5 + _r.NextDouble()));
                }
                _v += (_target - _v) * _ease;
                return _v;
            }
        }

        /// <summary>
        /// The roaring fire, 6 s, looping seamlessly: a low turbulent roar (low-passed noise, swelling), the rush of the
        /// flame (a band of noise around 0.7-1.5 kHz), a flickering hiss above 3 kHz, and sparse-to-dense crackles.
        /// </summary>
        private static AudioClip MakeRoar()
        {
            const float seconds = 6f, overlap = 0.4f;
            int n = (int)(seconds * Rate), x = (int)(overlap * Rate), total = n + x;
            var r = new System.Random(20261024);
            float W() => (float)(r.NextDouble() * 2.0 - 1.0);
            var roar = new float[total];
            var rush = new float[total];
            var hiss = new float[total];
            var crackle = new float[total];
            var swellRoar = new Swell(r, 7f);
            var swellRush = new Swell(r, 5f);
            var swellHiss = new Swell(r, 4f);
            float a1 = Lp(450f), a2 = Lp(450f), bLo = Lp(1500f), bHi = Lp(700f), hLo = Lp(3000f);
            float l1 = 0f, l2 = 0f, rLo = 0f, rHi = 0f, hl = 0f;
            float crackAmp = 0f, crackDecay = 1f, crackHp = 0f;
            for (int i = 0; i < total; i++)
            {
                float w = W();
                l1 += (w - l1) * a1;
                l2 += (l1 - l2) * a2;
                roar[i] = l2 * (0.45f + 0.55f * swellRoar.Next());
                rLo += (w - rLo) * bLo;
                rHi += (rLo - rHi) * bHi;
                rush[i] = (rLo - rHi) * (0.3f + 0.7f * swellRush.Next());
                hl += (w - hl) * hLo;
                float sh = swellHiss.Next();
                hiss[i] = (w - hl) * (0.25f + 0.75f * sh * sh);
                // Crackles: about 35 a second, most small, a few loud; each a few milliseconds of bright noise.
                if (r.NextDouble() < 35.0 / Rate)
                {
                    double u = r.NextDouble();
                    crackAmp = 0.15f + 0.85f * (float)(u * u * u);
                    crackDecay = Mathf.Exp(-1f / (Rate * (0.0006f + 0.0025f * (float)r.NextDouble())));
                }
                float c = W() * crackAmp;
                crackAmp *= crackDecay;
                crackHp += (c - crackHp) * Lp(1000f);
                crackle[i] = c - crackHp;
            }
            Normalize(roar, Rms(roar));
            Normalize(rush, Rms(rush));
            Normalize(hiss, Rms(hiss));
            Normalize(crackle, Peak(crackle));
            var mix = new float[total];
            for (int i = 0; i < total; i++)
                mix[i] = 0.7f * roar[i] + 0.28f * rush[i] + 0.07f * hiss[i] + 0.45f * crackle[i];
            // Loop: the start fades in from what follows the end, so the end runs on into the start without a seam.
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = i < x ? mix[i] * ((float)i / x) + mix[n + i] * (1f - (float)i / x) : mix[i];
            SoftLimit(data, 0.9f);
            var clip = AudioClip.Create("TaleSpireVR_FlameRoar", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>The flame catching: a rush of noise whose pitch sweeps up and back down, with a low thump, 1 s.</summary>
        private static AudioClip MakeWhoosh()
        {
            const float seconds = 1f;
            int n = (int)(seconds * Rate);
            var r = new System.Random(4242);
            var data = new float[n];
            float low = 0f, band = 0f, thump = 0f, aThump = Lp(110f);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float w = (float)(r.NextDouble() * 2.0 - 1.0);
                // Centre frequency: 220 Hz up to 1.7 kHz in 0.18 s, then back down to 500 Hz.
                float hz = t < 0.18f ? 220f * Mathf.Pow(1700f / 220f, t / 0.18f) : 1700f * Mathf.Pow(500f / 1700f, (t - 0.18f) / (seconds - 0.18f));
                float f = 2f * Mathf.Sin(Mathf.PI * hz / Rate);
                low += f * band;
                float high = w - low - 0.6f * band;   // (a fairly narrow band: the rush, not a hiss)
                band += f * high;
                float env = t < 0.05f ? (t / 0.05f) * (t / 0.05f) : Mathf.Exp(-(t - 0.05f) / 0.28f);
                thump += (w - thump) * aThump;
                data[i] = env * band + 6f * thump * Mathf.Exp(-t / 0.15f) * Mathf.Min(1f, t / 0.01f);
            }
            SoftLimit(data, 0.95f);
            var clip = AudioClip.Create("TaleSpireVR_FlameWhoosh", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// The ice, 6 s, looping seamlessly: a cold airy hiss (a 2.5 - 6 kHz band, swelling), a faint lower rush, crystals
        /// tinkling (short high pings, about 18 a second) and ice cracking (crunchy bursts, about 6 a second).
        /// </summary>
        private static AudioClip MakeFrost()
        {
            const float seconds = 6f, overlap = 0.4f;
            int n = (int)(seconds * Rate), x = (int)(overlap * Rate), total = n + x;
            var r = new System.Random(1225);
            float W() => (float)(r.NextDouble() * 2.0 - 1.0);
            var hiss = new float[total];
            var rush = new float[total];
            var tink = new float[total];
            var crack = new float[total];
            var swellHiss = new Swell(r, 3f);
            var swellRush = new Swell(r, 4f);
            float hLo = Lp(6000f), hHi = Lp(2500f), bLo = Lp(1500f), bHi = Lp(600f);
            float h1 = 0f, h2 = 0f, b1 = 0f, b2 = 0f;
            // Pings: up to 8 at once, each a decaying sine.
            var pf = new float[8]; var pa = new float[8]; var pd = new float[8]; var pp = new float[8];
            float crackAmp = 0f, crackDecay = 1f, crackLp = 0f;
            for (int i = 0; i < total; i++)
            {
                float w = W();
                h1 += (w - h1) * hLo; h2 += (h1 - h2) * hHi;
                float sh = swellHiss.Next();
                hiss[i] = (h1 - h2) * (0.35f + 0.65f * sh * sh);
                b1 += (w - b1) * bLo; b2 += (b1 - b2) * bHi;
                rush[i] = (b1 - b2) * (0.3f + 0.7f * swellRush.Next());
                if (r.NextDouble() < 18.0 / Rate)
                {
                    int k = r.Next(8);
                    pf[k] = 2f * Mathf.PI * (2500f + 5500f * (float)r.NextDouble()) / Rate;
                    double u = r.NextDouble();
                    pa[k] = 0.2f + 0.8f * (float)(u * u);
                    pd[k] = Mathf.Exp(-1f / (Rate * (0.015f + 0.045f * (float)r.NextDouble())));
                    pp[k] = 0f;
                }
                float t = 0f;
                for (int k = 0; k < 8; k++)
                {
                    if (pa[k] < 1e-4f) continue;
                    t += Mathf.Sin(pp[k]) * pa[k];
                    pp[k] += pf[k];
                    pa[k] *= pd[k];
                }
                tink[i] = t;
                if (r.NextDouble() < 6.0 / Rate)
                {
                    crackAmp = 0.3f + 0.7f * (float)r.NextDouble();
                    crackDecay = Mathf.Exp(-1f / (Rate * (0.005f + 0.015f * (float)r.NextDouble())));
                }
                float c = W() * crackAmp;
                crackAmp *= crackDecay;
                crackLp += (c - crackLp) * Lp(2000f);
                crack[i] = crackLp;
            }
            Normalize(hiss, Rms(hiss));
            Normalize(rush, Rms(rush));
            Normalize(tink, Peak(tink));
            Normalize(crack, Peak(crack));
            var mix = new float[total];
            for (int i = 0; i < total; i++)
                mix[i] = 0.45f * hiss[i] + 0.22f * rush[i] + 0.5f * tink[i] + 0.4f * crack[i];
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = i < x ? mix[i] * ((float)i / x) + mix[n + i] * (1f - (float)i / x) : mix[i];
            SoftLimit(data, 0.85f);
            var clip = AudioClip.Create("TaleSpireVR_IceLoop", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>The ice taking hold: a rush of noise sweeping down from very high (6 kHz to 1.2 kHz) with a few crystal pings, 1 s.</summary>
        private static AudioClip MakeFrostBurst()
        {
            const float seconds = 1f;
            int n = (int)(seconds * Rate);
            var r = new System.Random(77);
            var data = new float[n];
            float low = 0f, band = 0f;
            var pingAt = new[] { 0.04f, 0.11f, 0.19f, 0.3f, 0.42f };
            var pingHz = new[] { 6200f, 4100f, 7400f, 3300f, 5200f };
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float w = (float)(r.NextDouble() * 2.0 - 1.0);
                float hz = 6000f * Mathf.Pow(1200f / 6000f, Mathf.Clamp01(t / 0.6f));
                float f = 2f * Mathf.Sin(Mathf.PI * hz / Rate);   // (stays well inside this filter's stable range)
                low += f * band;
                float high = w - low - 0.7f * band;
                band += f * high;
                float env = t < 0.03f ? t / 0.03f : Mathf.Exp(-(t - 0.03f) / 0.3f);
                float v = env * band;
                for (int k = 0; k < pingAt.Length; k++)
                {
                    float dt = t - pingAt[k];
                    if (dt >= 0f) v += 0.5f * Mathf.Sin(2f * Mathf.PI * pingHz[k] * dt) * Mathf.Exp(-dt / 0.05f);
                }
                data[i] = v;
            }
            SoftLimit(data, 0.95f);
            var clip = AudioClip.Create("TaleSpireVR_IceBurst", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Rms(float[] d)
        {
            double sum = 0;
            foreach (float v in d) sum += v * v;
            return (float)System.Math.Sqrt(sum / Mathf.Max(1, d.Length));
        }

        private static float Peak(float[] d)
        {
            float p = 0f;
            foreach (float v in d) p = Mathf.Max(p, Mathf.Abs(v));
            return p;
        }

        private static void Normalize(float[] d, float by)
        {
            if (by < 1e-9f) return;
            for (int i = 0; i < d.Length; i++) d[i] /= by;
        }

        /// <summary>Round off the loudest peaks (tanh), then scale so the loudest is at peak.</summary>
        private static void SoftLimit(float[] d, float peak)
        {
            float rms = Rms(d);
            if (rms < 1e-9f) return;
            float k = 0.35f / rms;   // (about the level where tanh starts to bend)
            for (int i = 0; i < d.Length; i++) d[i] = (float)System.Math.Tanh(d[i] * k);
            float p = Peak(d);
            if (p > 1e-9f) for (int i = 0; i < d.Length; i++) d[i] *= peak / p;
        }
    }
}
