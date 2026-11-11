using NAudio.Wave;

namespace IskolaCsengo;

/// <summary>A csengőhang lejátszása, beállított hosszal és a végén halkítással.</summary>
public sealed class BellPlayer
{
    WaveOutEvent? output;
    AudioFileReader? reader;
    DateTime started;
    int duration;
    bool fade;
    float baseVolume;

    const double FadeSeconds = 2.5;

    public bool IsPlaying { get; private set; }
    public string Title { get; private set; } = "";
    public event Action? Finished;

    public double Elapsed => IsPlaying ? (DateTime.Now - started).TotalSeconds : 0;

    /// <summary>Teljes hossz másodpercben (a beállított hossz vagy a fájl hossza, amelyik rövidebb).</summary>
    public double Total
    {
        get
        {
            if (!IsPlaying || reader == null) return 0;
            var len = reader.TotalTime.TotalSeconds;
            return duration > 0 ? Math.Min(duration, len) : len;
        }
    }

    public void Play(string path, int durationSeconds, int volume, bool fadeOut, string title)
    {
        StopInternal();
        reader = new AudioFileReader(path);
        baseVolume = Math.Clamp(volume, 0, 100) / 100f;
        reader.Volume = baseVolume;
        output = new WaveOutEvent();
        output.Init(reader);
        output.Play();
        started = DateTime.Now;
        duration = durationSeconds;
        fade = fadeOut;
        Title = title;
        IsPlaying = true;
    }

    /// <summary>Az űrlap időzítője hívja ~4-szer másodpercenként.</summary>
    public void Tick()
    {
        if (!IsPlaying || reader == null || output == null) return;

        var elapsed = Elapsed;
        var fileLength = reader.TotalTime.TotalSeconds;

        if (fade && duration > 0 && duration < fileLength)
        {
            var remaining = duration - elapsed;
            if (remaining < FadeSeconds)
                reader.Volume = baseVolume * (float)Math.Clamp(remaining / FadeSeconds, 0, 1);
        }

        if ((duration > 0 && elapsed >= duration) || output.PlaybackState == PlaybackState.Stopped)
            Stop();
    }

    public void Stop()
    {
        if (!IsPlaying) return;
        StopInternal();
        Finished?.Invoke();
    }

    void StopInternal()
    {
        IsPlaying = false;
        try { output?.Stop(); } catch { }
        try { output?.Dispose(); } catch { }
        try { reader?.Dispose(); } catch { }
        output = null;
        reader = null;
    }
}
