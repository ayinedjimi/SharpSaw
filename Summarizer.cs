using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace SharpSaw;

/// <summary>Résumé des findings par un petit modèle Gemma local (lamazero.dll, llama.cpp, CPU, hors ligne).</summary>
public static class Summarizer
{
    private delegate int TokenCb(IntPtr piece, int idx, IntPtr user);

    [DllImport("lamazero.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr lamazero_quick_load([MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int nCtx, int nThreads, out int status);

    [DllImport("lamazero.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void lamazero_quick_free(IntPtr session);

    [DllImport("lamazero.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int lamazero_quick_chat_stream(IntPtr session,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string prompt, int maxNewTokens, int timeoutMs,
        TokenCb callback, IntPtr user, out int nGenerated, out double elapsedMs);

    [DllImport("lamazero.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void lamazero_request_abort();

    [DllImport("lamazero.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void lamazero_reset_abort();

    private const int NCtx = 6144;
    private const int MaxNewTokens = 450;
    private static readonly object Gate = new();
    private static IntPtr _session;

    // Modèle optionnel plus gros déposé à côté de l'exe (dossier models) : prioritaire sur le modèle embarqué.
    private static string? ExternalModel()
    {
        var dir = Path.Combine(Embedded.ExeDir, "models");
        if (!Directory.Exists(dir)) return null;
        foreach (var n in new[] { "gemma-3-4b-it-Q4_K_M.gguf", "gemma-3-1b-it-Q4_K_M.gguf" })
            if (File.Exists(Path.Combine(dir, n))) return Path.Combine(dir, n);
        return Directory.EnumerateFiles(dir, "*.gguf").FirstOrDefault();
    }

    /// <summary>Chemin du modèle ; le modèle embarqué (Gemma 3 1B) est extrait à la première utilisation.</summary>
    public static string ModelPath => ExternalModel() ?? Embedded.Extract("model.gguf", "model.gguf");

    public static bool Available => Embedded.Has("lamazero.dll") && (ExternalModel() is not null || Embedded.Has("model.gguf"));

    public static void Abort() { try { lamazero_request_abort(); } catch { } }

    public static void Release()
    {
        lock (Gate)
        {
            if (_session != IntPtr.Zero) { lamazero_quick_free(_session); _session = IntPtr.Zero; }
        }
    }

    /// <summary>Génère le résumé en streaming. <paramref name="onText"/> reçoit chaque fragment (thread de calcul).</summary>
    public static Task<string> SummarizeAsync(IReadOnlyList<Detection> findings, Action<string> onText, Action<string> onStatus)
        => Task.Run(() =>
        {
            lock (Gate)
            {
                if (_session == IntPtr.Zero)
                {
                    onStatus(ExternalModel() is null ? "Préparation du modèle embarqué (première utilisation)…" : "Chargement du modèle Gemma…");
                    var modelPath = ModelPath;
                    onStatus("Chargement du modèle Gemma…");
                    _session = lamazero_quick_load(modelPath, NCtx, 0, out int st);
                    if (_session == IntPtr.Zero) throw new InvalidOperationException($"Chargement du modèle impossible (code {st}).");
                }
                onStatus("Génération du résumé…");
                lamazero_reset_abort();

                var decoder = Encoding.UTF8.GetDecoder();   // gère les caractères UTF-8 coupés entre deux tokens
                var sb = new StringBuilder();
                TokenCb cb = (piece, idx, _) =>
                {
                    var len = 0;
                    while (Marshal.ReadByte(piece, len) != 0) len++;
                    var bytes = new byte[len];
                    Marshal.Copy(piece, bytes, 0, len);
                    var chars = new char[Encoding.UTF8.GetMaxCharCount(len)];
                    int n = decoder.GetChars(bytes, 0, len, chars, 0);
                    if (n > 0) { var s = new string(chars, 0, n); sb.Append(s); onText(s); }
                    return 0;
                };

                int rc = lamazero_quick_chat_stream(_session, BuildPrompt(findings), MaxNewTokens, 240_000,
                                                    cb, IntPtr.Zero, out _, out _);
                GC.KeepAlive(cb);
                if (rc != 0 && rc != -6 && sb.Length == 0) throw new InvalidOperationException($"Erreur de génération (code {rc}).");
                return sb.ToString();
            }
        });

    // ------------------------------------------------------------------ prompt

    private static string BuildPrompt(IReadOnlyList<Detection> f)
    {
        var sb = new StringBuilder();
        sb.Append("<start_of_turn>user\n");
        sb.Append("Tu es un analyste SOC prudent. Voici des détections automatiques (règles Sigma/Chainsaw) sur des journaux Windows. ")
          .Append("Une détection est un indice à vérifier, pas une preuve d'attaque. Règles strictes : ")
          .Append("n'emploie les mots « grave », « critique » ou « urgent » que si la gravité indiquée est critique ou haute ; ")
          .Append("ne cite que des règles présentes dans la liste ; n'invente ni chiffre, ni outil, ni machine ; ")
          .Append("ne reformule pas les compteurs (ils sont déjà affichés). ")
          .Append("Écris en français, 5 lignes maximum, une synthèse pour un responsable : ce qui ressort, ")
          .Append("le niveau d'inquiétude réaliste, puis la priorité de vérification. Appuie-toi sur l'analyse ci-dessous.\n\n");

        sb.Append(Facts(f));
        sb.Append("Analyse par règles :\n").Append(Analyzer.Analyze(f)).Append('\n');
        sb.Append("Exemples d'événements (les plus graves d'abord) :\n");
        foreach (var d in f.OrderByDescending(d => d.Rank).ThenBy(d => d.Timestamp).Take(5))
            sb.Append($"- [{d.Level}] {Clip(d.Name, 80)} {Context(d)}\n");

        sb.Append("<end_of_turn>\n<start_of_turn>model\n");
        return sb.ToString();
    }

    /// <summary>Chiffres exacts calculés par le code (jamais par le modèle).</summary>
    public static string Facts(IReadOnlyList<Detection> f)
    {
        var sb = new StringBuilder();
        sb.Append($"Total : {f.Count} détection(s).\n");
        sb.Append("Par gravité : ")
          .Append(string.Join(", ", new[] { (4, "critique"), (3, "haute"), (2, "moyenne"), (1, "faible"), (0, "info") }
              .Where(x => f.Any(d => d.Rank == x.Item1))
              .Select(x => $"{x.Item2}={f.Count(d => d.Rank == x.Item1)}"))).Append('\n');

        var stamps = f.Select(d => d.Timestamp).Where(t => t.Length > 0).OrderBy(t => t).ToList();
        if (stamps.Count > 0) sb.Append($"Période : {stamps[0]} -> {stamps[^1]}\n");

        sb.Append("Règles les plus fréquentes :\n");
        foreach (var g in f.GroupBy(d => d.Name).OrderByDescending(g => g.Count()).Take(8))
            sb.Append($"- {Clip(g.Key, 90)} (x{g.Count()}, gravité {g.First().Level})\n");
        return sb.ToString();
    }

    // Extrait quelques champs parlants de l'événement (machine, utilisateur, processus, ligne de commande).
    private static string Context(Detection d)
    {
        try
        {
            using var doc = JsonDocument.Parse(d.RawJson);
            var parts = new List<string>();
            foreach (var key in new[] { "Computer", "TargetUserName", "SubjectUserName", "User", "Image", "NewProcessName", "CommandLine", "ServiceName" })
                if (Find(doc.RootElement, key) is { } v && v.Length > 0) parts.Add($"{key}={Clip(v, 70)}");
            return parts.Count == 0 ? "" : "(" + string.Join(", ", parts.Take(3)) + ")";
        }
        catch { return ""; }
    }

    private static string? Find(JsonElement e, string key)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name == key && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString();
                if (Find(p.Value, key) is { } r) return r;
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
            foreach (var i in e.EnumerateArray()) if (Find(i, key) is { } r) return r;
        return null;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
