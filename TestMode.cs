using System.IO;

namespace Madoyaku;

public static class TestMode
{
    public static bool IsEnabled { get; private set; }

    public static string? DataDirectory { get; private set; }

    public static string SourceText => "これはUI確認用の長い原文です。折り返し、スクロール、過去の翻訳の文脈表示を確認できます。";

    public static string TranslationText => "これはUI確認用の翻訳結果です。設定保存、処理中表示、結果領域の読みやすさを確認できます。";

    public static void Initialize(string[] args)
    {
        IsEnabled = false;
        DataDirectory = null;

        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--ui-test", StringComparison.OrdinalIgnoreCase))
            {
                IsEnabled = true;
            }
            else if (string.Equals(args[index], "--test-data-dir", StringComparison.OrdinalIgnoreCase) &&
                     index + 1 < args.Length)
            {
                DataDirectory = Path.GetFullPath(args[++index]);
            }
        }

        if (IsEnabled && string.IsNullOrWhiteSpace(DataDirectory))
        {
            throw new ArgumentException("--ui-test には --test-data-dir が必要です。");
        }

        if (IsEnabled)
        {
            Directory.CreateDirectory(DataDirectory!);
        }
    }
}
