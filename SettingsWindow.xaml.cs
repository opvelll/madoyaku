using System.Windows;
using System.Windows.Controls;
using HonnyakuKun.Models;
using HonnyakuKun.Services;

namespace HonnyakuKun;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings, string currentApiKey, bool apiKeyStored)
    {
        InitializeComponent();
        _settings = settings;

        ApiKeyBox.Password = currentApiKey;
        RememberApiKeyBox.IsChecked = apiKeyStored;
        ModelBox.Text = settings.Model;
        TargetLanguageBox.Text = settings.TargetLanguage;
        HistoryLimitBox.Text = settings.HistoryLimit.ToString();
        BasePromptBox.Text = settings.BasePrompt;
        GameContextBox.Text = settings.GameContext;
    }

    public string ApiKey => ApiKeyBox.Password;

    public bool RememberApiKey => RememberApiKeyBox.IsChecked == true &&
                                  !string.IsNullOrWhiteSpace(ApiKeyBox.Password);

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ModelBox.Text))
        {
            ValidationText.Text = "モデル名を入力してください。";
            return;
        }

        if (!int.TryParse(HistoryLimitBox.Text, out var historyLimit) || historyLimit is < 0 or > 50)
        {
            ValidationText.Text = "履歴数は0～50で入力してください。";
            return;
        }

        _settings.Model = ModelBox.Text.Trim();
        _settings.TargetLanguage = string.IsNullOrWhiteSpace(TargetLanguageBox.Text)
            ? "日本語"
            : TargetLanguageBox.Text.Trim();
        _settings.HistoryLimit = historyLimit;
        _settings.BasePrompt = BasePromptBox.Text.Trim();
        _settings.GameContext = GameContextBox.Text.Trim();

        try
        {
            _settings.Save();

            if (RememberApiKey)
            {
                WindowsCredentialStore.WriteApiKey(ApiKeyBox.Password);
            }
            else
            {
                WindowsCredentialStore.DeleteApiKey();
            }

            DialogResult = true;
        }
        catch (Exception exception)
        {
            ValidationText.Text = $"設定を保存できませんでした: {exception.Message}";
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
