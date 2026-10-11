using System.Text;

namespace SharpSaw;

/// <summary>Analyse déterministe des détections : regroupement par thème, explication bénigne possible, vérification à faire.</summary>
public static class Analyzer
{
    private sealed record Theme(string Title, string[] Keys, string Meaning, string Benign, string Check);

    private static readonly Theme[] Themes =
    {
        new("PowerShell : téléchargement et appels système",
            new[] { "web request", "winapi", "downloadstring", "invoke-webrequest", "iex", "frombase64", "encoded", "windowstyle", "scriptblock", "powershell" },
            "du code PowerShell télécharge du contenu ou appelle l'API Windows (techniques courantes des chargeurs et du post-exploitation).",
            "scripts d'administration, d'installation ou de supervision légitimes.",
            "relire les ScriptBlocks (événement 4104) : source, URL appelée, compte et machine ; recouper avec les scripts attendus."),
        new("Falsification d'horodatage (Timestomp)",
            new[] { "timestomp" },
            "des dates de fichiers sont modifiées, ce qui peut servir à masquer une activité.",
            "outils de synchronisation ou de sauvegarde qui restaurent les dates.",
            "identifier le script ou le processus qui modifie les dates et les fichiers visés."),
        new("Authentification : échecs répétés",
            new[] { "failed login", "failing to authenticate", "logon failure", "brute", "password spray", "failed logon" },
            "plusieurs comptes échouent à s'authentifier depuis une même source (devinette de mots de passe possible).",
            "mot de passe expiré, service ou tâche planifiée mal configurée.",
            "relever la machine ou le processus source, la liste des comptes et si un succès suit les échecs (4624 après 4625)."),
        new("Comptes et privilèges",
            new[] { "user added", "account", "privilege", "admin group", "sid history", "kerberos", "ntlm", "dcsync" },
            "des comptes ou des privilèges ont changé, ou une authentification inhabituelle a eu lieu.",
            "gestion normale des comptes par l'administration.",
            "vérifier qui a fait la modification et si elle correspond à une demande."),
        new("Persistance et services",
            new[] { "service", "scheduled task", "run key", "startup", "autorun", "wmi" },
            "un service, une tâche ou une clé de démarrage a été créé ou modifié.",
            "installation ou mise à jour d'un logiciel.",
            "contrôler le binaire lancé, son éditeur et sa date d'apparition."),
        new("Effacement ou altération des traces",
            new[] { "log clear", "log cleared", "audit policy", "eventlog", "event log", "wevtutil" },
            "des journaux ou la politique d'audit ont été effacés ou modifiés.",
            "opération de maintenance planifiée.",
            "identifier le compte à l'origine et recouper avec une opération connue."),
        new("Outils et accès distants",
            new[] { "psexec", "rdp", "remote", "lateral", "mimikatz", "lsass", "credential" },
            "des outils d'exécution à distance ou d'accès aux identifiants sont signalés.",
            "outils d'administration à distance habituels.",
            "vérifier la machine source et le compte, puis la légitimité de l'outil."),
    };

    /// <summary>Thème trouvé, prêt pour l'affichage (Mermaid / cartes).</summary>
    public sealed record ThemeResult(string Title, int Count, int Rank, List<string> Rules, string Meaning, string Benign, string Check);

    private static (List<(Theme T, List<Detection> D)> Found, HashSet<Detection> Assigned) Group(IReadOnlyList<Detection> f)
    {
        var assigned = new HashSet<Detection>();
        var found = new List<(Theme T, List<Detection> D)>();
        foreach (var t in Themes)
        {
            var hits = f.Where(d => !assigned.Contains(d) && t.Keys.Any(k => d.Name.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();
            if (hits.Count == 0) continue;
            foreach (var d in hits) assigned.Add(d);
            found.Add((t, hits));
        }
        return (found, assigned);
    }

    public static List<ThemeResult> Structured(IReadOnlyList<Detection> f)
    {
        var (found, assigned) = Group(f);
        var list = found.OrderByDescending(x => x.D.Max(d => d.Rank)).ThenByDescending(x => x.D.Count)
            .Select(x => new ThemeResult(x.T.Title, x.D.Count, x.D.Max(d => d.Rank),
                x.D.GroupBy(d => d.Name).OrderByDescending(g => g.Count()).Take(3).Select(g => $"{g.Key} ×{g.Count()}").ToList(),
                x.T.Meaning, x.T.Benign, x.T.Check)).ToList();
        var rest = f.Where(d => !assigned.Contains(d)).ToList();
        if (rest.Count > 0)
            list.Add(new ThemeResult("Autres détections", rest.Count, rest.Max(d => d.Rank),
                rest.GroupBy(d => d.Name).OrderByDescending(g => g.Count()).Take(4).Select(g => $"{g.Key} ×{g.Count()}").ToList(),
                "règles hors des thèmes connus.", "dépend de chaque règle.",
                "ouvrir le détail de chaque règle et du premier événement"));
        return list;
    }

    public static string Analyze(IReadOnlyList<Detection> f)
    {
        if (f.Count == 0) return "";
        var sb = new StringBuilder();
        var (found, assigned) = Group(f);

        foreach (var (t, hits) in found.OrderByDescending(x => x.D.Max(d => d.Rank)).ThenByDescending(x => x.D.Count))
        {
            var rules = hits.GroupBy(d => d.Name).OrderByDescending(g => g.Count()).Take(3)
                            .Select(g => $"{g.Key} ×{g.Count()}");
            sb.Append($"■ {t.Title} — {hits.Count} détection(s), gravité max {hits.OrderByDescending(d => d.Rank).First().Level}\n");
            sb.Append($"   Règles : {string.Join(" ; ", rules)}\n");
            sb.Append($"   Signification : {t.Meaning}\n");
            sb.Append($"   Explication possible : {t.Benign}\n");
            sb.Append($"   À vérifier : {t.Check}\n\n");
        }

        var rest = f.Where(d => !assigned.Contains(d)).ToList();
        if (rest.Count > 0)
        {
            sb.Append($"■ Autres détections — {rest.Count}\n   Règles : ")
              .Append(string.Join(" ; ", rest.GroupBy(d => d.Name).OrderByDescending(g => g.Count()).Take(4).Select(g => $"{g.Key} ×{g.Count()}")))
              .Append("\n   À vérifier : ouvrir le détail de chaque règle (menu Règles) et du premier événement.\n");
        }
        return sb.ToString();
    }
}
