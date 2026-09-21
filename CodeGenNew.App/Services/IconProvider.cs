using System.Reflection;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace CodeGenNew.App.Services;

/// <summary>
/// Maps app state to the shipped icon images (Docs/specs.md section 9.4 / section 1's "use the provided
/// icons" ask). Images are embedded resources baked into this assembly (the app is meant to be self-
/// sufficient enough that copying the EXE is about all that is needed), loaded via
/// Assembly.GetManifestResourceStream rather than a loose file on disk. Looked at each PNG to decide its
/// role rather than guessing from the filename alone:
///   - database.png: plain database cylinder -> the TreeView's root (connected database) node.
///   - table.png: plain grid -> a normal table (has a primary key).
///   - "table _no_pk.png" (PK badge crossed out): a table missing a primary key but that still has
///     some other unique index/constraint.
///   - table_no_unique.png (warning triangle): a table with neither a primary key nor any unique
///     index at all -- the worst case, rows could be fully duplicated.
///   - DataSource.png (cylinder + plug): the "Connect" action.
///   - Refresh.png: the "Refresh" action.
///   - UIs.png: the "Manage Templates"/"Templates" action (closest available fit).
///   - columns.png: columns under an expanded table.
///   - pk.png: primary-key columns under an expanded table.
/// "New database.png" and "network-server-database.png" have no matching v1 feature yet and are left unused.
/// </summary>
public class IconProvider
{
    private static readonly Assembly ResourceAssembly = typeof(IconProvider).Assembly;
    private static readonly string[] ResourceNames = ResourceAssembly.GetManifestResourceNames();

    private readonly Dictionary<string, BitmapImage> _cache = new(StringComparer.OrdinalIgnoreCase);

    public BitmapImage Database => Get("database.png");
    public BitmapImage Table => Get("table.png");
    public BitmapImage TableNoPrimaryKey => Get("table _no_pk.png");
    public BitmapImage TableNoUniqueIndex => Get("table_no_unique.png");
    public BitmapImage Connect => Get("DataSource.png");
    public BitmapImage Refresh => Get("Refresh.png");
    public BitmapImage ManageTemplates => Get("UIs.png");
    public BitmapImage Column => Get("columns.png");
    public BitmapImage PrimaryKey => Get("pk.png");

    public BitmapImage ForTable(bool hasPrimaryKey, bool hasUniqueIndex)
    {
        if (hasPrimaryKey)
            return Table;
        return hasUniqueIndex ? TableNoPrimaryKey : TableNoUniqueIndex;
    }

    private BitmapImage Get(string fileName)
    {
        if (_cache.TryGetValue(fileName, out var cached))
            return cached;

        var image = new BitmapImage();
        _cache[fileName] = image;

        // Match on ".<file name>" so "table.png" cannot also match "database_table.png" (whichever came first in the
        // manifest used to win, giving tables the wrong icon).
        string? resourceName = ResourceNames.FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName is not null)
            _ = LoadAsync(image, resourceName);

        return image;
    }

    private static async Task LoadAsync(BitmapImage image, string resourceName)
    {
        try
        {
            using var resourceStream = ResourceAssembly.GetManifestResourceStream(resourceName);
            if (resourceStream is null)
                return;

            using var buffer = new MemoryStream();
            await resourceStream.CopyToAsync(buffer);

            // Write the bytes with a DataWriter and StoreAsync them: that really commits them to the stream. The
            // earlier approach copied through Stream.AsStreamForWrite(), a buffering adapter that was never
            // flushed, so the decoder was handed an empty stream and EVERY icon silently rendered blank.
            using var randomAccessStream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(randomAccessStream))
            {
                writer.WriteBytes(buffer.ToArray());
                await writer.StoreAsync();
                writer.DetachStream();
            }

            randomAccessStream.Seek(0);
            await image.SetSourceAsync(randomAccessStream);
        }
        catch (Exception ex)
        {
            // A missing icon must never take the app down (this runs fire-and-forget); leave a trace for a debugger.
            System.Diagnostics.Debug.WriteLine($"IconProvider: could not load {resourceName}: {ex.Message}");
        }
    }
}
