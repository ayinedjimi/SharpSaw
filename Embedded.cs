using System.Reflection;
using System.Runtime.InteropServices;

namespace SharpSaw;

/// <summary>
/// Tout est embarqué dans l'exécutable (DLL natives, modèle, mermaid, icône) et extrait une seule fois
/// dans %LOCALAPPDATA%\SharpSaw\runtime\&lt;version&gt;. L'exe reste donc portable : un seul fichier à copier.
/// </summary>
public static class Embedded
{
    private static readonly Assembly Asm = typeof(Embedded).Assembly;
    private static readonly object Gate = new();

    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharpSaw", "runtime",
        Asm.GetName().Version?.ToString() ?? "1");

    public static string WebDir => Path.Combine(Dir, "web");

    /// <summary>Dossier de l'exe d'origine (pour des modèles optionnels déposés à côté).</summary>
    public static string ExeDir => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    public static bool Has(string resource) => Asm.GetManifestResourceInfo(resource) is not null;

    /// <summary>Extrait la ressource si absente ou de taille différente, et renvoie son chemin.</summary>
    public static string Extract(string resource, string relativePath)
    {
        var target = Path.Combine(Dir, relativePath);
        lock (Gate)
        {
            using var src = Asm.GetManifestResourceStream(resource)
                ?? throw new FileNotFoundException("Ressource embarquée absente : " + resource);
            if (File.Exists(target) && new FileInfo(target).Length == src.Length) return target;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var tmp = target + ".part";
            using (var dst = File.Create(tmp)) src.CopyTo(dst, 1 << 20);
            File.Move(tmp, target, true);
        }
        return target;
    }

    private static bool _resolverSet;

    /// <summary>Fait charger chainsaw_ffi.dll et lamazero.dll depuis le dossier d'extraction.</summary>
    public static void InstallNativeResolver()
    {
        if (_resolverSet) return;
        _resolverSet = true;
        NativeLibrary.SetDllImportResolver(Asm, (name, _, _) =>
        {
            if (name is "chainsaw_ffi.dll" or "lamazero.dll" && Has(name))
                return NativeLibrary.Load(Extract(name, name));
            return IntPtr.Zero;
        });
    }

    public static string WebFiles()
    {
        Extract("mermaid.min.js", Path.Combine("web", "mermaid.min.js"));
        Extract("summary.html", Path.Combine("web", "summary.html"));
        return WebDir;
    }

    public static string? IconPath()
    {
        try { return Has("SharpSaw.ico") ? Extract("SharpSaw.ico", "SharpSaw.ico") : null; } catch { return null; }
    }
}
