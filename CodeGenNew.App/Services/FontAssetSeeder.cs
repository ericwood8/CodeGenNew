using System.Reflection;
using System.Security.Cryptography;

namespace CodeGenNew.App.Services;

/// <summary>
/// Materializes the Nocturne theme's embedded font files (CodeGenNew.App.csproj: Assets\Fonts\*.ttf,
/// embedded like Images rather than shipped as loose Content) into real files next to the EXE, since
/// XAML's <c>FontFamily="ms-appx:///Assets/Fonts/...#Name"</c> needs one -- there is no supported way to
/// point it straight at an in-memory resource stream the way IconProvider points ImageIcon at one.
///
/// Unlike DefaultAssetSeeder (Templates/SpecialLogicColumns.config), there is no customization to protect:
/// nobody hand-edits a .ttf, so this is a plain "write it if missing or different from what's embedded"
/// with no diff/".new" side-file logic.
/// </summary>
public static class FontAssetSeeder
{
    public static void EnsureFontsSeeded()
    {
        string fontsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        Directory.CreateDirectory(fontsDirectory);

        Assembly assembly = typeof(FontAssetSeeder).Assembly;
        string[] resourceNames = assembly.GetManifestResourceNames();

        foreach (string resourceName in resourceNames)
        {
            // Embedded under LinkBase "Fonts", e.g. "CodeGenNew.Fonts.Sora[wght].ttf".
            int marker = resourceName.IndexOf(".Fonts.", StringComparison.OrdinalIgnoreCase);
            if (marker < 0 || !resourceName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
                continue;

            string fileName = resourceName[(marker + ".Fonts.".Length)..];
            string destinationPath = Path.Combine(fontsDirectory, fileName);

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                continue;

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            byte[] shipped = buffer.ToArray();

            if (File.Exists(destinationPath) && Hash(File.ReadAllBytes(destinationPath)).Equals(Hash(shipped), StringComparison.OrdinalIgnoreCase))
                continue; // already up to date.

            File.WriteAllBytes(destinationPath, shipped);
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
