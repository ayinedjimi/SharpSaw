using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SharpSaw;

public sealed record HuntOptions(
    string EvtxPath,
    string? Level, DateTimeOffset? From, DateTimeOffset? To);

/// <summary>Moteur Chainsaw embarqué : appel direct de chainsaw_ffi.dll (aucun processus externe).</summary>
public static class ChainsawRunner
{
    [DllImport("chainsaw_ffi.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr chainsaw_run([MarshalAs(UnmanagedType.LPUTF8Str)] string argvJson);

    [DllImport("chainsaw_ffi.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void chainsaw_free(IntPtr p);

    private const string RulesVersion = "chainsaw-2.16.5-r1";

    /// <summary>Dossier où les règles embarquées sont extraites (une fois par version).</summary>
    public static string BaseDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharpSaw", RulesVersion);

    public static string SigmaDir => Path.Combine(BaseDir, "sigma");
    public static string RulesDir => Path.Combine(BaseDir, "rules");
    public static string MappingFile => Path.Combine(BaseDir, "mappings", "sigma-event-logs-all.yml");

    public static string CustomDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharpSaw", "custom-rules");
    public static string CustomSigmaDir => Path.Combine(CustomDir, "sigma");
    public static string CustomRulesDir => Path.Combine(CustomDir, "chainsaw");

    private static bool HasRules(string d) =>
        Directory.Exists(d) && Directory.EnumerateFiles(d, "*.y*ml", SearchOption.AllDirectories).Any();

    /// <summary>Copie les règles dans le dossier personnel : Sigma (logsource + detection) ou règle Chainsaw.</summary>
    public static (int Added, int Skipped) ImportRules(IEnumerable<string> files)
    {
        int ok = 0, skipped = 0;
        foreach (var f in files)
        {
            try
            {
                var text = File.ReadAllText(f);
                bool sigma = text.Contains("detection:") && text.Contains("logsource:");
                bool chainsaw = !sigma && text.Contains("kind:") && text.Contains("group:");
                if (!sigma && !chainsaw) { skipped++; continue; }
                var dir = sigma ? CustomSigmaDir : CustomRulesDir;
                Directory.CreateDirectory(dir);
                var name = Path.GetFileName(f);
                var dest = Path.Combine(dir, name);
                for (int i = 1; File.Exists(dest); i++)
                    dest = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(name)}-{i}{Path.GetExtension(name)}");
                File.Copy(f, dest);
                ok++;
            }
            catch { skipped++; }
        }
        return (ok, skipped);
    }

    public static int SigmaCount => CountFiles(SigmaDir);
    public static int RuleCount => CountFiles(RulesDir);
    private static int CountFiles(string d) =>
        Directory.Exists(d) ? Directory.EnumerateFiles(d, "*.yml", SearchOption.AllDirectories).Count() : 0;

    public static Task EnsureRulesAsync() => Task.Run(() =>
    {
        var marker = Path.Combine(BaseDir, ".ok");
        if (File.Exists(marker)) return;
        if (Directory.Exists(BaseDir)) Directory.Delete(BaseDir, true);
        Directory.CreateDirectory(BaseDir);
        using var s = typeof(ChainsawRunner).Assembly.GetManifestResourceStream("rules.zip")
                      ?? throw new InvalidOperationException("Ressource rules.zip introuvable.");
        ZipFile.ExtractToDirectory(s is MemoryStream ? s : CopyToMemory(s), BaseDir);
        File.WriteAllText(marker, RulesVersion);
    });

    private static MemoryStream CopyToMemory(Stream s) { var m = new MemoryStream(); s.CopyTo(m); m.Position = 0; return m; }

    public static Task<(List<Detection> Results, string Stderr)> HuntAsync(HuntOptions o)
    {
        var args = new List<string> { "hunt", o.EvtxPath, "-s", SigmaDir, "-m", MappingFile, "-r", RulesDir,
                                      "--skip-errors" };
        // Règles personnelles ajoutées par l'utilisateur
        if (HasRules(CustomSigmaDir)) { args.Add("-s"); args.Add(CustomSigmaDir); }
        if (HasRules(CustomRulesDir)) { args.Add("-r"); args.Add(CustomRulesDir); }
        if (!string.IsNullOrEmpty(o.Level)) { args.Add("--level"); args.Add(o.Level); }
        if (o.From is { } f) { args.Add("--from"); args.Add(f.ToString("yyyy-MM-ddTHH:mm:ss")); }
        if (o.To is { } t) { args.Add("--to"); args.Add(t.ToString("yyyy-MM-ddTHH:mm:ss")); }
        return RunToFileAsync(args);
    }

    public static Task<(List<Detection> Results, string Stderr)> SearchAsync(
        string path, string pattern, bool ignoreCase, bool regex)
    {
        var args = new List<string> { "search" };
        if (regex) { args.Add("-e"); args.Add(pattern); } else args.Add(pattern);
        args.Add(path);
        if (ignoreCase) args.Add("-i");
        args.Add("--skip-errors");
        return RunToFileAsync(args);
    }

    // Chainsaw écrit ses résultats JSON dans un fichier temporaire (-o), relu ensuite.
    private static Task<(List<Detection>, string)> RunToFileAsync(List<string> args) => Task.Run(() =>
    {
        var outFile = Path.Combine(Path.GetTempPath(), "SharpSaw-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            args.AddRange(new[] { "--json", "-q", "-o", outFile });
            var resp = CallNative(args);
            using var doc = JsonDocument.Parse(resp);
            bool ok = doc.RootElement.GetProperty("ok").GetBoolean();
            string err = doc.RootElement.GetProperty("error").GetString() ?? "";
            if (!ok) throw new InvalidOperationException(err);
            var text = ReadShared(outFile);
            return (ParseJson(text), err);
        }
        finally
        {
            for (int i = 0; i < 20; i++)
            {
                try { File.Delete(outFile); break; } catch (IOException) { Thread.Sleep(100); }
            }
        }
    });

    private static string ReadShared(string path)
    {
        for (int i = 0; ; i++)
        {
            try
            {
                if (!File.Exists(path)) return "";
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                return sr.ReadToEnd();
            }
            catch (IOException) when (i < 20) { Thread.Sleep(100); }
        }
    }

    private static string CallNative(List<string> args)
    {
        var p = chainsaw_run(JsonSerializer.Serialize(args));
        try { return Marshal.PtrToStringUTF8(p) ?? "{\"ok\":false,\"error\":\"réponse vide\"}"; }
        finally { chainsaw_free(p); }
    }

    public static async Task<bool> ExportChannelAsync(string channel, string dir, CancellationToken ct)
    {
        var file = Path.Combine(dir, channel.Replace("/", "%4") + ".evtx");
        var psi = new ProcessStartInfo("wevtutil.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add("epl"); psi.ArgumentList.Add(channel); psi.ArgumentList.Add(file); psi.ArgumentList.Add("/ow:true");
        using var p = Process.Start(psi)!;
        await p.WaitForExitAsync(ct);
        return p.ExitCode == 0 && File.Exists(file);
    }

    // Accepte du JSONL ou un tableau JSON.
    private static List<Detection> ParseJson(string text)
    {
        var list = new List<Detection>();
        text = text.Trim();
        if (text.Length == 0) return list;
        if (text[0] == '[')
        {
            using var doc = JsonDocument.Parse(text);
            foreach (var e in doc.RootElement.EnumerateArray()) list.Add(Detection.FromJson(e));
            return list;
        }
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var l = line.Trim();
            if (!l.StartsWith('{')) continue;
            try { using var doc = JsonDocument.Parse(l); list.Add(Detection.FromJson(doc.RootElement)); }
            catch (JsonException) { }
        }
        return list;
    }
}
