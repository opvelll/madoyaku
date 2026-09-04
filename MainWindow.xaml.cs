using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using HonnyakuKun.Models;
using HonnyakuKun.Services;

namespace HonnyakuKun;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0x484B;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkT = 0x54;

    private readonly AppSettings _settings;
    private readonly OpenAiVisionClient _openAiClient = new();
    private readonly List<TranslationHistoryItem> _history = [];
    private string _apiKey;
    private bool _apiKeyStored;
    private string? _credentialLoadError;
    private bool _isTranslating;
    private IntPtr _windowHandle;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();

        string? storedApiKey = null;
        try
        {
            storedApiKey = WindowsCredentialStore.ReadApiKey();
            _apiKeyStored = !string.IsNullOrWhiteSpace(storedApiKey);
        }
        catch (Exception exception)
        {
            _credentialLoadError = exception.Message;
        }

        var environmentApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        _apiKey = !string.IsNullOrWhiteSpace(environmentApiKey)
            ? environmentApiKey
            : storedApiKey ?? string.Empty;

        Left = _settings.WindowLeft;
        Top = _settings.WindowTop;
        Width = Math.Max(MinWidth, _settings.WindowWidth);
        Height = Math.Max(MinHeight, _settings.WindowHeight);

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowHandle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(_windowHandle);
        source?.AddHook(WindowMessageHook);

        if (!RegisterHotKey(_windowHandle, HotkeyId, ModControl | ModShift, VkT))
        {
            HotkeyStatusText.Text = "  ショートカット登録失敗（ボタンは使用可）";
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_credentialLoadError))
        {
            StatusText.Text = "Windows資格情報を読み込めませんでした";
            TranslationText.Text = _credentialLoadError;
        }
        else if (string.IsNullOrWhiteSpace(_apiKey))
        {
            StatusText.Text = "APIキー未設定 — 設定を開いて入力してください";
        }
        else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
        {
            StatusText.Text = "準備完了 · APIキー: 環境変数";
        }
        else if (_apiKeyStored)
        {
            StatusText.Text = "準備完了 · APIキー: Windows資格情報";
        }
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        await TranslateCurrentAreaAsync();
    }

    private async Task TranslateCurrentAreaAsync()
    {
        if (_isTranslating)
        {
            return;
        }

        _isTranslating = true;
        TranslateButton.IsEnabled = false;
        StatusText.Text = "画面を取得しています…";
        TranslationText.Text = "翻訳中…";

        try
        {
            var topLeft = CaptureSurface.PointToScreen(new Point(0, 0));
            var bottomRight = CaptureSurface.PointToScreen(
                new Point(CaptureSurface.ActualWidth, CaptureSurface.ActualHeight));
            var area = new Int32Rect(
                (int)Math.Round(topLeft.X),
                (int)Math.Round(topLeft.Y),
                Math.Max(1, (int)Math.Round(bottomRight.X - topLeft.X)),
                Math.Max(1, (int)Math.Round(bottomRight.Y - topLeft.Y)));

            Hide();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(100);
            var png = ScreenCaptureService.CapturePng(area);
            Show();

            StatusText.Text = $"{_settings.Model} に送信しています…";
            var relevantHistory = _settings.HistoryLimit <= 0
                ? Array.Empty<TranslationHistoryItem>()
                : _history.TakeLast(_settings.HistoryLimit).ToArray();

            var result = await _openAiClient.TranslateAsync(
                png,
                _settings,
                relevantHistory,
                _apiKey);

            _history.Add(result);
            TranslationText.Text = string.IsNullOrWhiteSpace(result.Translation)
                ? "翻訳結果が空でした。"
                : result.Translation;
            StatusText.Text = string.IsNullOrWhiteSpace(result.SourceText)
                ? $"完了 · 履歴 {_history.Count}件"
                : $"原文: {result.SourceText}";
        }
        catch (Exception exception)
        {
            if (!IsVisible)
            {
                Show();
            }

            StatusText.Text = "翻訳できませんでした";
            TranslationText.Text = exception.Message;
        }
        finally
        {
            _isTranslating = false;
            TranslateButton.IsEnabled = true;
            Topmost = true;
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings, _apiKey, _apiKeyStored) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _apiKey = dialog.ApiKey;
            _apiKeyStored = dialog.RememberApiKey;
            var keyLocation = _apiKeyStored ? "Windows資格情報" : "この起動中のみ";
            StatusText.Text = $"設定を保存しました · APIキー: {keyLocation} · {_settings.Model}";
        }
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        StatusText.Text = "翻訳履歴を消去しました";
        TranslationText.Text = "翻訳結果がここに表示されます。";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is not System.Windows.Controls.Button)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, HotkeyId);
        }

        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.WindowWidth = Width;
        _settings.WindowHeight = Height;

        try
        {
            _settings.Save();
        }
        catch
        {
            // Closing should never be blocked by an unavailable settings directory.
        }
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _ = TranslateCurrentAreaAsync();
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
