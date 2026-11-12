using UnityEngine;

/// <summary>
/// Generates the scooter's sounds in code, so no audio files are needed.
/// Both clips hold a whole number of wave cycles, so they loop without clicks.
/// </summary>
public static class ScooterSounds
{
    private const int SampleRate = 44100;
    private static AudioClip engine, horn;

    /// Small single-cylinder "putt-putt" loop. Play it looped and raise the pitch with speed.
    public static AudioClip Engine()
    {
        if (engine != null) return engine;
        int n = SampleRate / 2;                      // 0.5 s: 60 Hz -> 30 cycles, 30 Hz -> 15 cycles
        var data = new float[n];
        var rng = new System.Random(7);
        float peak = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float w = 2f * Mathf.PI * t;
            float tone = 0.55f * Mathf.Sin(w * 60f) + 0.25f * Mathf.Sin(w * 120f + 0.5f)
                       + 0.12f * Mathf.Sin(w * 180f + 1.1f) + 0.08f * Mathf.Sin(w * 240f + 2f);
            float putt = 0.6f + 0.4f * Mathf.Pow(Mathf.Abs(Mathf.Sin(w * 30f)), 3f);
            float noise = ((float)rng.NextDouble() * 2f - 1f) * 0.05f;
            data[i] = tone * putt + noise;
            peak = Mathf.Max(peak, Mathf.Abs(data[i]));
        }
        for (int i = 0; i < n; i++) data[i] *= 0.8f / peak;
        engine = AudioClip.Create("Scooter Engine", n, 1, SampleRate, false);
        engine.SetData(data, 0);
        return engine;
    }

    /// Two-tone "beep" loop, played for as long as the horn key is held.
    public static AudioClip Horn()
    {
        if (horn != null) return horn;
        int n = SampleRate / 2;                      // 420 Hz and 520 Hz both fit exactly into 0.5 s
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float w = 2f * Mathf.PI * i / SampleRate;
            float a = (float)System.Math.Tanh(3f * Mathf.Sin(w * 420f));
            float b = (float)System.Math.Tanh(3f * Mathf.Sin(w * 520f));
            data[i] = (a + b) * 0.35f;
        }
        horn = AudioClip.Create("Scooter Horn", n, 1, SampleRate, false);
        horn.SetData(data, 0);
        return horn;
    }
}
