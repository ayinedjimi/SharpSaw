using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SharpSaw;

public sealed partial class MainWindow : Window
{
    private readonly List<Detection> _all = new();
    private readonly ObservableCollection<Detection> _view = new();
    private readonly List<SevChip> _chips = new() { new(4), new(3), new(2), new(1), new(0) };
    private CancellationTokenSource? _cts;
    private bool _rulesReady;
    private bool _busy;
    private bool _hasRun;
    private string _lastRunInfo = "";

    private static readonly (string Name, bool On)[] DefaultChannels =
    {
        ("Security", true), ("System", true), ("Application", true),
        ("Microsoft-Windows-PowerShell/Operational", true), ("Microsoft-Windows-Sysmon/Operational", true),
        ("Microsoft-Windows-Windows Defender/Operational", false), ("Microsoft-Windows-TaskScheduler/Operational", false),
        ("Microsoft-Windows-WMI-Activity/Operational", false), ("Microsoft-Windows-TerminalServices-LocalSessionManager/Operational", false),
    };

    public MainWindow()
    {
        InitializeComponent();

        // Fenêtre : barre de titre personnalisée, fond Mica, icône de l'application.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        try { SystemBackdrop = new MicaBackdrop(); } catch { }
        try
        {
            if (Embedded.IconPath() is { } ico) AppWindow.SetIcon(ico);
        }
        catch { }

        ResultsList.ItemsSource = _view;
        Chips.ItemsSource = _chips;
        Chips.ElementPrepared += (_, a) =>
        {
            a.Element.Tapped -= Chip_Tapped;
            a.Element.Tapped += Chip_Tapped;
        };
        foreach (var (name, on) in DefaultChannels) AddChannel(name, on);
        EvtxPath.Text = @"C:\Windows\System32\winevt\Logs";
        Activated += OnFirstActivated;
        Closed += (_, _) => { try { Summarizer.Abort(); Summarizer.Release(); } catch { } App.CleanExit(); };
        if (App.PreviousCrashStep is { } prev)
            StatusText.Text = $"La dernière session s'est fermée brutalement (étape : {prev}).";
        ApplyView();
        ShowOverlay();
    }

    private bool IsSearch => SearchItem.IsSelected;
    private bool IsLive => SourceMode.SelectedIndex == 0;

    // ---------------------------------------------------------------- démarrage

    private async void OnFirstActivated(object s, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        SetBusy(true, "Extraction des règles embarquées…");
        try
        {
            await ChainsawRunner.EnsureRulesAsync();
            _rulesReady = true;
            RulesInfo.Text = $"Règles embarquées : {ChainsawRunner.SigmaCount} Sigma, {ChainsawRunner.RuleCount} Chainsaw";
            SetBusy(false, "Prêt.");
        }
        catch (Exception ex) { SetBusy(false, "Erreur règles : " + ex.Message); App.Log(ex); }
    }

    // ---------------------------------------------------------------- panneau latéral

    private void TogglePane_Click(object s, RoutedEventArgs e) => Split.IsPaneOpen = !Split.IsPaneOpen;

    private void ModeBar_SelectionChanged(SelectorBar s, SelectorBarSelectionChangedEventArgs e)
    {
        if (HuntPanel is null || SearchPanel is null) return;
        HuntPanel.Visibility = IsSearch ? Visibility.Collapsed : Visibility.Visible;
        SearchPanel.Visibility = IsSearch ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SourceMode_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (ChannelsBlock is null || FolderPanel is null) return;
        ChannelsBlock.Visibility = IsLive ? Visibility.Visible : Visibility.Collapsed;
        FolderPanel.Visibility = IsLive ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AddChannel(string name, bool on)
    {
        foreach (CheckBox c in ChannelsPanel.Children)
            if (string.Equals(c.Tag as string, name, StringComparison.OrdinalIgnoreCase)) { c.IsChecked = true; return; }
        var cb = new CheckBox
        {
            Tag = name, IsChecked = on, MinHeight = 28,
            Content = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis },
        };
        ToolTipService.SetToolTip(cb, name);
        ChannelsPanel.Children.Add(cb);
    }

    private void AddChannel_Click(object s, RoutedEventArgs e)
    {
        var name = CustomChannel.Text.Trim();
        if (name.Length == 0) return;
        AddChannel(name, true);
        CustomChannel.Text = "";
    }

