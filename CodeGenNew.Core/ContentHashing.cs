using System.Security.Cryptography;

namespace CodeGenNew.Core;

/// <summary> SHA-256 content-hashing toolbox for "is this file still what we shipped" comparisons --
/// CodeGenNew.TemplateEngine's DefaultAssetSeeder (Templates/*.tt, SpecialLogicColumns.config) and
/// CodeGenNew.App's FontAssetSeeder (embedded font files) each used to carry this same one-line helper. </summary>
public static class ContentHashing
{
    /// <summary> Hex-encoded SHA-256 of the given bytes. </summary>
    public static string Sha256Hex(this byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
