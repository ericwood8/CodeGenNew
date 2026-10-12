using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Shop.Api.Apis;

/// <summary>
/// Every error answers as problem details (RFC 9457): a validation failure lists the fields, a database write that was refused says why (a duplicate value, a row still in use, a value too long or missing) and names the
/// table, and anything unexpected says nothing of the server outside Development. The Angular, React and Blazor screens show the <c>detail</c> as it is.
/// </summary>
public static partial class ApiProblems
{
    private enum Constraint { Unique, ForeignKey, TooLong, Required }

    // The numbers Sqlite raises for a refused write, read from the provider's exception.
    private const int UniqueConstraint = 2067;
    private const int PrimaryKeyConstraint = 1555;
    private const int ForeignKeyConstraint = 787;
    private const int NotNullConstraint = 1299;

    public static IServiceCollection AddApiProblems(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        return services;
    }

    public static WebApplication UseApiProblems(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }

    public static ProblemDetails Problem(int status, string type, string title, string detail) => new() { Status = status, Type = type, Title = title, Detail = detail };

    /// <summary> What to tell the caller about a write the database refused. </summary>
    public static ProblemDetails Describe(DbUpdateException error)
    {
        string table = TableOf(error);
        bool removing = error.Entries.Any(entry => entry.State == EntityState.Deleted);
        return ConstraintOf(error.InnerException) switch
        {
            Constraint.Unique => Problem(StatusCodes.Status409Conflict, "/problems/duplicate-value", "Duplicate value", $"A {table} with that value already exists."),
            Constraint.ForeignKey when removing => Problem(StatusCodes.Status409Conflict, "/problems/in-use", "In use", $"The {table} cannot be removed because other rows use it."),
            Constraint.ForeignKey => Problem(StatusCodes.Status400BadRequest, "/problems/related-row-missing", "Related row missing", $"The {table} names a row that does not exist."),
            Constraint.TooLong => Problem(StatusCodes.Status400BadRequest, "/problems/value-too-long", "Value too long", $"A value is too long for the {table}."),
            Constraint.Required => Problem(StatusCodes.Status400BadRequest, "/problems/value-required", "Value required", $"A required value of the {table} is missing."),
            _ => Problem(StatusCodes.Status500InternalServerError, "/problems/database-refused", "The database refused the change", "The change could not be saved.")
        };
    }

    /// <summary> <see cref="Describe"/> as an answer, for a handler that catches the exception itself. </summary>
    public static IResult ToResult(DbUpdateException error) => Results.Problem(Describe(error));

    private static Constraint? ConstraintOf(Exception? error)
    {
        if (error is not Microsoft.Data.Sqlite.SqliteException database)
            return null;
        return database.SqliteExtendedErrorCode switch
        {
            UniqueConstraint or PrimaryKeyConstraint => Constraint.Unique,
            ForeignKeyConstraint => Constraint.ForeignKey,
            NotNullConstraint => Constraint.Required,
            _ => null
        };
    }

    // the table of the row that was being saved, in words: "SalesInvoiceLine" is "sales invoice line"
    private static string TableOf(DbUpdateException error) =>
        Words(error.Entries.Select(entry => entry.Metadata.GetTableName()).FirstOrDefault(name => name is not null) ?? "row");

    private static string Words(string name) => PascalWords().Replace(name.Replace('_', ' '), " ").ToLowerInvariant();

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex PascalWords();
}

/// <summary> Turns an exception no endpoint caught into problem details; the stack trace and the message of the inner exception (the database's own) are included only in Development. </summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problems, IHostEnvironment environment, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            DbUpdateConcurrencyException => ApiProblems.Problem(StatusCodes.Status409Conflict, "/problems/changed-by-someone-else", "Changed by someone else", "The row was changed or removed since it was read. Load it again."),
            DbUpdateException update => ApiProblems.Describe(update),
            // ASP.NET throws this for a body it cannot read when ThrowOnBadRequest is on (Development); in production it answers 400 itself
            BadHttpRequestException unreadable => ApiProblems.Problem(unreadable.StatusCode, "/problems/invalid-request", "Invalid request", "The request could not be read."),
            _ => ApiProblems.Problem(StatusCodes.Status500InternalServerError, "/problems/unexpected", "Something went wrong", environment.IsDevelopment() ? exception.Message : "The server could not finish the request.")
        };

        if (environment.IsDevelopment())
        {
            if (exception.InnerException is { } inner)
                problem.Extensions["cause"] = inner.Message;
            if (problem.Status == StatusCodes.Status500InternalServerError)
                problem.Extensions["trace"] = exception.ToString();
        }

        if (problem.Status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception on {Path}", http.Request.Path);
        else
            logger.LogWarning("{Title} on {Path}: {Detail}", problem.Title, http.Request.Path, problem.Detail);

        http.Response.StatusCode = problem.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem, Exception = exception });
    }
}