    private void CustomChannel_KeyDown(object s, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) AddChannel_Click(s, e);
    }

    private IEnumerable<string> SelectedChannels() =>
        ChannelsPanel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag);

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    private async void BrowseEvtx_Click(object s, RoutedEventArgs e) { if (await PickFolderAsync() is { } p) EvtxPath.Text = p; }

    private async void About_Click(object s, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var panel = new StackPanel { Spacing = 10, MinWidth = 320 };
        panel.Children.Add(new TextBlock { Text = "SharpSaw", Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] });
        panel.Children.Add(new TextBlock { Text = $"Version {version}", Opacity = 0.7 });
        panel.Children.Add(new TextBlock
        {
            Text = "Interface WinUI 3 pour l'analyse des journaux d'événements Windows, avec le moteur Chainsaw et les règles Sigma embarqués.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock { Text = "Auteur", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "Ayi NEDJIMI" });
        panel.Children.Add(new TextBlock { Text = "AYI NEDJIMI CONSULTANTS" });
        panel.Children.Add(new HyperlinkButton
        {
            Content = "https://ayinedjimi-consultants.fr",
            NavigateUri = new Uri("https://ayinedjimi-consultants.fr"),
            Padding = new Thickness(0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Moteur : Chainsaw v2.16.5 (WithSecure Countercept, licence GPL-3.0). Règles : projet Sigma et règles Chainsaw.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 12, Margin = new Thickness(0, 8, 0, 0),
        });
        var dlg = new ContentDialog
        {
            Title = "À propos", Content = panel, CloseButtonText = "Fermer",
            XamlRoot = Root.XamlRoot, DefaultButton = ContentDialogButton.Close,
        };
        await dlg.ShowAsync();
    }

    private async void Rules_Click(object s, RoutedEventArgs e)
    {
        if (!_rulesReady) { StatusText.Text = "Règles en cours d'extraction, patientez…"; return; }
        var view = new RulesView(this)
        {
            Width = Math.Clamp(Root.ActualWidth - 120, 320, 1100),
            Height = Math.Clamp(Root.ActualHeight - 220, 300, 640),
        };
        var dlg = new ContentDialog
        {
            Title = "Règles de détection", Content = view, CloseButtonText = "Fermer",
            XamlRoot = Root.XamlRoot, DefaultButton = ContentDialogButton.Close,
        };
        await dlg.ShowAsync();
        RulesInfo.Text = $"Règles embarquées : {ChainsawRunner.SigmaCount} Sigma, {ChainsawRunner.RuleCount} Chainsaw";
    }

    // ---------------------------------------------------------------- exécution

    private async void Run_Click(object s, RoutedEventArgs e)
    {
        if (!_rulesReady) { StatusText.Text = "Règles en cours d'extraction, patientez…"; return; }
        if (_busy) return;
        var cts = _cts = new CancellationTokenSource();
        var token = cts.Token;
        SetBusy(true, "Préparation…");
        string? tmp = null;
        Task? engine = null;
        _all.Clear();
        _view.Clear();
        _hasRun = true;
        DetailBox.Text = "";
        ApplyView();
        var sw = Stopwatch.StartNew();
        try
        {
            string? level = (LevelBox.SelectedItem as ComboBoxItem)?.Content as string;
            if (level == "(tous)") level = null;

            string source;
            int files = 0;
            if (IsLive)
            {
                var channels = SelectedChannels().ToArray();
                if (channels.Length == 0) throw new InvalidOperationException("Aucun canal sélectionné.");
                tmp = Path.Combine(Path.GetTempPath(), "SharpSaw-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);
                Progress.IsIndeterminate = false;
                int done = 0, failed = 0;
                foreach (var ch in channels)
                {
                    App.Step($"export du journal {ch}");
                    StatusText.Text = $"Export du journal {ch} ({done + 1}/{channels.Length})…";
                    Progress.Value = 100.0 * done / channels.Length;
                    if (!await ChainsawRunner.ExportChannelAsync(ch, tmp, token)) failed++;
                    done++;
                }
                Progress.Value = 100;
                if (failed == channels.Length) throw new InvalidOperationException("Aucun journal exportable (droits admin requis ?).");
                source = tmp;
                files = channels.Length - failed;
            }
            else
            {
                source = EvtxPath.Text;
                files = Directory.Exists(source)
                    ? Directory.EnumerateFiles(source, "*.evtx", SearchOption.AllDirectories).Count()
                    : File.Exists(source) ? 1 : 0;
            }

            Progress.IsIndeterminate = true;
            App.Step("analyse par le moteur Chainsaw");
            StatusText.Text = "Analyse en cours (moteur Chainsaw embarqué)…";

            var work = IsSearch
                ? ChainsawRunner.SearchAsync(source, Pattern.Text, IgnoreCase.IsChecked == true, UseRegex.IsChecked == true)
                : ChainsawRunner.HuntAsync(new HuntOptions(source, level, FromDate.Date, ToDate.Date));
            engine = work;

            // Le moteur natif ne s'interrompt pas : l'annulation abandonne son résultat, le dossier temporaire
            // n'est supprimé qu'une fois le moteur terminé.
            var cancelled = Task.Delay(Timeout.Infinite, token);
            if (await Task.WhenAny(work, cancelled) != work) throw new OperationCanceledException(token);

            (List<Detection> res, string err) = await work;
            App.Step($"affichage de {res.Count} résultats");
            _all.AddRange(res);
            ApplyView();
            App.Step("idle");
            sw.Stop();
            _lastRunInfo = $"{res.Count} détection{(res.Count > 1 ? "s" : "")} en {sw.Elapsed.TotalSeconds:F1} s · " +
                           $"{files} {(IsLive ? (files > 1 ? "journaux" : "journal") : (files > 1 ? "fichiers .evtx" : "fichier .evtx"))} analysé{(files > 1 ? "s" : "")}.";
            SetBusy(false, _lastRunInfo);
            if (res.Count == 0 && !string.IsNullOrWhiteSpace(err)) DetailBox.Text = err;
        }
        catch (OperationCanceledException) { SetBusy(false, "Analyse annulée."); }
        catch (Exception ex) { SetBusy(false, "Erreur : " + ex.Message); App.Log(ex); }
        finally
        {
            if (tmp is not null)
            {
                var dir = tmp;
                void Clean() { try { Directory.Delete(dir, true); } catch { } }
                if (engine is { IsCompleted: false }) _ = engine.ContinueWith(_ => Clean(), TaskScheduler.Default);
                else Clean();
            }
        }
    }

    private void Cancel_Click(object s, RoutedEventArgs e)
    {
        if (!_busy) return;
        StatusText.Text = "Annulation…";
        _cts?.Cancel();
    }

    // ---------------------------------------------------------------- filtres et tris

    private void Filter_Changed(object s, object e) => ApplyView();
    private void Sort_Changed(object s, object e) => ApplyView();

    private void Chip_Tapped(object s, TappedRoutedEventArgs e)
    {
        if (s is FrameworkElement { DataContext: SevChip chip })
        {
            chip.IsOn = !chip.IsOn;
            ApplyView();
        }
    }

    private bool SeverityOn(int rank) => _chips.First(c => c.Rank == rank).IsOn;

    private void UpdateCounts()
    {
        foreach (var c in _chips) c.Count = _all.Count(d => d.Rank == c.Rank);
    }

    private static string Plural(int n, string one, string many) => n > 1 ? many : one;

    private void ApplyView()
    {
        if (ResultsList is null || SortBox is null || DescToggle is null) return;
        UpdateCounts();
        var text = FilterBox.Text.Trim();
        IEnumerable<Detection> q = _all.Where(d => SeverityOn(d.Rank));
        if (text.Length > 0)
            q = q.Where(d => d.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || d.Group.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || d.Authors.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || d.RawJson.Contains(text, StringComparison.OrdinalIgnoreCase));

        bool desc = DescToggle.IsChecked == true;
        DescIcon.Glyph = desc ? "\uE74B" : "\uE74A"; // flèche bas / haut
        ToolTipService.SetToolTip(DescToggle, desc
            ? "Tri décroissant (cliquer pour passer en croissant)"
            : "Tri croissant (cliquer pour passer en décroissant)");
        q = SortBox.SelectedIndex switch
        {
            1 => Order(q, d => d.Timestamp, desc),
            2 => Order(q, d => d.Name, desc),
            3 => Order(q, d => d.Group, desc),
            _ => Order(q, d => d.Rank, desc).ThenByDescendingIf(desc, d => d.Timestamp),
        };

        var selected = ResultsList.SelectedItem as Detection;
        _view.Clear();
        foreach (var d in q) _view.Add(d);
        if (selected is not null && _view.Contains(selected)) ResultsList.SelectedItem = selected;
        else if (_view.Count > 0) ResultsList.SelectedIndex = 0;   // sélectionne le premier résultat

        CountText.Text = _all.Count == 0 ? "Aucun résultat"
            : _view.Count == _all.Count ? $"{_all.Count} {Plural(_all.Count, "résultat", "résultats")}"
            : $"{_view.Count} sur {_all.Count} {Plural(_all.Count, "résultat", "résultats")}";
        ExportButton.IsEnabled = !_busy && _view.Count > 0;
        UpdateAiModule();
        ShowOverlay();
    }

    private static IOrderedEnumerable<Detection> Order<T>(IEnumerable<Detection> q, Func<Detection, T> key, bool desc)
        => desc ? q.OrderByDescending(key) : q.OrderBy(key);

    /// <summary>Message au centre de la liste : état initial, analyse en cours, aucun résultat.</summary>
    private void ShowOverlay()
    {
        if (ListOverlay is null) return;
        OverlayRing.IsActive = _busy;
        OverlayRing.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
        if (_busy) { ListOverlay.Visibility = Visibility.Visible; OverlayText.Text = "Analyse en cours…"; return; }
        if (_view.Count > 0) { ListOverlay.Visibility = Visibility.Collapsed; return; }
        ListOverlay.Visibility = Visibility.Visible;
        OverlayText.Text = !_hasRun ? "Lancez une analyse pour voir les détections"
            : _all.Count == 0 ? "Aucune détection pour cette analyse"
            : "Aucun résultat avec ces filtres";
    }

    // ---------------------------------------------------------------- module IA

    private bool _summarizing;
    private DispatcherTimer? _aiTimer;

    private static readonly string[] AiPhrases =
    {
        "Lecture des détections…",
        "Regroupement des règles par thème…",
        "Rédaction de la synthèse…",
        "Le modèle travaille, tout va bien…",
        "Mise en forme des constats…",
        "Encore quelques instants…",
    };

    /// <summary>Le module est grisé tant qu'aucune analyse n'a produit de résultat.</summary>
    private void UpdateAiModule()
    {
        if (SummaryButton is null) return;
        bool ready = _all.Count > 0 && !_busy && !_summarizing && Summarizer.Available;
        SummaryButton.IsEnabled = ready;
        AiHint.Text = !Summarizer.Available ? "Modèle IA introuvable (dossier models)."
            : _summarizing ? "Génération en cours…"
            : _busy ? "Disponible une fois l'analyse terminée."
            : _all.Count == 0 ? "Disponible après une analyse."
            : $"Prêt : résume les {_view.Count} détection{(_view.Count > 1 ? "s" : "")} affichée{(_view.Count > 1 ? "s" : "")}.";
    }

    private async void Summarize_Click(object s, RoutedEventArgs e)
    {
        if (_view.Count == 0 || _summarizing) return;
        _summarizing = true;
        UpdateAiModule();
        ResultsGrid.Visibility = Visibility.Collapsed;
        SummaryPanel.Visibility = Visibility.Visible;
        SummaryStop.Visibility = Visibility.Visible;
        SummaryRing.IsActive = true;
        var dq = DispatcherQueue;
        var snapshot = _view.ToList();   // résumé de la vue courante (filtres appliqués)
        var ai = new System.Text.StringBuilder();
        bool dirty = false;
        DispatcherTimer? flush = null;

        // Messages qui changent régulièrement + temps écoulé + fragments reçus : montre que ça avance.
        var sw = Stopwatch.StartNew();
        string phase = "Préparation…";
        int pieces = 0, tick = 0;
        void Refresh()
        {
            var msg = pieces > 0 ? $"Rédaction en cours · {pieces} fragments reçus" : AiPhrases[tick % AiPhrases.Length];
            SummaryStatus.Text = $"{(pieces > 0 ? "" : phase + " ")}{msg} · {sw.Elapsed.TotalSeconds:F0} s";
        }
        _aiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _aiTimer.Tick += (_, _) => { tick++; Refresh(); };
        _aiTimer.Start();
        Refresh();
        try
        {
            // Partie factuelle + diagramme Mermaid : affichés tout de suite, avant la synthèse du modèle.
            phase = "Préparation de l'affichage…"; Refresh();
            await EnsureSummaryWebAsync();
            await SendSummaryDataAsync(snapshot);
            phase = "Analyse affichée."; Refresh();

            flush = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            flush.Tick += async (_, _) =>
            {
                if (!dirty) return;
                dirty = false;
                await SummaryScriptAsync($"setAi({System.Text.Json.JsonSerializer.Serialize(ai.ToString())}, true)");
            };
            flush.Start();

            await Summarizer.SummarizeAsync(snapshot,
                t => dq.TryEnqueue(() => { pieces++; ai.Append(t); dirty = true; }),
                st => dq.TryEnqueue(() => { phase = st; Refresh(); }));
            flush.Stop();
            await SummaryScriptAsync($"setAi({System.Text.Json.JsonSerializer.Serialize(ai.ToString())}, false)");
            SummaryStatus.Text = $"Terminé en {sw.Elapsed.TotalSeconds:F0} s.";
        }
        catch (Exception ex) { SummaryStatus.Text = "Erreur : " + ex.Message; App.Log(ex); }
        finally
        {
            flush?.Stop();
            _aiTimer?.Stop();
            _summarizing = false;
            SummaryRing.IsActive = false;
            SummaryStop.Visibility = Visibility.Collapsed;
            UpdateAiModule();
        }
    }

    private void SummaryStop_Click(object s, RoutedEventArgs e) { Summarizer.Abort(); SummaryStatus.Text = "Arrêt…"; }
    private void SummaryClose_Click(object s, RoutedEventArgs e)
    {
        Summarizer.Abort();
        SummaryPanel.Visibility = Visibility.Collapsed;
        ResultsGrid.Visibility = Visibility.Visible;
    }

    private bool _webReady;

    private async Task EnsureSummaryWebAsync()
    {
        if (_webReady) return;
        var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(
            null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharpSaw", "webview"), new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions());
        await SummaryWeb.EnsureCoreWebView2Async(env);
        var core = SummaryWeb.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.SetVirtualHostNameToFolderMapping("sharpsaw.local", Embedded.WebFiles(),
            Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        var loaded = new TaskCompletionSource();
        void Done(Microsoft.Web.WebView2.Core.CoreWebView2 c, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs a)
        { core.NavigationCompleted -= Done; loaded.TrySetResult(); }
        core.NavigationCompleted += Done;
        core.Navigate("https://sharpsaw.local/summary.html");
        await loaded.Task;
        _webReady = true;
    }

    private async Task SummaryScriptAsync(string js)
    {
        if (_webReady) try { await SummaryWeb.CoreWebView2.ExecuteScriptAsync(js); } catch { }
    }

    private async Task SendSummaryDataAsync(IReadOnlyList<Detection> f)
    {
        var stamps = f.Select(d => d.Timestamp).Where(t => t.Length > 0).OrderBy(t => t).ToList();
        var data = new
        {
            total = f.Count,
            period = stamps.Count > 0 ? $"{stamps[0][..Math.Min(10, stamps[0].Length)]} → {stamps[^1][..Math.Min(10, stamps[^1].Length)]}" : "",
            sev = new[] { 4, 3, 2, 1, 0 }.Select(r => new { rank = r, count = f.Count(d => d.Rank == r) }).ToArray(),
            themes = Analyzer.Structured(f).Select(t => new { title = t.Title, count = t.Count, rank = t.Rank, rules = t.Rules, meaning = t.Meaning, benign = t.Benign, check = t.Check }).ToArray(),
        };
        await SummaryScriptAsync($"setData({System.Text.Json.JsonSerializer.Serialize(data)})");
    }

    // ---------------------------------------------------------------- détail et export

    private void ResultsList_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        var d = ResultsList.SelectedItem as Detection;
        DetailBox.Text = d?.RawJson ?? "";
        DetailEmpty.Visibility = d is null && DetailBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Export_Click(object s, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = "sharpsaw-results" };
        picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        // Exporte la vue courante (filtres et tri appliqués).
        await File.WriteAllTextAsync(file.Path, "[\n" + string.Join(",\n", _view.Select(r => r.RawJson)) + "\n]");
        StatusText.Text = "Exporté : " + file.Path;
    }

    private void SetBusy(bool busy, string text)
    {
        _busy = busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Progress.IsIndeterminate = busy;
        Progress.Value = 0;
        RunButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;     // actif pendant l'analyse, désactivé sinon
        ExportButton.IsEnabled = !busy && _view.Count > 0;
        StatusText.Text = text;
        UpdateAiModule();
        ShowOverlay();
    }
}

internal static class OrderingExt
{
    // Applique un second critère (date) dans le même sens que le premier.
    public static IEnumerable<Detection> ThenByDescendingIf(this IOrderedEnumerable<Detection> q, bool desc,
        Func<Detection, string> key) => desc ? q.ThenByDescending(key) : q.ThenBy(key);
}
