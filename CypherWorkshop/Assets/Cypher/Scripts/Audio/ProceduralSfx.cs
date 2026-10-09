using System;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// Synthesizes every sound effect in code at startup, so the project needs no audio files
    /// (and no licenses). Each method returns a ready-to-play AudioClip.
    /// To use your own sounds instead, assign clips in the CypherAudio Inspector.
    /// </summary>
    public static class ProceduralSfx
    {
        const int Rate = 44100;
        const double TwoPi = Math.PI * 2.0;

        public static AudioClip Hover()
        {
            var data = Render(0.05f, (t, _) => Math.Sin(TwoPi * 2300 * t) * Env(t, 0.002, 60));
            return Make("Hover", Normalize(data, 0.5f));
        }

        public static AudioClip Click()
        {
            var data = Render(0.09f, (t, _) =>
                (Math.Sin(TwoPi * 1400 * t) + 0.5 * Math.Sin(TwoPi * 2800 * t)) * Env(t, 0.001, 45));
            return Make("Click", Normalize(data, 0.6f));
        }

        public static AudioClip PickUp() => Make("PickUp", Normalize(Sweep(0.14f, 420, 680, 22), 0.6f));

        public static AudioClip Drop() => Make("Drop", Normalize(Sweep(0.16f, 640, 300, 20), 0.6f));

        public static AudioClip Open() => Make("Open", Normalize(Sweep(0.22f, 320, 980, 9), 0.5f));

        public static AudioClip Close() => Make("Close", Normalize(Sweep(0.2f, 900, 340, 11), 0.5f));

        /// <summary>Three quick rising notes: "data synced".</summary>
        public static AudioClip Sync()
        {
            double[] notes = { 880, 1175, 1568 };
            const double step = 0.075;
            var data = Render(0.32f, (t, _) =>
            {
                double s = 0;
                for (int i = 0; i < notes.Length; i++)
                {
                    double local = t - i * step;
                    if (local >= 0) s += Math.Sin(TwoPi * notes[i] * local) * Env(local, 0.002, 18);
                }
                return s;
            });
            return Make("Sync", Normalize(data, 0.55f));
        }

        /// <summary>
        /// Paper crinkling: a dense burst of tiny high-pitched clicks that thins out, over a
        /// breath of hiss. Different seeds give different (but repeatable) crinkles.
        /// </summary>
        public static AudioClip Crinkle(int seed)
        {
            var rng = new System.Random(1000 + seed);
            const float seconds = 0.42f;
            int n = (int)(Rate * seconds);
            var data = new float[n];

            for (int c = 0; c < 140; c++)
            {
                // Clicks cluster near the start (squeeze) and thin out.
                double t = Math.Pow(rng.NextDouble(), 1.8) * (seconds - 0.02);
                int start = (int)(t * Rate);
                double amp = (0.3 + rng.NextDouble() * 0.7) * Math.Exp(-t * 4.0);
                double decay = 1.0 / (0.0015 + rng.NextDouble() * 0.004);
                double prev = 0;
                for (int i = 0; i < Rate * 0.012 && start + i < n; i++)
                {
                    double white = rng.NextDouble() * 2 - 1;
                    double hp = white - prev * 0.85; // crude high-pass: paper is bright
                    prev = white;
                    data[start + i] += (float)(hp * amp * Math.Exp(-i / (double)Rate * decay));
                }
            }
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                data[i] += (float)((rng.NextDouble() * 2 - 1) * 0.04 * Math.Exp(-t * 6));
            }
            return Make("Crinkle " + seed, Normalize(data, 0.6f));
        }

        /// <summary>Air rushing past: noise through a band-pass filter sweeping upward.</summary>
        public static AudioClip Whoosh()
        {
            var data = BandSweep(0.45f, 350, 2200, 0.6, 3);
            return Make("Whoosh", Normalize(data, 0.5f));
        }

        /// <summary>
        /// The landing: a basketball-net "swish" (noise sweeping down, fluttering), a soft thump,
        /// and a small bright chime for satisfaction.
        /// </summary>
        public static AudioClip Swish()
        {
            const float seconds = 0.7f;
            var swish = BandSweep(seconds, 3200, 700, 0.35, 5);
            var data = new float[swish.Length];
            for (int i = 0; i < data.Length; i++)
            {
                double t = i / (double)Rate;
                double flutter = 0.7 + 0.3 * Math.Sin(TwoPi * 28 * t);
                double swishEnv = Math.Min(1, t / 0.01) * Math.Exp(-t * 7);
                double thump = Math.Sin(TwoPi * (90 - 40 * t) * t) * Math.Exp(-t * 22) * 0.6;
                double chime = (Math.Sin(TwoPi * 1568 * t) + 0.4 * Math.Sin(TwoPi * 2349 * t)) * Math.Exp(-t * 6) * 0.18 * Math.Min(1, t / 0.005);
                data[i] = (float)(swish[i] * flutter * swishEnv + thump + chime);
            }
            return Make("Swish", Normalize(data, 0.7f));
        }

        /// <summary>White noise through a resonant band-pass whose center glides from -> to.</summary>
        static float[] BandSweep(float seconds, double fromHz, double toHz, double q, int seed)
        {
            var rng = new System.Random(seed);
            int n = (int)(Rate * seconds);
            var data = new float[n];
            double low = 0, band = 0;
            for (int i = 0; i < n; i++)
            {
                double x = i / (double)n;
                double fc = fromHz * Math.Pow(toHz / fromHz, x);
                double f = 2 * Math.Sin(Math.PI * fc / Rate);
                double input = rng.NextDouble() * 2 - 1;
                low += f * band;
                double high = input - low - q * band;
                band += f * high;
                double env = Math.Sin(Math.PI * Math.Min(1, x * 1.15));
                data[i] = (float)(band * env);
            }
            return data;
        }

        /// <summary>Short low buzz for invalid input.</summary>
        public static AudioClip Error()
        {
            var data = Render(0.18f, (t, _) => Math.Sign(Math.Sin(TwoPi * 150 * t)) * 0.5 * Env(t, 0.004, 14));
            return Make("Error", Normalize(data, 0.35f));
        }

        /// <summary>Rising tonal sweep + sparkling high partials + a breath of noise.</summary>
        public static AudioClip Materialize(float seconds = 1.1f) =>
            Make("Materialize", Normalize(MaterializeData(seconds, 11), 0.8f));

        /// <summary>The materialize sound played backwards, so it "un-builds".</summary>
        public static AudioClip Dematerialize(float seconds = 0.7f)
        {
            var data = MaterializeData(seconds, 23);
            Array.Reverse(data);
            return Make("Dematerialize", Normalize(data, 0.8f));
        }

        static float[] MaterializeData(float seconds, int seed)
        {
            var rng = new System.Random(seed);
            int n = (int)(Rate * seconds);
            var data = new float[n];

            var sparkFreq = new double[6];
            var sparkRate = new double[6];
            for (int i = 0; i < 6; i++)
            {
                sparkFreq[i] = 2000 + rng.NextDouble() * 3000;
                sparkRate[i] = 7 + rng.NextDouble() * 9;
            }

            double phase = 0, noise = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double x = t / seconds;

                double freq = 250 + 1100 * Math.Pow(x, 1.5) + 12 * Math.Sin(TwoPi * 6 * t);
                phase += TwoPi * freq / Rate;
                double tone = Math.Sin(phase) * 0.5;

                double sparkle = 0;
                for (int k = 0; k < 6; k++)
                    sparkle += Math.Sin(TwoPi * sparkFreq[k] * t) * Math.Max(0, Math.Sin(TwoPi * sparkRate[k] * t + k));
                sparkle *= 0.06 * x;

                noise += ((rng.NextDouble() * 2 - 1) - noise) * 0.08; // one-pole low-pass
                double air = noise * 0.6 * Math.Sin(Math.PI * x);

                double env = Math.Min(1, t / 0.03) * Math.Min(1, (seconds - t) / 0.25);
                data[i] = (float)((tone + sparkle + air) * env);
            }
            return data;
        }

        static float[] Sweep(float seconds, double from, double to, double decay)
        {
            double phase = 0;
            return Render(seconds, (t, x) =>
            {
                phase += TwoPi * (from + (to - from) * x) / Rate;
                return Math.Sin(phase) * Env(t, 0.003, decay);
            });
        }

        /// <summary>Calls sample(timeSeconds, progress0to1) for every sample.</summary>
        static float[] Render(float seconds, Func<double, double, double> sample)
        {
            int n = (int)(Rate * seconds);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                data[i] = (float)sample(t, t / seconds);
            }
            return data;
        }

        /// <summary>Linear attack, then exponential decay.</summary>
        static double Env(double t, double attack, double decay) => Math.Min(1, t / attack) * Math.Exp(-t * decay);

        static float[] Normalize(float[] data, float peak)
        {
            float max = 1e-6f;
            foreach (var s in data) max = Mathf.Max(max, Mathf.Abs(s));
            float k = peak / max;
            for (int i = 0; i < data.Length; i++) data[i] *= k;
            return data;
        }

        static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create("Cypher " + name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
