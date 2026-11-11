using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace IskolaCsengo;

/// <summary>Iskolarádió: helyi lejátszási lista vagy online rádióadás.</summary>
public sealed class RadioPlayer
{
    readonly Action<Action> runOnUi;

    WaveOutEvent? output;
    WaveStream? reader;
    VolumeSampleProvider? volumeProvider;

    List<string> tracks = new();
    List<int> order = new();
    int pos = -1;
    bool shuffle;
    int volume = 70;
    bool stopRequested;
    int openToken;
    int skipCount;
    DateTime? reconnectAt;
    string? streamUrl;
    string? streamName;

    // halkítás / hangosítás (0..1 szorzó a hangerőn)
    float gain = 1f, gainTarget = 1f, gainStep = 1f;
    Action? afterFade;
    bool fadeInOnStart;
    double fadeInSeconds;

    public RadioPlayer(Action<Action> runOnUi) => this.runOnUi = runOnUi;

    /// <summary>"none", "playlist" vagy "stream"</summary>
    public string Mode { get; private set; } = "none";
    public string Title { get; private set; } = "";
    public string? Error { get; private set; }
    public bool Loading { get; private set; }

    public bool IsPlaying => output?.PlaybackState == PlaybackState.Playing;
    public bool IsPaused => output?.PlaybackState == PlaybackState.Paused;
    public bool IsActive => IsPlaying || IsPaused || Loading || reconnectAt != null;
    public int CurrentTrackIndex => Mode == "playlist" && pos >= 0 && pos < order.Count ? order[pos] : -1;

    public double Position
    {
        get { try { return reader?.CurrentTime.TotalSeconds ?? 0; } catch { return 0; } }
    }

    public double Length
    {
        get
        {
            if (Mode != "playlist") return 0;
            try { return reader?.TotalTime.TotalSeconds ?? 0; } catch { return 0; }
        }
    }

    public int Volume
    {
        get => volume;
        set
        {
            volume = Math.Clamp(value, 0, 100);
            ApplyVolume();
        }
    }

    static float Curve(int v) => (float)Math.Pow(v / 100.0, 2);

    void ApplyVolume()
    {
        if (volumeProvider != null) volumeProvider.Volume = Curve(volume) * gain;
    }

    /// <summary>Halkítás folyamatban (csengetés előtt).</summary>
    public bool FadingOut => afterFade != null;

    /// <summary>Fokozatosan elhalkul, majd szünetel (stop = false) vagy leáll (stop = true).</summary>
    public void FadeOut(double seconds, bool stop)
    {
        if (!IsPlaying || seconds <= 0)
        {
            if (stop) Stop(); else Pause();
            return;
        }
        gainTarget = 0f;
        gainStep = (float)(0.25 / seconds); // a Tick 250 ms-onként fut
        afterFade = stop ? Stop : () => { output?.Pause(); };
    }

    void StartFadeIn(double seconds)
    {
        afterFade = null;
        if (seconds <= 0) { gain = gainTarget = 1f; ApplyVolume(); return; }
        gain = 0f;
        gainTarget = 1f;
        gainStep = (float)(0.25 / seconds);
        ApplyVolume();
    }

    void ResetGain()
    {
        afterFade = null;
        gain = gainTarget = 1f;
        ApplyVolume();
    }

    public void SetTracks(List<string> newTracks, bool newShuffle)
    {
        string? current = CurrentTrackIndex >= 0 && CurrentTrackIndex < tracks.Count ? tracks[CurrentTrackIndex] : null;
        bool reorder = newShuffle != shuffle || !newTracks.SequenceEqual(tracks);
        tracks = new List<string>(newTracks);
        shuffle = newShuffle;
        if (!reorder) return;

        BuildOrder();
        pos = current != null ? order.FindIndex(i => tracks[i] == current) : -1;
        if (current != null && pos > 0 && shuffle)
        {
            // az éppen szóló szám kerüljön a sor elejére
            var t = order[pos]; order.RemoveAt(pos); order.Insert(0, t); pos = 0;
        }
    }

