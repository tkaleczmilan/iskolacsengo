using System.Text.Encodings.Web;
using System.Text.Json;

namespace IskolaCsengo;

/// <summary>Beállítások mentése/betöltése és a hangfájlok kezelése.</summary>
public static class Store
{
    public static readonly string BaseDir = AppContext.BaseDirectory;
    /// <summary>
    /// Ha a program mappája írható (pl. Asztal, pendrive), az "adatok" almappát használja (hordozható mód).
    /// Ha nem (pl. Program Files), akkor a felhasználó AppData\Local\IskolaCsengo mappáját.
    /// </summary>
    public static readonly string DataDir = ResolveDataDir();

    static string ResolveDataDir()
    {
        var portable = Path.Combine(BaseDir, "adatok");
        if (Directory.Exists(portable) ? IsWritable(portable) : IsWritable(BaseDir)) return portable;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IskolaCsengo");
    }

    static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".irasteszt-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }
    public static readonly string SoundDir = Path.Combine(DataDir, "hangok");
    public static readonly string DefaultSoundDir = Path.Combine(BaseDir, "alap_hangok");
    public static readonly string SettingsPath = Path.Combine(DataDir, "beallitasok.json");
    public static readonly string LogPath = Path.Combine(DataDir, "naplo.txt");

    public static readonly string[] AudioExtensions = { ".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac", ".ogg" };

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Settings Load()
    {
        Directory.CreateDirectory(SoundDir);

        // Alap hangok bemásolása az adatok\hangok mappába, ha még nincsenek ott.
        if (Directory.Exists(DefaultSoundDir))
        {
            foreach (var f in Directory.GetFiles(DefaultSoundDir))
            {
                var dest = Path.Combine(SoundDir, Path.GetFileName(f));
                if (!File.Exists(dest)) File.Copy(f, dest);
            }
        }

        Settings? s = null;
        if (File.Exists(SettingsPath))
        {
            try
            {
                s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath), Json);
            }
            catch (Exception ex)
            {
                var backup = SettingsPath + ".hibas-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try { File.Copy(SettingsPath, backup, true); } catch { }
                Log("Hibás beállításfájl, alapértékek betöltve: " + ex.Message);
            }
        }

        s ??= CreateDefaults();
        Normalize(s);
        Save(s);
        return s;
    }

    public static void Normalize(Settings s)
    {
        s.Profiles ??= new();
        if (s.Profiles.Count == 0) s.Profiles.Add(DefaultProfile());
        if (!s.Profiles.Any(p => p.Id == s.ActiveProfileId)) s.ActiveProfileId = s.Profiles[0].Id;
        foreach (var p in s.Profiles)
        {
            p.Bells ??= new();
            p.RadioSkipBreaks ??= new();
            p.Bells = p.Bells.Where(b => TimeOnly.TryParse(b.Time, out _))
                             .Select(b => { b.Time = TimeOnly.Parse(b.Time).ToString("HH:mm"); return b; })
                             .OrderBy(b => b.Time).ToList();
        }
        s.ActiveDays ??= new();
        s.ExtraSounds ??= new();
        s.ScheduledSounds ??= new();
        foreach (var x in s.ScheduledSounds)
        {
            x.Days ??= new();
            x.Time = TimeOnly.TryParse(x.Time, out var t) ? t.ToString("HH:mm") : "07:45";
        }
        s.ScheduledSounds = s.ScheduledSounds.OrderBy(x => x.Time).ToList();
        s.Radio ??= new();
        s.Radio.Tracks ??= new();
        s.Radio.Streams ??= new();
        s.BellDuration = Math.Clamp(s.BellDuration, 0, 600);
        s.BellVolume = Math.Clamp(s.BellVolume, 0, 100);
        s.Radio.Volume = Math.Clamp(s.Radio.Volume, 0, 100);
        s.Radio.FadeSeconds = Math.Clamp(s.Radio.FadeSeconds, 0, 10);
    }

    public static void Save(Settings s)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
            File.Move(tmp, SettingsPath, true);
        }
        catch (Exception ex)
        {
            Log("Mentési hiba: " + ex.Message);
        }
    }

    /// <summary>Hangfájl teljes útvonala (a relatív nevek az adatok\hangok mappára vonatkoznak).</summary>
    public static string ResolveSound(string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return "";
        return Path.IsPathRooted(file) ? file : Path.Combine(SoundDir, file);
    }

    /// <summary>Bemásol egy hangfájlt az adatok\hangok mappába, és visszaadja az új nevét.</summary>
    public static string ImportSound(string source)
    {
        Directory.CreateDirectory(SoundDir);
        var name = Path.GetFileName(source);
        var dest = Path.Combine(SoundDir, name);

        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            return name;

        int i = 2;
        while (File.Exists(dest) && new FileInfo(dest).Length != new FileInfo(source).Length)
        {
            name = $"{Path.GetFileNameWithoutExtension(source)} ({i++}){Path.GetExtension(source)}";
            dest = Path.Combine(SoundDir, name);
        }
        if (!File.Exists(dest)) File.Copy(source, dest);
        return name;
    }

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch { }
    }

    static Settings CreateDefaults()
    {
        var p = DefaultProfile();
        return new Settings { Profiles = { p }, ActiveProfileId = p.Id };
    }

    /// <summary>Alapértelmezett csengetési rend új telepítéshez (a programban szabadon átírható).</summary>
    static Profile DefaultProfile() => new()
    {
        Id = "normal",
        Name = "Normál csengetési rend",
        Bells = new()
        {
            new() { Time = "08:00", Type = "be", Label = "1. óra" },
            new() { Time = "08:45", Type = "ki" },
            new() { Time = "08:55", Type = "be", Label = "2. óra" },
            new() { Time = "09:40", Type = "ki" },
            new() { Time = "10:00", Type = "be", Label = "3. óra" },
            new() { Time = "10:45", Type = "ki" },
            new() { Time = "10:55", Type = "be", Label = "4. óra" },
            new() { Time = "11:40", Type = "ki" },
            new() { Time = "11:50", Type = "be", Label = "5. óra" },
            new() { Time = "12:35", Type = "ki" },
            new() { Time = "12:45", Type = "be", Label = "6. óra" },
            new() { Time = "13:30", Type = "ki" },
            new() { Time = "13:50", Type = "be", Label = "7. óra" },
            new() { Time = "14:35", Type = "ki" },
            new() { Time = "14:45", Type = "be", Label = "8. óra" },
            new() { Time = "15:30", Type = "ki" },
        }
    };
}
