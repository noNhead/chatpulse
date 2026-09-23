using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ChatPulse.Core;
using ChatPulse.Stats;

namespace ChatPulse.Ui;

public partial class MainWindow : Window
{
    private readonly AppState _state;
    private readonly DashboardVm _vm;
    private OverlayWindow? _overlay;

    public MainWindow(AppState state)
    {
        _state = state;
        _vm = new DashboardVm(state);

        InitializeComponent();

        DataContext = _vm;
        SettingsPanelHost.Bind(state);

        Loaded += (_, _) =>
        {
            Icon = AppIcon.CreateBitmap(64);
            // Seeded once. The state was built before this window existed, so anything already
            // set there — the saved channel, --overlay — never raised a PropertyChanged we could
            // have heard.
            ChannelBox.Text = _state.ChannelInput;
            SyncOverlay();
            Apply();
        };

        _state.PropertyChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppState.Snapshot):
                Apply();
                UpdateOverlay();
                break;

            // These only affect the main window. Driving the overlay from them meant every
            // keystroke in the filter box re-rendered a layered window, which is expensive and
            // can flicker.
            case nameof(AppState.LastLog):
            case nameof(AppState.Search):
            case nameof(AppState.SelectedLogin):
                Apply();
                break;

            case nameof(AppState.Settings):
                UpdateOverlay();
                SettingsPanelHost.Refresh();
                break;

            case nameof(AppState.OverlayVisible):
                SyncOverlay();
                break;

            case nameof(AppState.SettingsOpen):
                SettingsScrimVisibility();
                break;

            case nameof(AppState.ChannelInput):
                // Only when something other than typing changed it — starting the demo feed, or
                // restoring the saved channel at startup. Typing round-trips to an equal value,
                // so this is a no-op and the caret never moves.
                if (ChannelBox.Text != _state.ChannelInput) ChannelBox.Text = _state.ChannelInput;
                break;
        }
    }

    /// <summary>
    /// Rebuilds the bound view models from the latest immutable snapshot. Runs on the UI thread
    /// at the configured refresh rate — four times a second by default, not once per frame.
    /// </summary>
    private void Apply()
    {
        _vm.Apply(_state.Snapshot, _state.Search, _state.SelectedLogin, _state.LastLog);
        _vm.SettingsOpen = _state.SettingsOpen;
        SettingsPanelHost.ShowStorageUsage(_vm.StorageUsage);
    }

    private void UpdateOverlay() => _overlay?.Apply(_state.Snapshot, _state.Settings);

    private void SettingsScrimVisibility()
    {
        _vm.SettingsOpen = _state.SettingsOpen;
        SettingsPanelHost.Refresh();
    }

    // --- overlay ------------------------------------------------------------------------

    private void SyncOverlay()
    {
        if (_state.OverlayVisible)
        {
            if (_overlay is null)
            {
                _overlay = new OverlayWindow(_state) { Owner = null };
                _overlay.Closed += (_, _) =>
                {
                    _overlay = null;
                    if (_state.OverlayVisible) _state.OverlayVisible = false;
                };
                _overlay.Show();
                _overlay.Apply(_state.Snapshot, _state.Settings);
            }
        }
        else
        {
            _overlay?.Close();
            _overlay = null;
        }

        OverlayButton.Background = _state.OverlayVisible
            ? new SolidColorBrush(Color.FromArgb(0x38, 0xA9, 0x70, 0xFF))
            : System.Windows.Media.Brushes.Transparent;
    }

    // --- input handlers -----------------------------------------------------------------

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _state.Search = SearchBox.Text;
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnChannelTextChanged(object sender, TextChangedEventArgs e) =>
        _state.ChannelInput = ChannelBox.Text;

    /// <summary>
    /// The box is pre-filled with the previously watched channel, so focusing it selects that
    /// text: typing a new name replaces it instead of prepending to it.
    /// </summary>
    private void OnChannelFocused(object sender, KeyboardFocusChangedEventArgs e) => ChannelBox.SelectAll();

    private void OnChannelMouseDown(object sender, MouseButtonEventArgs e)
    {
        // A click would otherwise place the caret and clear the selection made above.
        if (ChannelBox.IsKeyboardFocusWithin) return;
        e.Handled = true;
        ChannelBox.Focus();
    }

    private void OnChannelKeyDown(object sender, KeyEventArgs e)
    {
        // No text mangling here: KeyDown fires before the character reaches the box, so anything
        // read back is one keystroke stale. Connect trims and normalises the name itself.
        if (e.Key == Key.Enter) _state.Connect(ChannelBox.Text);
    }

    private void OnToggleConnection(object sender, RoutedEventArgs e) => _state.ToggleConnection();

    private void OnToggleOverlay(object sender, RoutedEventArgs e) =>
        _state.OverlayVisible = !_state.OverlayVisible;

    private void OnOpenSettings(object sender, RoutedEventArgs e) => _state.SettingsOpen = true;

    private void OnScrimClicked(object sender, MouseButtonEventArgs e)
    {
        // Only a click on the scrim itself dismisses; clicks inside the card bubble up here too.
        if (ReferenceEquals(e.OriginalSource, SettingsScrim)) _state.SettingsOpen = false;
    }

    private void OnCloseDetail(object sender, RoutedEventArgs e) => _state.SelectedLogin = null;

    private void OnSortChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse<SortKey>(tag, out var key))
        {
            _state.UpdateSettings(s => s with { SortKey = key });
        }
    }

    private void OnRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnderMouse(e.OriginalSource as DependencyObject) is { } vm) _state.Select(vm.Login);
    }

    private static ChatterRowVm? ItemUnderMouse(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: ChatterRowVm vm }) return vm;
            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _state.PropertyChanged -= OnStateChanged;
        _overlay?.Close();
        base.OnClosed(e);
    }
}
