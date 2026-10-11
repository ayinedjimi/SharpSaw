using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SharpSaw;

public sealed class RuleItem
{
    public string Title { get; init; } = "";
    public string Path { get; init; } = "";
    public string Source { get; init; } = "";   // Sigma | Chainsaw | Personnelle
    public string Level { get; init; } = "";
    public bool Custom { get; init; }
    public string FileName => System.IO.Path.GetFileName(Path);

    private int Rank => Level.ToLowerInvariant() switch { "critical" => 4, "high" => 3, "medium" => 2, "low" => 1, _ => 0 };
    public string Glyph => Rank switch { 4 => "", 3 => "", 2 => "", 1 => "", _ => "" };
    public Brush Brush => new SolidColorBrush(Rank switch
    {
        4 => ColorHelper.FromArgb(255, 192, 28, 40),
        3 => ColorHelper.FromArgb(255, 224, 96, 20),
        2 => ColorHelper.FromArgb(255, 214, 160, 0),
        1 => ColorHelper.FromArgb(255, 38, 132, 214),
        _ => ColorHelper.FromArgb(255, 128, 128, 128),
    });
}

public sealed partial class RulesView : UserControl
{
    private static readonly Regex TitleRe = new(@"^\s*(?:title|name)\s*:\s*['""]?(.+?)['""]?\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex LevelRe = new(@"^\s*level\s*:\s*['""]?(\w+)", RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly Window _owner;
    private List<RuleItem> _all = new();
    private readonly ObservableCollection<RuleItem> _view = new();

    public RulesView(Window owner)
    {
        _owner = owner;
        InitializeComponent();
        RuleList.ItemsSource = _view;
        _ = LoadAsync();
    }

    // ------------------------------------------------------------ chargement

    private async Task LoadAsync()
    {
        Busy.Visibility = Visibility.Visible;
        Info.Text = "Chargement des règles…";
        _all = await Task.Run(() =>
        {
            var l = new List<RuleItem>();
            l.AddRange(Scan(ChainsawRunner.SigmaDir, "Sigma", false));
            l.AddRange(Scan(ChainsawRunner.RulesDir, "Chainsaw", false));
            l.AddRange(Scan(ChainsawRunner.CustomSigmaDir, "Personnelle (Sigma)", true));
            l.AddRange(Scan(ChainsawRunner.CustomRulesDir, "Personnelle (Chainsaw)", true));
            return l;
        });
        Busy.Visibility = Visibility.Collapsed;
        ApplyFilter();
    }

    private static IEnumerable<RuleItem> Scan(string dir, string source, bool custom)
    {
        if (!Directory.Exists(dir)) yield break;
        var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));
        foreach (var f in files)
        {
            string head;
            try
            {
                using var sr = new StreamReader(f);
                var buf = new char[4096];
                int n = sr.Read(buf, 0, buf.Length);
                head = new string(buf, 0, n);
            }
            catch { continue; }
            var t = TitleRe.Match(head);
            var lv = LevelRe.Match(head);
            yield return new RuleItem
            {
                Title = t.Success ? t.Groups[1].Value : System.IO.Path.GetFileNameWithoutExtension(f),
                Path = f, Source = source, Custom = custom, Level = lv.Success ? lv.Groups[1].Value : "",
            };
        }
    }

    // ------------------------------------------------------------ filtre / sélection

    private void Filter_Changed(object s, object e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (RuleList is null || SourceBox is null) return;
        var text = SearchBox.Text.Trim();
        IEnumerable<RuleItem> q = _all;
        q = SourceBox.SelectedIndex switch
        {
            1 => q.Where(r => r.Source == "Sigma"),
            2 => q.Where(r => r.Source == "Chainsaw"),
            3 => q.Where(r => r.Custom),
            _ => q,
        };
        if (text.Length > 0)
            q = q.Where(r => r.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || r.FileName.Contains(text, StringComparison.OrdinalIgnoreCase));
        _view.Clear();
        foreach (var r in q.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase)) _view.Add(r);
        Info.Text = $"{_view.Count} / {_all.Count} règle(s) — {_all.Count(r => r.Custom)} personnelle(s). " +
                    $"Dossier perso : {ChainsawRunner.CustomDir}";
    }

    private void List_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        var r = RuleList.SelectedItem as RuleItem;
        DeleteButton.IsEnabled = r?.Custom == true;
        try { RuleText.Text = r is null ? "" : File.ReadAllText(r.Path); }
        catch (Exception ex) { RuleText.Text = ex.Message; }
    }

    // ------------------------------------------------------------ ajout / suppression

    private async void AddFiles_Click(object s, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".yml");
        picker.FileTypeFilter.Add(".yaml");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_owner));
        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0) return;
        await ImportAsync(files.Select(f => f.Path).ToList());
    }

    private async void AddFolder_Click(object s, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_owner));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        var list = await Task.Run(() => Directory.EnumerateFiles(folder.Path, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            .ToList());
        await ImportAsync(list);
    }

    private async Task ImportAsync(List<string> files)
    {
        Busy.Visibility = Visibility.Visible;
        Info.Text = $"Import de {files.Count} fichier(s)…";
        var (ok, skipped) = await Task.Run(() => ChainsawRunner.ImportRules(files));
        await LoadAsync();
        Info.Text = $"{ok} règle(s) ajoutée(s), {skipped} ignorée(s) (format non reconnu). " + Info.Text;
    }

    private async void DeleteConfirm_Click(object s, RoutedEventArgs e)
    {
        DeleteButton.Flyout?.Hide();
        if (RuleList.SelectedItem is not RuleItem { Custom: true } r) return;
        try { File.Delete(r.Path); } catch (Exception ex) { Info.Text = "Suppression impossible : " + ex.Message; return; }
        await LoadAsync();
    }

    private void OpenFolder_Click(object s, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ChainsawRunner.CustomDir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ChainsawRunner.CustomDir) { UseShellExecute = true });
    }
}
