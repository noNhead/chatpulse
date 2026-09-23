using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ChatPulse.Core;
using ChatPulse.Config;
using ChatPulse.Stats;

namespace ChatPulse.Ui;

/// <summary>
/// The always-on-top "who is talking" window a streamer parks on their second monitor.
/// Frameless and translucent, so it sits over a game capture without a chrome box around it.
/// </summary>
public partial class OverlayWindow : Window
{
    private const double HeaderHeight = 38;
    private const double FooterHeight = 28;
    private const double RowHeight = 34;
    private const double CompactRowHeight = 24;

    /// <summary>Outer transparent margin (2 x 8) plus the list's own vertical padding.</summary>
    private const double ListPadding = 28;

    private readonly AppState _state;
    private readonly ObservableCollection<OverlayRowVm> _rows = [];

    public OverlayWindow(AppState state)
    {
        _state = state;
        InitializeComponent();

        RowsHost.DataContext = _rows;
        Topmost = state.Settings.OverlayAlwaysOnTop;

        // Size to the row count it was opened with, so there is no dead space under the list.
        Height = HeaderHeight + FooterHeight + ListPadding + RowHeight * state.Settings.OverlayTopN;

        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 24;
        Top = work.Top + (work.Height - Height) / 2;
    }

    public void Apply(Snapshot snapshot, Settings settings)
    {
        Topmost = settings.OverlayAlwaysOnTop;
        Card.Opacity = settings.OverlayOpacity;

        PinnedGlyph.Visibility = settings.OverlayAlwaysOnTop ? Visibility.Visible : Visibility.Collapsed;
        UnpinnedGlyph.Visibility = settings.OverlayAlwaysOnTop ? Visibility.Collapsed : Visibility.Visible;
        PinButton.ToolTip = settings.OverlayAlwaysOnTop
            ? "Pinned above other windows — click to let it go behind"
            : "Free to go behind other windows — click to pin on top";

        Dot.Active = snapshot.Connection.IsLive;
        // Pulses on the snapshot tick — a few repaints a second rather than one per vsync.
        Dot.Step();
        ChannelText.Text = string.IsNullOrEmpty(snapshot.Channel) ? "not connected" : "#" + snapshot.Channel;
        UnitText.Text = snapshot.Config.RateUnit.Suffix();

        GlobalRate.Text = $"{Format.Rate(snapshot.Global.ChatRate)} {snapshot.Config.RateUnit.Suffix()}";
        ActiveText.Text = $"{snapshot.Global.ActiveChatters} active";
        TotalText.Text = $"{Format.Count(snapshot.Global.TotalMessages)} msgs";

        var source = snapshot.Rows.Take(settings.OverlayTopN).ToList();
        var maxRate = source.Count > 0 ? source.Max(r => r.Rate) : 0;
        if (maxRate <= 0) maxRate = 1;

        while (_rows.Count > source.Count) _rows.RemoveAt(_rows.Count - 1);
        while (_rows.Count < source.Count) _rows.Add(new OverlayRowVm());

        for (var i = 0; i < source.Count; i++)
        {
            _rows[i].Update(source[i], i, maxRate, settings.OverlayCompact);
        }
    }

    private void OnDragCard(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnResize(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }

    private void OnToggleCompact(object sender, RoutedEventArgs e) =>
        _state.UpdateSettings(s => s with { OverlayCompact = !s.OverlayCompact });

    /// <summary>
    /// Same setting as the "Always on top" switch in Settings, reachable in one click from the
    /// overlay itself — which is where you want it when a game or OBS needs the foreground.
    /// </summary>
    private void OnTogglePin(object sender, RoutedEventArgs e)
    {
        _state.UpdateSettings(s => s with { OverlayAlwaysOnTop = !s.OverlayAlwaysOnTop });

        // Unpinning alone leaves the window wherever it was in the z-order; push it down now so
        // the click does something visible instead of waiting for the next window to be raised.
        if (!Topmost) SendToBack();
    }

    /// <summary>Drops the window to the bottom of the z-order without activating it.</summary>
    private void SendToBack()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, NoMove | NoSize | NoActivate);
    }

    private static readonly IntPtr HwndBottom = new(1);
    private const uint NoSize = 0x0001;
    private const uint NoMove = 0x0002;
    private const uint NoActivate = 0x0010;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private void OnClose(object sender, RoutedEventArgs e) => _state.OverlayVisible = false;
}

public sealed class OverlayRowVm : Observable
{
    private string _rank = string.Empty;
    private Brush _rankBrush = BrushCache.Gray;
    private FontWeight _rankWeight = FontWeights.Normal;
    private string _displayName = string.Empty;
    private Brush _userBrush = BrushCache.Gray;
    private string _rateText = "0";
    private double _barFraction;
    private Brush _rankTint = System.Windows.Media.Brushes.Transparent;
    private double _rowHeight = 34;
    private bool _showBar = true;

    public string Rank { get => _rank; private set => Set(ref _rank, value); }
    public Brush RankBrush { get => _rankBrush; private set => Set(ref _rankBrush, value); }
    public FontWeight RankWeight { get => _rankWeight; private set => Set(ref _rankWeight, value); }
    public string DisplayName { get => _displayName; private set => Set(ref _displayName, value); }
    public Brush UserBrush { get => _userBrush; private set => Set(ref _userBrush, value); }
    public string RateText { get => _rateText; private set => Set(ref _rateText, value); }
    public double BarFraction { get => _barFraction; private set => Set(ref _barFraction, value); }
    public Brush RankTint { get => _rankTint; private set => Set(ref _rankTint, value); }
    public double RowHeight { get => _rowHeight; private set => Set(ref _rowHeight, value); }
    public bool ShowBar { get => _showBar; private set => Set(ref _showBar, value); }

    public void Update(ChatterRow row, int index, double maxRate, bool compact)
    {
        Rank = (index + 1).ToString();

        var rankColor = Format.RankColor(index);
        RankBrush = BrushCache.Frozen(rankColor);
        RankWeight = index < 3 ? FontWeights.Bold : FontWeights.Normal;
        RankTint = index < 3
            ? BrushCache.Frozen(Color.FromArgb(0x0E, rankColor.R, rankColor.G, rankColor.B))
            : System.Windows.Media.Brushes.Transparent;

        DisplayName = row.DisplayName;
        UserBrush = BrushCache.Frozen(Format.UserColor(row.Login, row.ColorArgb));
        RateText = Format.Rate(row.Rate);

        // Quantised to half a percent. An unrounded fraction changes on every tick and repaints
        // the bar for a sub-pixel difference nobody can see — and this window is layered, so WPF
        // renders it on the CPU and pushes the whole surface through UpdateLayeredWindow.
        BarFraction = Math.Round(row.Rate / maxRate * 200) / 200.0;
        RowHeight = compact ? 24 : 34;
        ShowBar = !compact;
    }
}
