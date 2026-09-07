using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Madoyaku.Models;
using Madoyaku.Services;

namespace Madoyaku;

public partial class MainWindow : Window
{
    private const double ExpandedMinHeight = 330;
    private const double CollapsedHeight = 42;
    private const double DefaultResultHeight = 220;
    private const int ResizeBorderThickness = 8;
    private const int HotkeyId = 0x484B;
    private const int WmHotkey = 0x0312;
    private const int WmNcHitTest = 0x0084;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
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
    private bool _isCollapsed;
    private double _expandedHeight;
    private double _expandedResultHeight = DefaultResultHeight;
    private IntPtr _windowHandle;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        _settings.WindowLayouts ??= [];

        string? storedApiKey = null;
        if (TestMode.IsEnabled)
        {
            _apiKey = string.Empty;
            Topmost = true;
            ShowActivated = true;
            CaptureSurface.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
        }
        else
        {
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
        }

        Left = _settings.WindowLeft;
        Top = _settings.WindowTop;
        Width = Math.Max(MinWidth, _settings.WindowWidth);
        Height = Math.Max(MinHeight, _settings.WindowHeight);
        _expandedHeight = Height;

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
            StatusText.Text = "ショートカット登録失敗（ボタンは使用可）";
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_credentialLoadError))
        {
            StatusText.Text = "Windows資格情報を読み込めませんでした";
            TranslationText.Text = _credentialLoadError;
        }
        else if (TestMode.IsEnabled)
        {
            StatusText.Text = "UI確認モード · API通信なし";
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
        DismissCaptureHint();

        if (_isTranslating)
        {
            return;
        }

        if (_isCollapsed)
        {
            RestoreWindow();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        }

        _isTranslating = true;
        TranslateButton.IsEnabled = false;
        StatusText.Text = "画面を取得しています…";
        SetSourceText(null);
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
            var png = TestMode.IsEnabled
                ? Array.Empty<byte>()
                : ScreenCaptureService.CapturePng(area);
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
            SetSourceText(result.SourceText);
            TranslationText.Text = string.IsNullOrWhiteSpace(result.Translation)
                ? "翻訳結果が空でした。"
                : result.Translation;
            TranslationText.ScrollToHome();
            StatusText.Text = $"完了 · 履歴 {_history.Count}件";
        }
        catch (Exception exception)
        {
            if (!IsVisible)
            {
                Show();
            }

            StatusText.Text = "翻訳できませんでした";
            SetSourceText(null);
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

    private void LayoutButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var saveItem = new MenuItem { Header = "+ 現在の配置を保存…" };
        saveItem.Click += (_, _) => SaveNewLayout();
        menu.Items.Add(saveItem);

        if (_settings.WindowLayouts.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var layout in _settings.WindowLayouts)
            {
                menu.Items.Add(CreateLayoutMenuItem(layout));
            }
        }

        LayoutButton.ContextMenu = menu;
        menu.PlacementTarget = LayoutButton;
        menu.IsOpen = true;
    }

    private MenuItem CreateLayoutMenuItem(WindowLayout layout)
    {
        var item = new MenuItem
        {
            Header = layout.Name,
            ToolTip = "クリックで呼び出し / 右クリックで管理"
        };
        item.Click += (_, _) => ApplyLayout(layout);

        var managementMenu = new ContextMenu();
        var overwriteItem = new MenuItem { Header = "現在の配置で上書き" };
        overwriteItem.Click += (_, _) => OverwriteLayout(layout);
        managementMenu.Items.Add(overwriteItem);

        var renameItem = new MenuItem { Header = "名前を変更…" };
        renameItem.Click += (_, _) => RenameLayout(layout);
        managementMenu.Items.Add(renameItem);

        var deleteItem = new MenuItem { Header = "削除" };
        deleteItem.Click += (_, _) => DeleteLayout(layout);
        managementMenu.Items.Add(deleteItem);

        item.ContextMenu = managementMenu;
        return item;
    }

    private void SaveNewLayout()
    {
        var suggestedName = $"配置{_settings.WindowLayouts.Count + 1}";
        var dialog = new LayoutNameWindow(suggestedName) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var existing = FindLayout(dialog.LayoutName);
        if (existing is not null &&
            MessageBox.Show($"「{dialog.LayoutName}」を現在の配置で上書きしますか？", "配置を保存",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        if (existing is null)
        {
            existing = new WindowLayout { Name = dialog.LayoutName };
            _settings.WindowLayouts.Add(existing);
        }

        CopyCurrentLayoutTo(existing);
        SaveLayouts("配置を保存しました");
    }

    private void OverwriteLayout(WindowLayout layout)
    {
        CopyCurrentLayoutTo(layout);
        SaveLayouts($"「{layout.Name}」を更新しました");
    }

    private void RenameLayout(WindowLayout layout)
    {
        var dialog = new LayoutNameWindow(layout.Name) { Owner = this };
        if (dialog.ShowDialog() != true || string.Equals(dialog.LayoutName, layout.Name, StringComparison.Ordinal))
        {
            return;
        }

        var existing = FindLayout(dialog.LayoutName);
        if (existing is not null)
        {
            MessageBox.Show("同じ名前の配置がすでにあります。別の名前を入力してください。", "配置名",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        layout.Name = dialog.LayoutName;
        SaveLayouts("配置名を変更しました");
    }

    private void DeleteLayout(WindowLayout layout)
    {
        if (MessageBox.Show($"「{layout.Name}」を削除しますか？", "配置を削除",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _settings.WindowLayouts.Remove(layout);
        SaveLayouts("配置を削除しました");
    }

    private WindowLayout? FindLayout(string name)
    {
        return _settings.WindowLayouts.FirstOrDefault(layout =>
            string.Equals(layout.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private void CopyCurrentLayoutTo(WindowLayout layout)
    {
        layout.Left = Left;
        layout.Top = _isCollapsed ? Top + CollapsedHeight - _expandedHeight : Top;
        layout.Width = Width;
        layout.Height = _isCollapsed ? _expandedHeight : Height;
        layout.ResultHeight = _isCollapsed
            ? _expandedResultHeight
            : Math.Max(ResultPanel.MinHeight, ResultRow.ActualHeight);
    }

    private void ApplyLayout(WindowLayout layout)
    {
        var width = Math.Max(MinWidth, layout.Width);
        var height = Math.Max(ExpandedMinHeight, layout.Height);
        var resultHeight = Math.Max(ResultPanel.MinHeight, layout.ResultHeight);
        var maxResultHeight = Math.Max(ResultPanel.MinHeight, height - 90);
        resultHeight = Math.Min(resultHeight, maxResultHeight);

        Width = width;
        Left = layout.Left;
        _expandedHeight = height;
        _expandedResultHeight = resultHeight;

        if (_isCollapsed)
        {
            Top = layout.Top + height - CollapsedHeight;
        }
        else
        {
            Top = layout.Top;
            Height = height;
            ResultRow.Height = new GridLength(resultHeight);
        }

        StatusText.Text = $"「{layout.Name}」を呼び出しました";
    }

    private void SaveLayouts(string status)
    {
        try
        {
            _settings.Save();
            StatusText.Text = status;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"配置を保存できませんでした: {exception.Message}";
        }
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        StatusText.Text = "翻訳履歴を消去しました";
        SetSourceText(null);
        TranslationText.Text = "翻訳結果がここに表示されます。";
    }

    private void SetSourceText(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            SourceText.Text = string.Empty;
            SourcePanel.Visibility = Visibility.Collapsed;
            return;
        }

        SourceText.Text = sourceText;
        SourcePanel.Visibility = Visibility.Visible;
        SourceText.ScrollToHome();
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCollapsed)
        {
            RestoreWindow();
        }
        else
        {
            CollapseWindow();
        }
    }

    private void CollapseWindow()
    {
        var bottom = Top + Height;
        _expandedHeight = Math.Max(ExpandedMinHeight, Height);
        _expandedResultHeight = Math.Max(ResultPanel.MinHeight, ResultRow.ActualHeight);
        _isCollapsed = true;
        CaptureSurface.Visibility = Visibility.Collapsed;
        ResultSplitter.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        CaptureRow.Height = new GridLength(0);
        SplitterRow.Height = new GridLength(0);
        ResultRow.Height = new GridLength(0);
        MinHeight = CollapsedHeight;
        MaxHeight = CollapsedHeight;
        Height = CollapsedHeight;
        Top = bottom - CollapsedHeight;
        CollapseButton.Content = "⌃";
        AutomationProperties.SetName(CollapseButton, "戻す");
        CollapseButton.ToolTip = "上側の翻訳領域と結果を戻す";
    }

    private void RestoreWindow()
    {
        var bottom = Top + Height;
        var expandedHeight = Math.Max(ExpandedMinHeight, _expandedHeight);
        _isCollapsed = false;
        MaxHeight = double.PositiveInfinity;
        MinHeight = ExpandedMinHeight;
        CaptureRow.Height = new GridLength(1, GridUnitType.Star);
        SplitterRow.Height = new GridLength(8);
        ResultRow.Height = new GridLength(_expandedResultHeight);
        CaptureSurface.Visibility = Visibility.Visible;
        ResultSplitter.Visibility = Visibility.Visible;
        ResultPanel.Visibility = Visibility.Visible;
        Height = expandedHeight;
        Top = bottom - expandedHeight;
        CollapseButton.Content = "⌄";
        AutomationProperties.SetName(CollapseButton, "たたむ");
        CollapseButton.ToolTip = "上側の翻訳領域をたたむ";
    }

    private void CaptureSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DismissCaptureHint();

        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void DismissCaptureHint()
    {
        CaptureHintPanel.Visibility = Visibility.Collapsed;
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
        _settings.WindowTop = _isCollapsed
            ? Top + CollapsedHeight - _expandedHeight
            : Top;
        _settings.WindowWidth = Width;
        _settings.WindowHeight = _isCollapsed ? _expandedHeight : Height;

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
        if (message == WmNcHitTest)
        {
            var hit = GetResizeHitTest(hwnd, lParam);
            if (hit != 0)
            {
                handled = true;
                return new IntPtr(hit);
            }
        }

        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _ = TranslateCurrentAreaAsync();
        }

        return IntPtr.Zero;
    }

    private int GetResizeHitTest(IntPtr hwnd, IntPtr lParam)
    {
        if (!GetWindowRect(hwnd, out var windowRect))
        {
            return 0;
        }

        var screenX = unchecked((short)(long)lParam);
        var screenY = unchecked((short)((long)lParam >> 16));
        var border = Math.Max(1, (int)Math.Round(ResizeBorderThickness * GetDpiForWindow(hwnd) / 96d));
        var onLeft = screenX >= windowRect.Left && screenX < windowRect.Left + border;
        var onRight = screenX <= windowRect.Right && screenX > windowRect.Right - border;

        if (_isCollapsed)
        {
            return onLeft ? HtLeft : onRight ? HtRight : 0;
        }

        var onTop = screenY >= windowRect.Top && screenY < windowRect.Top + border;
        var onBottom = screenY <= windowRect.Bottom && screenY > windowRect.Bottom - border;

        if (onTop && onLeft)
        {
            return HtTopLeft;
        }

        if (onTop && onRight)
        {
            return HtTopRight;
        }

        if (onBottom && onLeft)
        {
            return HtBottomLeft;
        }

        if (onBottom && onRight)
        {
            return HtBottomRight;
        }

        if (onLeft)
        {
            return HtLeft;
        }

        if (onRight)
        {
            return HtRight;
        }

        if (onTop)
        {
            return HtTop;
        }

        return onBottom ? HtBottom : 0;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out WindowRect windowRect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
