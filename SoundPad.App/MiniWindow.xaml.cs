using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SoundPad.App.Models;

namespace SoundPad.App;

public partial class MiniWindow : Window
{
    private readonly MainWindow                  _owner;
    private readonly Dictionary<Guid, Action<bool>> _padControls = new();
    private          bool                        _isReallyClosing;
    private          Deck?                       _currentDeck;

    internal MiniWindow(MainWindow owner)
    {
        _owner = owner;
        InitializeComponent();

        _owner.PlaybackStateChanged += OnPlaybackStateChanged;
        _owner.ActiveDeckChanged    += OnActiveDeckChanged;
    }

    // ── Public surface called by MainWindow ───────────────────────────────────

    internal void ApplySettings(AppSettings settings)
    {
        Topmost = settings.MiniAlwaysOnTop;
        SyncPinButton();

        if (settings.MiniWindowWidth  is > 0 and double w) Width  = w;
        if (settings.MiniWindowHeight is > 0 and double h) Height = h;
        bool hadPos = false;
        if (settings.MiniWindowLeft is double l) { Left = l; hadPos = true; }
        if (settings.MiniWindowTop  is double t) { Top  = t; hadPos = true; }

        if (hadPos)
        {
            double vl = SystemParameters.VirtualScreenLeft;
            double vt = SystemParameters.VirtualScreenTop;
            double vr = vl + SystemParameters.VirtualScreenWidth;
            double vb = vt + SystemParameters.VirtualScreenHeight;
            Left = Math.Clamp(Left, vl, Math.Max(vl, vr - 100));
            Top  = Math.Clamp(Top,  vt, Math.Max(vt, vb - 50));
        }
    }

    internal void SavePositionTo(AppSettings settings)
    {
        if (WindowState != WindowState.Normal) return;
        settings.MiniWindowLeft   = Left;
        settings.MiniWindowTop    = Top;
        settings.MiniWindowWidth  = Width;
        settings.MiniWindowHeight = Height;
    }

    internal void ForceClose()
    {
        _isReallyClosing = true;
        Close();
    }

    internal void InitializeDeck(Deck deck)
    {
        _currentDeck = deck;
        RebuildPads();
    }

    // ── Event handlers from MainWindow ────────────────────────────────────────

    private void OnPlaybackStateChanged(Guid soundId, bool active)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnPlaybackStateChanged(soundId, active));
            return;
        }
        if (_padControls.TryGetValue(soundId, out var setActive))
            setActive(active);
    }

    private void OnActiveDeckChanged(Deck deck)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnActiveDeckChanged(deck));
            return;
        }
        _currentDeck = deck;
        DeckNameText.Text = deck.Name;
        RebuildPads();
    }

    // ── Pad construction ──────────────────────────────────────────────────────

    private void RebuildPads()
    {
        _padControls.Clear();
        MiniPadPanel.Children.Clear();

        if (_currentDeck is null) return;

        DeckNameText.Text = _currentDeck.Name;

        foreach (var item in _currentDeck.Sounds)
        {
            var card = BuildMiniPad(item);
            MiniPadPanel.Children.Add(card);
        }

        // Sync active state for any sounds already playing when we rebuild.
        var active = _owner.GetActivePlaybackIds();
        foreach (var id in active)
        {
            if (_padControls.TryGetValue(id, out var setActive))
                setActive(true);
        }
    }

    private Border BuildMiniPad(SoundItem item)
    {
        var capturedItem = item;

        // ── Active-state visuals ──────────────────────────────────────────────
        var accentBrush  = new SolidColorBrush(MainWindow._fallbackAccent) { Opacity = 0.30 };
        var normalBg     = MainWindow.GetPadBackground(item.PadColor);
        var activeBg     = accentBrush;

        var playingDot = new TextBlock
        {
            Text              = "▶",
            FontSize          = 9,
            Foreground        = new SolidColorBrush(MainWindow._fallbackAccent),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin            = new Thickness(0, 3, 4, 0),
            Visibility        = Visibility.Hidden
        };

        var nameText = new TextBlock
        {
            Text                = item.DisplayName,
            FontSize            = 11,
            FontWeight          = FontWeights.SemiBold,
            Foreground          = (Brush)FindResource("TextFillColorPrimaryBrush"),
            TextWrapping        = TextWrapping.Wrap,
            TextAlignment       = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Margin              = new Thickness(4)
        };

        var overlay = new Border
        {
            Background  = activeBg,
            CornerRadius = new CornerRadius(6),
            Visibility  = Visibility.Hidden
        };

        var card = new Border
        {
            Width        = 110,
            Height       = 90,
            Margin       = new Thickness(3),
            CornerRadius = new CornerRadius(6),
            Background   = normalBg,
            BorderBrush  = (Brush)FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            Cursor       = Cursors.Hand
        };

        var grid = new Grid();
        grid.Children.Add(overlay);
        grid.Children.Add(nameText);
        grid.Children.Add(playingDot);
        card.Child = grid;

        // Register the active-state toggle callback.
        _padControls[item.Id] = active =>
        {
            overlay.Visibility    = active ? Visibility.Visible : Visibility.Hidden;
            playingDot.Visibility = active ? Visibility.Visible : Visibility.Hidden;
        };

        card.MouseLeftButtonUp += (_, _) =>
        {
            if (_owner.IsActivePlayback(capturedItem.Id))
                _owner.StopSoundById(capturedItem.Id);
            else
                _owner.PlayLibraryItem(capturedItem);
        };

        return card;
    }

    // ── Header / window chrome ────────────────────────────────────────────────

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void SyncPinButton()
    {
        PinButton.Appearance = Topmost
            ? Wpf.Ui.Controls.ControlAppearance.Primary
            : Wpf.Ui.Controls.ControlAppearance.Secondary;
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _owner.SaveMiniPositionFrom(this);
        Hide();
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        SyncPinButton();
        _owner.SaveMiniAlwaysOnTop(Topmost);
    }

    private void StopAll_Click(object sender, RoutedEventArgs e) => _owner.StopAllSounds();

    // ── Closing guard ─────────────────────────────────────────────────────────

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isReallyClosing)
        {
            // X button — hide instead of closing so the app keeps running.
            e.Cancel = true;
            _owner.SaveMiniPositionFrom(this);
            Hide();
            return;
        }

        // Real close triggered by MainWindow during app exit.
        _owner.PlaybackStateChanged -= OnPlaybackStateChanged;
        _owner.ActiveDeckChanged    -= OnActiveDeckChanged;
        base.OnClosing(e);
    }
}
