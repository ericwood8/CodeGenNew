namespace CodeGenNew.TemplateEngine;

/// <summary> A template file that is only a stub for another template (its config says <c>Base=</c>): which file holds the text and which <c>Variant</c> the base template is told. </summary>
public static class TemplateAlias
{
    public static (string Path, string? Variant) Resolve(string templateFilePath)
    {
        var config = TemplateConfig.Load(templateFilePath + ".config");
        return config.Base is null
            ? (templateFilePath, null)
            : (Path.Combine(Path.GetDirectoryName(templateFilePath) ?? "", config.Base), config.Variant);
    }
}
