using System.Collections.Generic;
using UnityEngine;

public enum LabSound { Plug, Unplug, Place, Hop, Delivered, Error, Click, MissionComplete }

// Small synthesized sound effects (no audio files needed).
public class LabSounds : MonoBehaviour
{
    public static bool Muted { get; set; }
    public static float Volume { get; set; } = 0.6f;

    private static LabSounds instance;
    private AudioSource source;
    private readonly Dictionary<LabSound, AudioClip> clips = new Dictionary<LabSound, AudioClip>();
    private readonly Dictionary<LabSound, float> lastPlayed = new Dictionary<LabSound, float>();

    const int SampleRate = 44100;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        Muted = false;
    }

    public static void Play(LabSound sound)
    {
        if (Muted || !Application.isPlaying) return;

        if (instance == null)
        {
            GameObject host = new GameObject("LabSounds");
            instance = host.AddComponent<LabSounds>();
        }
        instance.PlayClip(sound);
    }

    void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;

        clips[LabSound.Plug] = Synth(0.09f, t => Click(t, 2600f, 0.012f) + Tone(t, 900f, 0.06f) * 0.5f);
        clips[LabSound.Unplug] = Synth(0.12f, t => Click(t, 1800f, 0.015f) + Sweep(t, 700f, 300f, 0.1f) * 0.4f);
        clips[LabSound.Place] = Synth(0.18f, t => Tone(t, 110f, 0.16f) * 0.9f + Noise(t, 0.04f) * 0.3f);
        clips[LabSound.Hop] = Synth(0.06f, t => Tone(t, 1320f, 0.05f) * 0.35f);
        clips[LabSound.Delivered] = Synth(0.34f, t => Tone(t, 784f, 0.14f) * 0.6f + Tone(t - 0.12f, 1175f, 0.2f) * 0.6f);
        clips[LabSound.Error] = Synth(0.3f, t => Square(t, 150f, 0.28f) * 0.35f + Square(t, 155f, 0.28f) * 0.2f);
        clips[LabSound.Click] = Synth(0.05f, t => Click(t, 3200f, 0.01f) * 0.6f);
        clips[LabSound.MissionComplete] = Synth(0.7f, t =>
            Tone(t, 523f, 0.18f) * 0.5f + Tone(t - 0.15f, 659f, 0.18f) * 0.5f + Tone(t - 0.3f, 784f, 0.35f) * 0.55f);
    }

    void PlayClip(LabSound sound)
    {
        // Avoid machine-gun repeats when many packets hop at once
        if (lastPlayed.TryGetValue(sound, out float last) && Time.unscaledTime - last < 0.04f) return;
        lastPlayed[sound] = Time.unscaledTime;

        if (clips.TryGetValue(sound, out AudioClip clip)) source.PlayOneShot(clip, Volume);
    }

    // ---------- synthesis ----------

    static AudioClip Synth(float duration, System.Func<float, float> wave)
    {
        int samples = Mathf.CeilToInt(duration * SampleRate);
        float[] data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            data[i] = Mathf.Clamp(wave(i / (float)SampleRate), -1f, 1f);
        }

        AudioClip clip = AudioClip.Create("LabSfx", samples, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float Envelope(float t, float length)
    {
        if (t < 0f || t > length) return 0f;
        float attack = Mathf.Min(1f, t / 0.004f);
        float release = 1f - t / length;
        return attack * release * release;
    }

    static float Tone(float t, float frequency, float length) => Mathf.Sin(2f * Mathf.PI * frequency * t) * Envelope(t, length);

    static float Square(float t, float frequency, float length) => Mathf.Sign(Mathf.Sin(2f * Mathf.PI * frequency * t)) * Envelope(t, length);

    static float Sweep(float t, float from, float to, float length)
    {
        float frequency = Mathf.Lerp(from, to, Mathf.Clamp01(t / length));
        return Mathf.Sin(2f * Mathf.PI * frequency * t) * Envelope(t, length);
    }

    static float Click(float t, float frequency, float length) => Tone(t, frequency, length) + Noise(t, length * 0.6f) * 0.5f;

    static float Noise(float t, float length)
    {
        // Deterministic hash noise so clips are identical every run
        float n = Mathf.Sin(t * 12345.678f) * 43758.5453f;
        return (n - Mathf.Floor(n) - 0.5f) * 2f * Envelope(t, length);
    }
}
