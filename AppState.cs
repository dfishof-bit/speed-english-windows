using System.IO;
using System.Text.Json;

namespace SpeedEnglish;

public sealed class AppState
{
    public const int WordsPerLevel = 100;
    public const int MaxLevel = 500;

    public int CurrentLevel { get; set; } = 1;
    public int HighestLevel { get; set; } = 1;
    public int TargetStreak { get; set; } = 10;
    public int ChoicesCount { get; set; } = 5;
    public bool TtsEnabled { get; set; } = true;
    public int TtsRate { get; set; } = 0; // -5..+5 (System.Speech range)
    public string VoiceName { get; set; } = "";
    public bool SoundEnabled { get; set; } = true;
    public int TotalCorrect { get; set; } = 0;
    public int TotalWrong { get; set; } = 0;

    private static string StateFilePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpeedEnglishHebrew");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "state.json");
        }
    }

    public int GetPoolSize(int totalWords)
    {
        if (CurrentLevel >= MaxLevel) return totalWords;
        return Math.Min(CurrentLevel * WordsPerLevel, totalWords);
    }

    public static AppState Load()
    {
        try
        {
            var path = StateFilePath;
            if (!File.Exists(path)) return new AppState();
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppState>(json);
            if (loaded is null) return new AppState();
            loaded.CurrentLevel = Math.Clamp(loaded.CurrentLevel, 1, MaxLevel);
            loaded.HighestLevel = Math.Clamp(Math.Max(loaded.HighestLevel, loaded.CurrentLevel), 1, MaxLevel);
            loaded.TargetStreak = loaded.TargetStreak == 15 ? 15 : 10;
            loaded.ChoicesCount = loaded.ChoicesCount == 4 ? 4 : 5;
            loaded.TtsRate = Math.Clamp(loaded.TtsRate, -5, 5);
            return loaded;
        }
        catch
        {
            return new AppState();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StateFilePath, json);
        }
        catch
        {
            // Ignore write failures
        }
    }

    public void ResetSettings()
    {
        TargetStreak = 10;
        ChoicesCount = 5;
        TtsEnabled = true;
        TtsRate = 0;
        SoundEnabled = true;
        Save();
    }
}
