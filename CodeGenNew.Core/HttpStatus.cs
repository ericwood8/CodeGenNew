namespace CodeGenNew.Core;

/// <summary> The HTTP status numbers the generated APIs and clients answer and test for. Use these names in templates and writers so a number is never bare. </summary>
public static class HttpStatus
{
    public const int Ok = 200;
    public const int Created = 201;
    public const int NoContent = 204;
    public const int BadRequest = 400;
    public const int Unauthorized = 401;
    public const int Forbidden = 403;
    public const int NotFound = 404;
    public const int Conflict = 409;
    public const int UnprocessableEntity = 422;
    public const int TooManyRequests = 429;
    public const int InternalServerError = 500;

    /// <summary> The ASP.NET Core name of a status (<c>StatusCodes.Status409Conflict</c>), for generated C# that should read as words. </summary>
    public static string AspNetName(int status) => status switch
    {
        Ok => "StatusCodes.Status200OK",
        Created => "StatusCodes.Status201Created",
        NoContent => "StatusCodes.Status204NoContent",
        BadRequest => "StatusCodes.Status400BadRequest",
        Unauthorized => "StatusCodes.Status401Unauthorized",
        Forbidden => "StatusCodes.Status403Forbidden",
        NotFound => "StatusCodes.Status404NotFound",
        Conflict => "StatusCodes.Status409Conflict",
        UnprocessableEntity => "StatusCodes.Status422UnprocessableEntity",
        TooManyRequests => "StatusCodes.Status429TooManyRequests",
        InternalServerError => "StatusCodes.Status500InternalServerError",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "No ASP.NET name is listed for this status.")
    };
}

/// <summary> What the command-line tool returns to the shell. </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
}
