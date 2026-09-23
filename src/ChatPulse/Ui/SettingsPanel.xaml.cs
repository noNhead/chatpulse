using System.Windows;
using System.Windows.Controls;
using ChatPulse.Core;
using ChatPulse.Stats;

namespace ChatPulse.Ui;

public partial class SettingsPanel : UserControl
{
    private AppState? _state;

    /// <summary>Guards the handlers while <see cref="Refresh"/> pushes state into the controls.</summary>
    private bool _syncing;

    private string _storage = string.Empty;

    public SettingsPanel()
    {
        InitializeComponent();
        OpacitySlider.Style = (Style)FindResource("ValueSlider");
    }

    public void Bind(AppState state)
    {
        _state = state;
        Refresh();
    }

    /// <summary>
    /// Live readout of what the retention settings actually cost, so the caps above are a
    /// decision rather than a guess. Pushed in rather than bound: the panel is usually hidden and
    /// should not re-render four times a second behind the scrim.
    /// </summary>
    public void ShowStorageUsage(string text)
    {
        _storage = text;
        if (IsVisible) StorageText.Text = text;
    }

    public void Refresh()
    {
        if (_state is null) return;

        _syncing = true;
        try
        {
            DataContext = _state.Settings;
            OpacitySlider.Value = _state.Settings.OverlayOpacity;
            OpacityValue.Text = $"{Math.Round(_state.Settings.OverlayOpacity * 100)}%";
            CompactToggle.IsChecked = _state.Settings.OverlayCompact;
            TopmostToggle.IsChecked = _state.Settings.OverlayAlwaysOnTop;
            DemoButton.Content = _state.IsDemo ? "Restart demo" : "Start demo";
            StorageText.Text = _storage;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void Apply(Func<Config.Settings, Config.Settings> transform)
    {
        if (_syncing || _state is null) return;
        _state.UpdateSettings(transform);
        Refresh();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        if (_state is not null) _state.SettingsOpen = false;
    }

    private void OnUnitChanged(object sender, RoutedEventArgs e)
    {
        if (TagEnum<RateUnit>(sender) is { } unit) Apply(s => s with { RateUnit = unit });
    }

    private void OnWindowChanged(object sender, RoutedEventArgs e)
    {
        if (TagInt(sender) is { } value) Apply(s => s with { RateWindowSec = value });
    }

    private void OnOriginalityWindowChanged(object sender, RoutedEventArgs e)
    {
        if (TagInt(sender) is { } value) Apply(s => s with { OriginalityWindowSec = value });
    }

    private void OnRetentionChanged(object sender, RoutedEventArgs e)
    {
        if (TagInt(sender) is { } value) Apply(s => s with { RetentionHours = value });
    }

    private void OnRefreshChanged(object sender, RoutedEventArgs e)
    {
        if (TagInt(sender) is { } value) Apply(s => s with { RefreshMs = value });
    }

    private void OnCapChanged(object sender, RoutedEventArgs e)
    {
        if (TagInt(sender) is { } value) Apply(s => s with { MaxEntriesPerUser = value });
    }

    private void OnLogChanged(object sender, RoutedEventArgs e)
    {
        if (TagInt(sender) is { } value) Apply(s => s with { MessageLogPerUser = value });
    }

    private void OnTopChanged(object sender, RoutedEventArgs e)
    {
        if (TagInt(sender) is { } value) Apply(s => s with { OverlayTopN = value });
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || _state is null) return;
        OpacityValue.Text = $"{Math.Round(e.NewValue * 100)}%";
        _state.UpdateSettings(s => s with { OverlayOpacity = e.NewValue });
    }

    private void OnCompactChanged(object sender, RoutedEventArgs e) =>
        Apply(s => s with { OverlayCompact = CompactToggle.IsChecked == true });

    private void OnTopmostChanged(object sender, RoutedEventArgs e) =>
        Apply(s => s with { OverlayAlwaysOnTop = TopmostToggle.IsChecked == true });

    private void OnStartDemo(object sender, RoutedEventArgs e)
    {
        _state?.StartDemo();
        Refresh();
    }

    private void OnReset(object sender, RoutedEventArgs e) => _state?.ResetStats();

    private static T? TagEnum<T>(object sender) where T : struct, Enum =>
        sender is FrameworkElement { Tag: string tag } && Enum.TryParse<T>(tag, out var value) ? value : null;

    private static int? TagInt(object sender) =>
        sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out var value) ? value : null;
}
