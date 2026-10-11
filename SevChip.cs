using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SharpSaw;

/// <summary>Palette et pictogrammes de gravité, partagés par les chips et la liste.</summary>
public static class Sev
{
    public static string Label(int rank) => rank switch { 4 => "Critique", 3 => "Haute", 2 => "Moyenne", 1 => "Faible", _ => "Info" };

    // Segoe Fluent Icons : un pictogramme distinct par niveau.
    public static string Glyph(int rank) => rank switch
    {
        4 => "", // pastille d'erreur
        3 => "", // avertissement
        2 => "", // drapeau
        1 => "", // information
        _ => "", // aide / neutre
    };

    public static Windows.UI.Color Color(int rank) => rank switch
    {
        4 => ColorHelper.FromArgb(255, 164, 14, 28),   // rouge foncé
        3 => ColorHelper.FromArgb(255, 216, 90, 10),   // orange
        2 => ColorHelper.FromArgb(255, 176, 120, 0),   // ambre
        1 => ColorHelper.FromArgb(255, 30, 120, 205),  // bleu
        _ => ColorHelper.FromArgb(255, 108, 108, 108), // gris
    };

    public static Brush Brush(int rank) => new SolidColorBrush(Color(rank));
}

/// <summary>Chip de filtre de gravité : l'état affiché est toujours celui du filtre réel (IsOn).</summary>
public sealed class SevChip : INotifyPropertyChanged
{
    public SevChip(int rank) { Rank = rank; }

    public int Rank { get; }
    public string Glyph => Sev.Glyph(Rank);

    private int _count;
    public int Count { get => _count; set { _count = value; Raise(nameof(Text)); } }

    private bool _isOn = true;
    public bool IsOn
    {
        get => _isOn;
        set { _isOn = value; Raise(nameof(IsOn)); Raise(nameof(ChipBg)); Raise(nameof(IconFg)); Raise(nameof(OnVisibility)); Raise(nameof(OffVisibility)); }
    }

    public string Text => $"{Sev.Label(Rank)} {Count}";
    public Brush ChipBg => IsOn ? Sev.Brush(Rank) : new SolidColorBrush(Colors.Transparent);
    public Brush IconFg => IsOn ? new SolidColorBrush(Colors.White) : Sev.Brush(Rank);
    public Visibility OnVisibility => IsOn ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OffVisibility => IsOn ? Visibility.Collapsed : Visibility.Visible;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
