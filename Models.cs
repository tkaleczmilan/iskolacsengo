namespace IskolaCsengo;

/// <summary>Egy csengetés a rendben.</summary>
public class Bell
{
    public string Time { get; set; } = "08:00";
    /// <summary>"be" = becsengő, "ki" = kicsengő, "egyeb" = egyéb jelzés</summary>
    public string Type { get; set; } = "be";
    public string? Label { get; set; }
    /// <summary>Egyedi hang ehhez a csengetéshez (üres = a típus alap hangja).</summary>
    public string? Sound { get; set; }
}

/// <summary>Csengetési rend (pl. normál, rövidített órák).</summary>
public class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Új csengetési rend";
    public List<Bell> Bells { get; set; } = new();
}

public class ExtraSound
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
}

/// <summary>Adott időpontban automatikusan lejátszott hang (pl. reggeli zene, Himnusz).</summary>
public class ScheduledSound
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Time { get; set; } = "07:45";
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public List<int> Days { get; set; } = new() { 1, 2, 3, 4, 5 };
    public bool Enabled { get; set; } = true;
}

public class RadioStream
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}

public class RadioSettings
{
    public List<string> Tracks { get; set; } = new();
    public List<RadioStream> Streams { get; set; } = new();
    public int Volume { get; set; } = 70;
    public bool Shuffle { get; set; }
    /// <summary>Szünetekben (kicsengő után, becsengőig) automatikusan szóljon.</summary>
    public bool AutoBreak { get; set; }
    /// <summary>"playlist" vagy "stream:N"</summary>
    public string AutoSource { get; set; } = "playlist";
    /// <summary>Ennyi másodperc alatt halkul el / hangosodik vissza a rádió csengetéskor (0 = azonnal).</summary>
    public double FadeSeconds { get; set; } = 2;
}

public class Settings
{
    public bool BellEnabled { get; set; } = true;
    /// <summary>Ennyi másodpercig szól a csengő (0 = a teljes hangfájl).</summary>
    public int BellDuration { get; set; } = 42;
    public int BellVolume { get; set; } = 100;
    public bool FadeOut { get; set; } = true;
    public string SoundIn { get; set; } = "becsengo.wav";
    public string SoundOut { get; set; } = "kicsengo.wav";
    public List<ExtraSound> ExtraSounds { get; set; } = new();
    public List<ScheduledSound> ScheduledSounds { get; set; } = new();
    public List<Profile> Profiles { get; set; } = new();
    public string ActiveProfileId { get; set; } = "";
    /// <summary>A hét napjai, amikor automatikusan csenget (0 = vasárnap … 6 = szombat).</summary>
    public List<int> ActiveDays { get; set; } = new() { 1, 2, 3, 4, 5 };
    public bool StartWithWindows { get; set; } = true;
    public RadioSettings Radio { get; set; } = new();
}