    void BuildOrder()
    {
        order = Enumerable.Range(0, tracks.Count).ToList();
        if (shuffle) Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(order));
    }

    public void PlayPlaylist()
    {
        if (tracks.Count == 0) { Error = "A lejátszási lista üres."; return; }
        if (order.Count != tracks.Count) BuildOrder();
        if (pos < 0 || pos >= order.Count) pos = 0;
        Mode = "playlist";
        skipCount = 0;
        OpenFile(tracks[order[pos]]);
    }

    public void PlayTrack(int trackIndex)
    {
        if (trackIndex < 0 || trackIndex >= tracks.Count) return;
        if (order.Count != tracks.Count) BuildOrder();
        Mode = "playlist";
        pos = order.IndexOf(trackIndex);
        skipCount = 0;
        OpenFile(tracks[trackIndex]);
    }

    public void Next()
    {
        if (Mode != "playlist" || order.Count == 0) return;
        pos++;
        if (pos >= order.Count)
        {
            pos = 0;
            if (shuffle) BuildOrder();
        }
        OpenFile(tracks[order[pos]]);
    }

    public void Prev()
    {
        if (Mode != "playlist" || order.Count == 0) return;
        if (Position > 3 && reader != null) { reader.CurrentTime = TimeSpan.Zero; return; }
        pos = (pos - 1 + order.Count) % order.Count;
        OpenFile(tracks[order[pos]]);
    }

    public void Seek(double seconds)
    {
        if (Mode != "playlist" || reader == null) return;
        try { reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Length)); } catch { }
    }

    void OpenFile(string path)
    {
        CloseOutput();
        Loading = false;
        reconnectAt = null;
        if (!File.Exists(path))
        {
            Error = "Hiányzó fájl: " + Path.GetFileName(path);
            if (++skipCount < tracks.Count) Next(); else Stop();
            return;
        }
        try
        {
            Start(new AudioFileReader(path), Path.GetFileNameWithoutExtension(path));
            skipCount = 0;
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Nem lejátszható: {Path.GetFileName(path)} ({ex.Message})";
            if (++skipCount < tracks.Count) Next(); else Stop();
        }
    }

    public void PlayStream(string url, string name)
    {
        CloseOutput();
        Mode = "stream";
        streamUrl = url;
        streamName = name;
        Title = name;
        Loading = true;
        reconnectAt = null;
        int token = ++openToken;

        Task.Run(() =>
        {
            try
            {
                var r = new MediaFoundationReader(url);
                runOnUi(() =>
                {
                    if (token != openToken) { r.Dispose(); return; }
                    Loading = false;
                    Error = null;
                    Start(r, name);
                });
            }
            catch (Exception ex)
            {
                runOnUi(() =>
                {
                    if (token != openToken) return;
                    Loading = false;
                    Error = "Nem sikerült csatlakozni a rádióhoz: " + ex.Message;
                    Mode = "none";
                });
            }
        });
    }

    void Start(WaveStream r, string title)
    {
        reader = r;
        ISampleProvider sp = r is ISampleProvider isp ? isp : r.ToSampleProvider();
        volumeProvider = new VolumeSampleProvider(sp);
        if (fadeInOnStart) { fadeInOnStart = false; StartFadeIn(fadeInSeconds); }
        else ResetGain();
        output = new WaveOutEvent { DesiredLatency = Mode == "stream" ? 600 : 300 };
        output.Init(volumeProvider);
        output.Play();
        Title = title;
        stopRequested = false;
    }

    public void Pause()
    {
        if (IsPlaying) output!.Pause();
        ResetGain();
    }

    /// <summary>Folytatás, opcionálisan fokozatos hangosítással.</summary>
    public void Resume(double fadeSeconds = 0)
    {
        if (afterFade != null)
        {
            // még halkult, amikor véget ért a csengő → visszafordítjuk
            afterFade = null;
            gainTarget = 1f;
            gainStep = fadeSeconds > 0 ? (float)(0.25 / fadeSeconds) : 1f;
            return;
        }
        if (Mode == "stream" && streamUrl != null)
        {
            // élő adásnál a szünet után újracsatlakozunk, hogy ne késve szóljon
            if (!IsPlaying)
            {
                fadeInOnStart = fadeSeconds > 0;
                fadeInSeconds = fadeSeconds;
                PlayStream(streamUrl, streamName ?? "Rádió");
            }
            return;
        }
        if (IsPaused)
        {
            StartFadeIn(fadeSeconds);
            output!.Play();
        }
        else if (output == null && Mode == "playlist") PlayPlaylist();
    }

    public void Stop()
    {
        afterFade = null;
        gain = gainTarget = 1f;
        fadeInOnStart = false;
        openToken++;
        Loading = false;
        reconnectAt = null;
        CloseOutput();
        Mode = "none";
    }

    /// <summary>Az űrlap időzítője hívja: szám vége → következő, megszakadt adás → újracsatlakozás.</summary>
    public void Tick()
    {
        if (gain != gainTarget)
        {
            gain = gain < gainTarget ? Math.Min(gainTarget, gain + gainStep) : Math.Max(gainTarget, gain - gainStep);
            ApplyVolume();
            if (gain == gainTarget && afterFade != null)
            {
                var a = afterFade;
                afterFade = null;
                a();
                return;
            }
        }

        if (reconnectAt != null && DateTime.Now >= reconnectAt && streamUrl != null)
        {
            reconnectAt = null;
            PlayStream(streamUrl, streamName ?? "Rádió");
            return;
        }

        if (output != null && output.PlaybackState == PlaybackState.Stopped && !stopRequested && !Loading)
        {
            if (Mode == "playlist") Next();
            else if (Mode == "stream")
            {
                CloseOutput();
                Error = "Megszakadt az adás, újracsatlakozás…";
                reconnectAt = DateTime.Now.AddSeconds(5);
            }
        }
    }

    void CloseOutput()
    {
        stopRequested = true;
        try { output?.Stop(); } catch { }
        try { output?.Dispose(); } catch { }
        try { reader?.Dispose(); } catch { }
        output = null;
        reader = null;
        volumeProvider = null;
    }
}
