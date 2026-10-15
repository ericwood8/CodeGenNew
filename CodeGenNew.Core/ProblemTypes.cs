namespace CodeGenNew.Core;

/// <summary> The <c>type</c> of each problem-details answer the generated API gives (RFC 9457). The status says how serious the failure is; the type says which failure it is, so a caller can tell
/// "value too long" from "value required" without reading the text. The path is an identifier, not a page to fetch. </summary>
public static class ProblemTypes
{
    public const string Prefix = "/problems/";

    // a write the database refused
    public const string DuplicateValue = Prefix + "duplicate-value";
    public const string InUse = Prefix + "in-use";
    public const string RelatedRowMissing = Prefix + "related-row-missing";
    public const string ValueTooLong = Prefix + "value-too-long";
    public const string ValueRequired = Prefix + "value-required";
    public const string DatabaseRefused = Prefix + "database-refused";
    public const string ChangedByElse = Prefix + "changed-by-someone-else";

    // a request the API refused
    public const string InvalidRequest = Prefix + "invalid-request";
    public const string InvalidBody = Prefix + "invalid-body";
    public const string InvalidRows = Prefix + "invalid-rows";
    public const string InvalidPage = Prefix + "invalid-page";
    public const string InvalidId = Prefix + "invalid-id";
    public const string KeyRequired = Prefix + "key-required";
    public const string DuplicateKey = Prefix + "duplicate-key";
    public const string NameRequired = Prefix + "name-required";
    public const string DuplicateName = Prefix + "duplicate-name";
    public const string NotYours = Prefix + "not-your-row";
    public const string ColumnNotYours = Prefix + "column-not-yours";
    public const string RoleNotAllowed = Prefix + "role-not-allowed";
    public const string LastAdministrator = Prefix + "last-administrator";
    public const string ChangeNotAllowed = Prefix + "change-not-allowed";
    public const string SearchNotScoped = Prefix + "search-not-scoped";

    // signing in
    public const string SignInIncomplete = Prefix + "sign-in-incomplete";
    public const string TooManyAttempts = Prefix + "too-many-attempts";
    public const string WrongCredentials = Prefix + "wrong-credentials";

    // anything else
    public const string Unexpected = Prefix + "unexpected";
}
