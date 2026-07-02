using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SoundPad.App.Audio;

namespace SoundPad.App.Dialogs;

public partial class ClipSettingsDialog : Wpf.Ui.Controls.FluentWindow
{
    public float  ResultVolume  { get; private set; }
    public double ResultFadeIn  { get; private set; }
    public double ResultFadeOut { get; private set; }

    private readonly double _clipDuration;

    public ClipSettingsDialog(
        Window owner,
        MixClip clip,
        string sourceName,
        int trackNumber,
        double durationSeconds)
    {
        Owner = owner;
        InitializeComponent();

        _clipDuration   = durationSeconds;
        ResultVolume    = clip.Volume;
        ResultFadeIn    = clip.FadeInSeconds;
        ResultFadeOut   = clip.FadeOutSeconds;

        SourceLabel.Text   = sourceName;
        TrackLabel.Text    = $"Track {trackNumber}";
        StartLabel.Text    = FormatTime(clip.StartOffsetSeconds);
        DurationLabel.Text = FormatTime(durationSeconds);

        VolumeSlider.Value = Math.Round(clip.Volume * 100.0, 0);
        FadeInBox.Text     = FormatFade(clip.FadeInSeconds);
        FadeOutBox.Text    = FormatFade(clip.FadeOutSeconds);
    }

    // ── Volume ────────────────────────────────────────────────────────────────

    private void VolumeSlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeLabel is null) return;
        VolumeLabel.Text = $"{VolumeSlider.Value:0}%";
        UpdateDbLabel();
    }

    private void UpdateDbLabel()
    {
        if (DbLabel is null) return;
        double volume = VolumeSlider.Value / 100.0;
        DbLabel.Text = volume <= 0.0
            ? "-∞ dB"
            : $"{20.0 * Math.Log10(volume):+0.0;-0.0;0.0} dB";
    }

    private void ApplyDbChange(double deltaDb)
    {
        double volume = VolumeSlider.Value / 100.0;
        if (volume <= 0.0 && deltaDb > 0) volume = 0.01;
        double newVolume = Math.Clamp(volume * Math.Pow(10.0, deltaDb / 20.0), 0.0, 2.0);
        VolumeSlider.Value = Math.Round(newVolume * 100.0, 0);
    }

    private void DbMinus3_Click(object sender, RoutedEventArgs e) => ApplyDbChange(-3.0);
    private void DbMinus1_Click(object sender, RoutedEventArgs e) => ApplyDbChange(-1.0);
    private void DbPlus1_Click(object sender, RoutedEventArgs e)  => ApplyDbChange(+1.0);
    private void DbPlus3_Click(object sender, RoutedEventArgs e)  => ApplyDbChange(+3.0);

    // ── Fade In / Fade Out ────────────────────────────────────────────────────

    private void FadeIn01_Click(object sender, RoutedEventArgs e)     => FadeInBox.Text  = FormatFade(0.1);
    private void FadeIn025_Click(object sender, RoutedEventArgs e)    => FadeInBox.Text  = FormatFade(0.25);
    private void FadeIn05_Click(object sender, RoutedEventArgs e)     => FadeInBox.Text  = FormatFade(0.5);
    private void FadeInClear_Click(object sender, RoutedEventArgs e)  => FadeInBox.Text  = FormatFade(0.0);

    private void FadeOut01_Click(object sender, RoutedEventArgs e)    => FadeOutBox.Text = FormatFade(0.1);
    private void FadeOut025_Click(object sender, RoutedEventArgs e)   => FadeOutBox.Text = FormatFade(0.25);
    private void FadeOut05_Click(object sender, RoutedEventArgs e)    => FadeOutBox.Text = FormatFade(0.5);
    private void FadeOutClear_Click(object sender, RoutedEventArgs e) => FadeOutBox.Text = FormatFade(0.0);

    // Parses a fade TextBox; clamps to [0, clipDuration / 2] so fades never overlap.
    private double ParseFade(TextBox box)
    {
        if (!double.TryParse(box.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
            v = 0.0;
        double maxFade = _clipDuration > 0 ? _clipDuration / 2.0 : 0.0;
        return Math.Clamp(v, 0.0, maxFade);
    }

    // ── Apply / Cancel ────────────────────────────────────────────────────────

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ResultVolume  = (float)(VolumeSlider.Value / 100.0);
        ResultFadeIn  = ParseFade(FadeInBox);
        ResultFadeOut = ParseFade(FadeOutBox);
        DialogResult  = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatFade(double seconds) =>
        seconds.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatTime(double seconds)
    {
        if (seconds < 60) return $"{seconds:0.00} s";
        int    m = (int)(seconds / 60);
        double s = seconds - m * 60.0;
        return $"{m}:{s:00.00}";
    }
}
