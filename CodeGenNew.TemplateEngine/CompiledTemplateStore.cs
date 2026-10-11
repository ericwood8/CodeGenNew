using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodeGenNew.Core;
using Mono.TextTemplating;

namespace CodeGenNew.TemplateEngine;

/// <summary> Compiled templates kept on disk, so a run that changed nothing skips the compiler. An entry is named by a hash of the template text, the public surface of
/// CodeGenNew.Core (what a template binds to when it compiles: type and member signatures, constants, enum values, default arguments) and the runtime, so editing a template or
/// changing what Core offers compiles again, while a change inside a Core method body does not. A missing, unreadable or mismatched entry is simply a miss. </summary>
internal sealed class CompiledTemplateStore
{
    private sealed record Meta(string ClassName, string[] References, string Culture);

    private const string EnvironmentVariable = "CODEGENNEW_TEMPLATE_CACHE";
    private const string Off = "off";
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);

    private static readonly Lazy<string> SurfaceHash = new(ComputeSurfaceHash);
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Mono.TextTemplating keeps the compiled image and the class name in members it does not expose, so they are read and put back by reflection; any mismatch is a cache miss.
    private static readonly Type? AssemblyDataType = typeof(CompiledTemplate).Assembly.GetType("Mono.TextTemplating.CodeCompilation.CompiledAssemblyData");
    private static readonly ConstructorInfo? AssemblyDataConstructor = AssemblyDataType?.GetConstructor(Hidden, [typeof(byte[]), typeof(byte[])]);
    private static readonly PropertyInfo? ImageProperty = AssemblyDataType?.GetProperty("Assembly", Hidden);
    private static readonly PropertyInfo? ReferencesProperty = typeof(CompiledTemplate).GetProperty("ReferencedAssemblyFiles", Hidden);
    private static readonly ConstructorInfo? TemplateConstructor = AssemblyDataType is null ? null
        : typeof(CompiledTemplate).GetConstructor(Hidden, [typeof(Microsoft.VisualStudio.TextTemplating.ITextTemplatingEngineHost), AssemblyDataType, typeof(string), typeof(CultureInfo), typeof(string[])]);
    private static readonly FieldInfo? AssemblyDataField = typeof(CompiledTemplate).GetField("templateAssemblyData", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ClassNameField = typeof(CompiledTemplate).GetField("templateClassFullName", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CultureField = typeof(CompiledTemplate).GetField("culture", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly string _folder;
    private int _pruned;

    private CompiledTemplateStore(string folder) => _folder = folder;

    /// <summary> The store a run uses: the folder named by the environment variable CODEGENNEW_TEMPLATE_CACHE ("off" turns it off), else a folder under the user's local application data. Null when off. </summary>
    public static CompiledTemplateStore? Default()
    {
        string? setting = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.Equals(setting, Off, StringComparison.OrdinalIgnoreCase))
            return null;
        if (!string.IsNullOrWhiteSpace(setting))
            return At(setting);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(local) ? null : At(Path.Combine(local, "CodeGenNew", "TemplateCache"));
    }

    public static CompiledTemplateStore At(string folder) => new(folder);

    public static string KeyOf(string templateText)
    {
        var text = new StringBuilder(templateText.Length + 200).Append(templateText).Append('\0').Append(SurfaceHash.Value).Append('\0')
            .Append(Environment.Version).Append('\0').Append(typeof(TemplateGenerator).Assembly.GetName().Version);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..32];
    }

    public CompiledTemplate? TryLoad(TemplateGenerator generator, string key)
    {
        try
        {
            string stem = Path.Combine(_folder, key);
            if (!File.Exists(stem + ".dll") || !File.Exists(stem + ".json"))
                return null;
            var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(stem + ".json"));
            if (meta is null)
                return null;
            if (AssemblyDataConstructor is null || TemplateConstructor is null)
                return null;
            object data = AssemblyDataConstructor.Invoke([File.ReadAllBytes(stem + ".dll"), null]);
            return (CompiledTemplate)TemplateConstructor.Invoke([generator, data, meta.ClassName, CultureInfo.GetCultureInfo(meta.Culture), meta.References]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or CultureNotFoundException or ArgumentException or TargetInvocationException)
        {
            return null;
        }
    }

    public void Save(CompiledTemplate compiled, string key)
    {
        try
        {
            if (AssemblyDataField?.GetValue(compiled) is not { } data || ImageProperty?.GetValue(data) is not byte[] { Length: > 0 } image
                || ClassNameField?.GetValue(compiled) is not string className)
                return;
            string culture = (CultureField?.GetValue(compiled) as CultureInfo ?? CultureInfo.InvariantCulture).Name;
            Directory.CreateDirectory(_folder);
            Prune();
            string stem = Path.Combine(_folder, key);
            Replace(stem + ".json", JsonSerializer.SerializeToUtf8Bytes(new Meta(className, ReferencesProperty?.GetValue(compiled) as string[] ?? [], culture)));
            Replace(stem + ".dll", image);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary> Written beside the target and moved into place, so another process never reads half a file. </summary>
    private static void Replace(string path, byte[] bytes)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary> Once per process: entries nobody wrote for a month are old surfaces or edited templates. </summary>
    private void Prune()
    {
        if (Interlocked.Exchange(ref _pruned, 1) == 1)
            return;
        foreach (string file in Directory.EnumerateFiles(_folder))
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > KeepFor)
                try { File.Delete(file); } catch (IOException) { }
    }

    private static string ComputeSurfaceHash()
    {
        var text = new StringBuilder();
        var assembly = typeof(TableModel).Assembly;
        text.Append(assembly.GetName().Name);
        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            text.Append('\n').Append(type.FullName).Append(':').Append(type.BaseType).Append(':').Append((int)type.Attributes);
            foreach (var face in type.GetInterfaces().OrderBy(i => i.FullName, StringComparer.Ordinal))
                text.Append(',').Append(face);
            if (type.IsEnum)
            {
                foreach (var name in Enum.GetNames(type))
                    text.Append(' ').Append(name).Append('=').Append(Convert.ToInt64(Enum.Parse(type, name), CultureInfo.InvariantCulture));
                continue;
            }
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(IsVisibleToTemplates).Select(Describe).OrderBy(m => m, StringComparer.Ordinal);
            foreach (string member in members)
                text.Append("\n ").Append(member);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static bool IsVisibleToTemplates(MemberInfo member) => member switch
    {
        FieldInfo f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly,
        MethodBase m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly,
        PropertyInfo or EventInfo => true,
        Type t => t.IsNestedPublic || t.IsNestedFamily,
        _ => false,
    };

    private static string Describe(MemberInfo member)
    {
        var text = new StringBuilder(member.MemberType + " " + member);
        switch (member)
        {
            case FieldInfo { IsLiteral: true } f:
                text.Append('=').Append(f.GetRawConstantValue());
                break;
            case PropertyInfo p:
                text.Append(p.CanRead ? " get" : "").Append(p.CanWrite ? " set" : "");
                break;
            case MethodBase m:
                foreach (var p in m.GetParameters().Where(p => p.HasDefaultValue))
                    text.Append(' ').Append(p.Name).Append('=').Append(p.RawDefaultValue);
                break;
        }
        return text.ToString();
    }
}
