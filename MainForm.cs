using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace IskolaCsengo;

public sealed class MainForm : Form
{
    Settings settings;
    readonly BellPlayer bell = new();
    readonly RadioPlayer radio;
    readonly WebView2 web;
    NotifyIcon tray = null!;
    readonly System.Windows.Forms.Timer timer;
    readonly bool startHidden;

    bool webReady;
    bool reallyExit;
    bool trayHintShown;
    int lastSecond = -1;
    string lastRingKey = "";

    // rádió + csengő együttműködés
    bool radioPausedForBell;
    string currentBellType = "";
    bool radioStartedByAuto;
    string? autoStartedBreakKey;
    string? manualStoppedBreakKey;

    // ütemezett hangok: ma már lejátszottak
    readonly HashSet<string> firedScheduled = new();
    DateTime firedDate = DateTime.Today;

    // A gép ne aludjon el, amíg a csengő fut (a kijelző kikapcsolhat).
    [DllImport("kernel32.dll")]
    static extern uint SetThreadExecutionState(uint esFlags);
    const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;

    static readonly string[] DayNames = { "vasárnap", "hétfő", "kedd", "szerda", "csütörtök", "péntek", "szombat" };

    public MainForm(bool startHidden)
    {
        this.startHidden = startHidden;
        settings = Store.Load();

        Text = "Iskolacsengő";
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(14, 16, 22);
        try
        {
            var ico = Path.Combine(Store.BaseDir, "app.ico");
            Icon = File.Exists(ico) ? new Icon(ico) : Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);
        }
        catch { }

