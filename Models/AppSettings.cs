using System.IO;
using System.Text.Json;

namespace HonnyakuKun.Models;

public sealed class AppSettings
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HonnyakuKun");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public string Model { get; set; } = "gpt-5.6-luna";

    public string TargetLanguage { get; set; } = "日本語";

    public string BasePrompt { get; set; } =
        "あなたはゲーム翻訳者です。画像内の台詞や文章を読み取り、話者の口調、感情、固有名詞を保ちながら自然な日本語に翻訳してください。UI上の無関係な文字は無視してください。";

    public string GameContext { get; set; } = string.Empty;

    public int HistoryLimit { get; set; } = 8;

    public double WindowLeft { get; set; } = 120;

    public double WindowTop { get; set; } = 120;

    public double WindowWidth { get; set; } = 820;

    public double WindowHeight { get; set; } = 520;

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
