using Microsoft.UI.Xaml;

namespace SharpSaw;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        ReadPreviousStep();
        Embedded.InstallNativeResolver();   // DLL natives embarquées dans l'exe
        InitializeComponent();
        UnhandledException += (_, e) => { Log(e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log(e.Exception); e.SetObserved(); };
    }

    private static readonly string StepFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharpSaw", "step.txt");

    /// <summary>Étape de la session précédente si elle s'est terminée sans fermeture normale (plantage natif).</summary>
    public static string? PreviousCrashStep { get; private set; }

    /// <summary>Repère écrit avant chaque étape risquée : si l'app disparaît, on sait où.</summary>
    public static void Step(string step)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(StepFile)!); File.WriteAllText(StepFile, step); } catch { }
    }

    public static void CleanExit() { try { File.Delete(StepFile); } catch { } }

    private static void ReadPreviousStep()
    {
        try
        {
            if (!File.Exists(StepFile)) return;
            var s = File.ReadAllText(StepFile).Trim();
            File.Delete(StepFile);
            if (s.Length > 0 && s != "idle")
            {
                PreviousCrashStep = s;
                Log(new Exception("Session précédente interrompue brutalement pendant : " + s));
            }
        }
        catch { }
    }

    public static void Log(Exception? ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharpSaw");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.Now:s}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var argv = Environment.GetCommandLineArgs();
        if (argv.Length >= 3 && argv[1] == "--selftest")
        {
            // Mode diagnostic sans interface : SharpSaw.exe --selftest <dossier-ou-fichier.evtx>
            try
            {
                await ChainsawRunner.EnsureRulesAsync();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var (res, err) = await ChainsawRunner.HuntAsync(new HuntOptions(argv[2], null, null, null));
                Log(new Exception($"SELFTEST OK : {res.Count} résultats en {sw.Elapsed.TotalSeconds:F1}s. {err}"));
                if (argv.Length >= 4 && argv[3] == "--ai")
                {
                    sw.Restart();
                    var text = await Summarizer.SummarizeAsync(res, _ => { }, _ => { });
                    Log(new Exception($"SELFTEST IA OK en {sw.Elapsed.TotalSeconds:F1}s :{Environment.NewLine}{text}"));
                }
            }
            catch (Exception ex) { Log(ex); }
            Exit();
            return;
        }

        _window = new MainWindow();
        _window.Activate();
    }
}