        radio = new RadioPlayer(a => { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); });
        radio.Volume = settings.Radio.Volume;
        radio.SetTracks(settings.Radio.Tracks, settings.Radio.Shuffle);
        bell.Finished += OnBellFinished;

        web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
        Controls.Add(web);

        tray = BuildTray();
        ApplyAutostart();
        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);

        timer = new System.Windows.Forms.Timer { Interval = 250 };
        timer.Tick += (_, _) => OnTick();
        timer.Start();

        Load += async (_, _) => await InitWebAsync();
        Shown += (_, _) => { if (this.startHidden) Hide(); };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) HideToTray(); };
        FormClosing += OnFormClosing;

        Store.Log("Program elindult.");
    }

    // ───────────────────────────── Felület ─────────────────────────────

    async Task InitWebAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Store.DataDir, "webview"));
            await web.EnsureCoreWebView2Async(env);
            var core = web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = true;
            core.SetVirtualHostNameToFolderMapping("csengo.local", Path.Combine(Store.BaseDir, "ui"),
                CoreWebView2HostResourceAccessKind.Allow);
            core.WebMessageReceived += (_, e) => HandleMessage(e.WebMessageAsJson);
            core.NavigationCompleted += (_, _) => { webReady = true; PushSettings(); PushState(); };
            core.Navigate("https://csengo.local/index.html");
        }
        catch (Exception ex)
        {
            Store.Log("WebView2 hiba: " + ex.Message);
            Controls.Remove(web);
            Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "A grafikus felület nem tudott elindulni (hiányzik a Microsoft Edge WebView2 Runtime).\n\n" +
                       "A csengő ettől függetlenül működik a háttérben.\n" +
                       "Telepítsd a WebView2 Runtime-ot: https://developer.microsoft.com/microsoft-edge/webview2/"
            });
        }
    }

    void Post(object message)
    {
        if (!webReady) return;
        try { web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, Store.Json)); } catch { }
    }

    void PushSettings() => Post(new { type = "settings", settings });
    void Toast(string text, string kind = "ok") => Post(new { type = "toast", text, kind });

    void PushState()
    {
        if (!webReady || !Visible) return;
        var now = DateTime.Now;
        var hm = now.ToString("HH:mm");
        var bells = ActiveProfile().Bells;
        var next = bells.FirstOrDefault(b => string.CompareOrdinal(b.Time, hm) > 0);
        double? countdown = next != null ? (DateTime.Today + TimeOnly.Parse(next.Time).ToTimeSpan() - now).TotalSeconds : null;

        Post(new
        {
            type = "state",
            time = now.ToString("HH:mm:ss"),
            date = now.ToString("yyyy. MMMM d.", new System.Globalization.CultureInfo("hu-HU")),
            dayName = DayNames[(int)now.DayOfWeek],
            dayActive = IsDayActive(now),
            dow = (int)now.DayOfWeek,
            enabled = settings.BellEnabled,
            next = next == null ? null : new { next.Time, next.Type, next.Label },
            countdown,
            bell = new { playing = bell.IsPlaying, title = bell.Title, elapsed = bell.Elapsed, total = bell.Total },
            radio = new
            {
                mode = radio.Mode,
                playing = radio.IsPlaying,
                paused = radio.IsPaused,
                loading = radio.Loading,
                title = radio.Title,
                index = radio.CurrentTrackIndex,
                position = radio.Position,
                length = radio.Length,
                error = radio.Error,
                auto = radioStartedByAuto,
                pausedForBell = radioPausedForBell,
            },
        });
    }

    // ───────────────────────────── Időzítő ─────────────────────────────

    Profile ActiveProfile() =>
        settings.Profiles.FirstOrDefault(p => p.Id == settings.ActiveProfileId) ?? settings.Profiles[0];

    bool IsDayActive(DateTime d) => settings.ActiveDays.Contains((int)d.DayOfWeek);

    void OnTick()
    {
        var now = DateTime.Now;
        var hm = now.ToString("HH:mm");
        var key = now.ToString("yyyy-MM-dd ") + hm;

        bool bellThisMinute = false;
        if (settings.BellEnabled && IsDayActive(now))
        {
            var b = ActiveProfile().Bells.FirstOrDefault(x => x.Time == hm);
            bellThisMinute = b != null;
            if (b != null && lastRingKey != key)
            {
                lastRingKey = key;
                Ring(b.Type, b.Sound, b.Label, automatic: true);
            }
        }

        if (settings.BellEnabled) CheckScheduled(now, hm, key, bellThisMinute);

        bell.Tick();
        radio.Tick();
        HandleAutoBreak(now);

        if (now.Second != lastSecond)
        {
            lastSecond = now.Second;
            PushState();
            UpdateTrayText(now);
        }
    }

    void CheckScheduled(DateTime now, string hm, string key, bool bellThisMinute)
    {
        if (firedDate != now.Date) { firedDate = now.Date; firedScheduled.Clear(); }
        foreach (var s in settings.ScheduledSounds)
        {
            if (!s.Enabled || s.Time != hm || !s.Days.Contains((int)now.DayOfWeek)) continue;
            if (!firedScheduled.Add(key + "|" + s.Id)) continue;
            if (bellThisMinute)
            {
                Store.Log($"Ütemezett hang kihagyva (ugyanebben a percben csengetés van): {s.Name}");
                continue;
            }
            PlayScheduled(s, automatic: true);
        }
    }

    void PlayScheduled(ScheduledSound s, bool automatic)
    {
        var path = Store.ResolveSound(s.File);
        if (!File.Exists(path))
        {
            Store.Log("Ütemezett hang fájlja hiányzik: " + path);
            Toast("Hiányzik a fájl: " + Path.GetFileName(path), "error");
            return;
        }
        Ring("utemezett", s.File, string.IsNullOrWhiteSpace(s.Name) ? Path.GetFileNameWithoutExtension(path) : s.Name,
             automatic, fullLength: true);
    }

    /// <summary>Előző és következő be/ki csengetés a mai napon.</summary>
    (Bell? prev, Bell? next) Neighbors(DateTime now)
    {
        var hm = now.ToString("HH:mm");
        var list = ActiveProfile().Bells.Where(b => b.Type is "be" or "ki").ToList();
        return (list.LastOrDefault(b => string.CompareOrdinal(b.Time, hm) <= 0),
                list.FirstOrDefault(b => string.CompareOrdinal(b.Time, hm) > 0));
    }

    string? CurrentBreakKey(DateTime now)
    {
        if (!settings.BellEnabled || !IsDayActive(now)) return null;
        var (prev, next) = Neighbors(now);
        return prev?.Type == "ki" && next?.Type == "be" ? now.ToString("yyyy-MM-dd ") + prev.Time : null;
    }

    void HandleAutoBreak(DateTime now)
    {
        if (!settings.Radio.AutoBreak) return;
        var breakKey = CurrentBreakKey(now);

        if (breakKey != null)
        {
            if (!bell.IsPlaying && !radio.IsActive && autoStartedBreakKey != breakKey && manualStoppedBreakKey != breakKey)
            {
                autoStartedBreakKey = breakKey;
                if (StartRadioSource(settings.Radio.AutoSource))
                {
                    radioStartedByAuto = true;
                    Store.Log("Iskolarádió automatikusan elindult (szünet).");
                }
            }
        }
        else if (radioStartedByAuto && radio.IsActive && !bell.IsPlaying && !radioPausedForBell && !radio.FadingOut)
        {
            radio.FadeOut(settings.Radio.FadeSeconds, stop: true);
            radioStartedByAuto = false;
        }
    }

    bool StartRadioSource(string source)
    {
        if (source.StartsWith("stream:") && int.TryParse(source[7..], out var i) && i >= 0 && i < settings.Radio.Streams.Count)
        {
            var s = settings.Radio.Streams[i];
            radio.PlayStream(s.Url, s.Name);
            return true;
        }
        if (settings.Radio.Tracks.Count == 0) return false;
        radio.PlayPlaylist();
        return true;
    }

    // ───────────────────────────── Csengetés ─────────────────────────────

    void Ring(string type, string? sound, string? label, bool automatic, bool fullLength = false)
    {
        var file = !string.IsNullOrWhiteSpace(sound) ? sound : type == "ki" ? settings.SoundOut : settings.SoundIn;
        var path = Store.ResolveSound(file);

        if (!File.Exists(path))
        {
            Store.Log("Hiányzó hangfájl: " + path + " – alap hang használva.");
            var baseName = type == "ki" ? "kicsengo" : "becsengo";
            path = new[] { ".wav", ".mp3" }.Select(e => Path.Combine(Store.DefaultSoundDir, baseName + e))
                                          .FirstOrDefault(File.Exists) ?? "";
            if (!File.Exists(path)) { Toast("Nem található a hangfájl!", "error"); return; }
        }

        string title;
        if (type == "utemezett") title = label ?? "Ütemezett hang";
        else
        {
            title = type switch { "be" => "Becsengő", "ki" => "Kicsengő", _ => "Jelzés" };
            if (!string.IsNullOrWhiteSpace(label)) title += " – " + label;
        }

        if (radio.Loading)
        {
            radio.Stop();
            radioPausedForBell = true;
        }
        else if (radio.IsPlaying && !radio.FadingOut)
        {
            // a rádió fokozatosan elhalkul, közben már szól a csengő
            radio.FadeOut(settings.Radio.FadeSeconds, stop: false);
            radioPausedForBell = true;
        }

        try
        {
            currentBellType = type;
            bell.Play(path, fullLength ? 0 : settings.BellDuration, settings.BellVolume, settings.FadeOut, title);
            Store.Log($"{(automatic ? "Automatikus" : "Kézi")} csengetés: {title} ({Path.GetFileName(path)})");
        }
        catch (Exception ex)
        {
            Store.Log("Lejátszási hiba: " + ex.Message);
            Toast("Nem sikerült lejátszani: " + ex.Message, "error");
            OnBellFinished();
        }
        PushState();
    }

    void OnBellFinished()
    {
        if (radioPausedForBell)
        {
            radioPausedForBell = false;
            if (currentBellType == "be")
            {
                radio.Stop();          // becsengő után kezdődik az óra → rádió vége
                radioStartedByAuto = false;
            }
            else radio.Resume(settings.Radio.FadeSeconds);
        }
        PushState();
    }

    // ───────────────────────────── Üzenetek a felületről ─────────────────────────────

    void HandleMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var m = doc.RootElement;
            var cmd = m.GetProperty("cmd").GetString();
            string Str(string name) => m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            int Int(string name) => m.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : -1;
            double Dbl(string name) => m.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? d : 0;

            switch (cmd)
            {
                case "ready": PushSettings(); PushState(); break;

                case "saveSettings":
                    var incoming = JsonSerializer.Deserialize<Settings>(m.GetProperty("settings").GetRawText(), Store.Json);
                    if (incoming != null) ApplySettings(incoming, push: false);
                    break;

                case "setEnabled":
                    settings.BellEnabled = m.GetProperty("value").GetBoolean();
                    Store.Save(settings);
                    Store.Log("Automatikus csengetés " + (settings.BellEnabled ? "BEKAPCSOLVA" : "KIKAPCSOLVA"));
                    if (!settings.BellEnabled) bell.Stop();
                    PushSettings(); PushState(); UpdateTrayMenu();
                    break;

                case "ring": Ring(Str("kind"), null, null, automatic: false); break;
                case "ringExtra":
                    var ex = settings.ExtraSounds.FirstOrDefault(x => x.Id == Str("id"));
                    if (ex != null) Ring("egyeb", ex.File, ex.Name, automatic: false, fullLength: true);
                    break;
                case "preview":
                    Ring(Str("kind") is "be" or "ki" ? Str("kind") : "egyeb", Str("file"), "próba", automatic: false,
                         fullLength: Str("kind") == "egyeb");
                    break;
                case "stopBell": bell.Stop(); break;
                case "playScheduled":
                    var sc = settings.ScheduledSounds.FirstOrDefault(x => x.Id == Str("id"));
                    if (sc != null) PlayScheduled(sc, automatic: false);
                    break;

                case "pickSound": PickSound(Str("target"), Str("profileId"), Int("index")); break;

                case "radioPlay":
                    manualStoppedBreakKey = null;
                    if (Int("index") >= 0) radio.PlayTrack(Int("index"));
                    else if (radio.IsPaused) radio.Resume();
                    else if (!StartRadioSource(settings.Radio.AutoSource) && !StartRadioSource("playlist"))
                        Toast("Adj hozzá zenéket vagy egy online rádiót!", "error");
                    break;
                case "radioStream":
                    var si = Int("index");
                    if (si >= 0 && si < settings.Radio.Streams.Count)
                        radio.PlayStream(settings.Radio.Streams[si].Url, settings.Radio.Streams[si].Name);
                    break;
                case "radioPause": radio.Pause(); break;
                case "radioResume": radio.Resume(); break;
                case "radioStop":
                    radio.Stop();
                    radioStartedByAuto = false;
                    radioPausedForBell = false;
                    manualStoppedBreakKey = CurrentBreakKey(DateTime.Now);
                    break;
                case "radioNext": radio.Next(); break;
                case "radioPrev": radio.Prev(); break;
                case "radioSeek": radio.Seek(Dbl("value")); break;
                case "radioVolume":
                    radio.Volume = Int("value");
                    settings.Radio.Volume = radio.Volume;
                    Store.Save(settings);
                    break;
                case "radioAddFiles": RadioAddFiles(); break;
                case "radioAddFolder": RadioAddFolder(); break;

                case "openDataFolder":
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Store.DataDir}\"") { UseShellExecute = true });
                    break;
                case "openLog":
                    if (File.Exists(Store.LogPath))
                        Process.Start(new ProcessStartInfo(Store.LogPath) { UseShellExecute = true });
                    break;
                case "hide": HideToTray(); break;
            }
            PushState();
        }
        catch (Exception e)
        {
            Store.Log("Üzenetkezelési hiba: " + e.Message);
            Toast("Hiba: " + e.Message, "error");
        }
    }

    void ApplySettings(Settings s, bool push)
    {
        Store.Normalize(s);
        settings = s;
        radio.SetTracks(settings.Radio.Tracks, settings.Radio.Shuffle);
        radio.Volume = settings.Radio.Volume;
        Store.Save(settings);
        ApplyAutostart();
        UpdateTrayMenu();
        if (push) PushSettings();
    }

    void PickSound(string target, string profileId, int index)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Válassz hangfájlt",
            Filter = "Hangfájlok|*.mp3;*.wav;*.wma;*.m4a;*.aac;*.flac|Minden fájl|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var name = Store.ImportSound(dlg.FileName);
        switch (target)
        {
            case "in": settings.SoundIn = name; break;
            case "out": settings.SoundOut = name; break;
            case "extra":
                settings.ExtraSounds.Add(new ExtraSound { Name = Path.GetFileNameWithoutExtension(name), File = name });
                break;
            case "scheduled":
                if (index >= 0 && index < settings.ScheduledSounds.Count)
                    settings.ScheduledSounds[index].File = name;
                else
                {
                    var t = DateTime.Now.AddMinutes(5);
                    settings.ScheduledSounds.Add(new ScheduledSound
                    {
                        Name = Path.GetFileNameWithoutExtension(name),
                        File = name,
                        Time = t.ToString("HH:mm"),
                    });
                    settings.ScheduledSounds = settings.ScheduledSounds.OrderBy(x => x.Time).ToList();
                }
                break;
            case "bell":
                var p = settings.Profiles.FirstOrDefault(x => x.Id == profileId);
                if (p != null && index >= 0 && index < p.Bells.Count) p.Bells[index].Sound = name;
                break;
        }
        Store.Save(settings);
        PushSettings();
        Toast("Hang beállítva: " + name);
    }

    void RadioAddFiles()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Zenék hozzáadása az iskolarádióhoz",
            Filter = "Hangfájlok|*.mp3;*.wav;*.wma;*.m4a;*.aac;*.flac|Minden fájl|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        AddTracks(dlg.FileNames);
    }

    void RadioAddFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Zenék mappája", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var files = Directory.EnumerateFiles(dlg.SelectedPath, "*.*", SearchOption.AllDirectories)
            .Where(f => Store.AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase);
        AddTracks(files);
    }

    void AddTracks(IEnumerable<string> files)
    {
        int added = 0;
        foreach (var f in files)
            if (!settings.Radio.Tracks.Contains(f, StringComparer.OrdinalIgnoreCase)) { settings.Radio.Tracks.Add(f); added++; }
        radio.SetTracks(settings.Radio.Tracks, settings.Radio.Shuffle);
        Store.Save(settings);
        PushSettings();
        Toast(added > 0 ? $"{added} zene hozzáadva" : "Nem volt új zene");
    }

    // ───────────────────────────── Tálca, indítás ─────────────────────────────

    NotifyIcon BuildTray()
    {
        var t = new NotifyIcon { Icon = Icon ?? SystemIcons.Application, Text = "Iskolacsengő", Visible = true };
        t.DoubleClick += (_, _) => ShowMain();
        t.ContextMenuStrip = new ContextMenuStrip();
        t.Text = "Iskolacsengő";
        tray = t; // a menühöz kell
        UpdateTrayMenu();
        return t;
    }

    void UpdateTrayMenu()
    {
        if (tray?.ContextMenuStrip == null) return;
        var menu = tray.ContextMenuStrip;
        menu.Items.Clear();
        menu.Items.Add("Megnyitás", null, (_, _) => ShowMain()).Font = new Font(menu.Font, FontStyle.Bold);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Becsengetés most", null, (_, _) => Ring("be", null, null, false));
        menu.Items.Add("Kicsengetés most", null, (_, _) => Ring("ki", null, null, false));
        menu.Items.Add("Csengő leállítása", null, (_, _) => bell.Stop());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settings.BellEnabled ? "Automatikus csengetés kikapcsolása" : "Automatikus csengetés bekapcsolása", null, (_, _) =>
        {
            settings.BellEnabled = !settings.BellEnabled;
            Store.Save(settings);
            UpdateTrayMenu(); PushSettings(); PushState();
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Kilépés", null, (_, _) => ExitApp());
    }

    void UpdateTrayText(DateTime now)
    {
        var hm = now.ToString("HH:mm");
        var next = ActiveProfile().Bells.FirstOrDefault(b => string.CompareOrdinal(b.Time, hm) > 0);
        var text = !settings.BellEnabled ? "Iskolacsengő – KIKAPCSOLVA"
                 : next != null ? $"Iskolacsengő – következő: {next.Time}"
                 : "Iskolacsengő – ma nincs több csengetés";
        if (tray.Text != text) tray.Text = text.Length > 63 ? text[..63] : text;
    }

    void ShowMain()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        PushState();
    }

    void HideToTray()
    {
        Hide();
        if (!trayHintShown)
        {
            trayHintShown = true;
            tray.ShowBalloonTip(3000, "Iskolacsengő", "A csengő a háttérben tovább működik. Dupla kattintással újra megnyithatod.", ToolTipIcon.Info);
        }
    }

    void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        Store.Log("Program leállt.");
        timer.Stop();
        bell.Stop();
        radio.Stop();
        tray.Visible = false;
        SetThreadExecutionState(ES_CONTINUOUS);
    }

    void ExitApp()
    {
        var r = MessageBox.Show("Ha kilépsz, a csengő NEM fog szólni.\nBiztosan kilépsz?", "Iskolacsengő",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (r != DialogResult.Yes) return;
        reallyExit = true;
        Close();
    }

    void ApplyAutostart()
    {
        try
        {
            using var rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (rk == null) return;
            // Ugyanazt a nevet használjuk, mint a régi program → a régi automatikus indítást lecseréli.
            if (settings.StartWithWindows) rk.SetValue("IskolaCsengo", $"\"{Application.ExecutablePath}\" --tray");
            else if (rk.GetValue("IskolaCsengo") != null) rk.DeleteValue("IskolaCsengo");
        }
        catch (Exception ex) { Store.Log("Autostart hiba: " + ex.Message); }
    }
}
