namespace CodeGenNew.Cli;

public class RetryOutcome
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public int AttemptsMade { get; init; }
}

/// <summary>
/// Generic retry-then-halt-and-beep helper (Docs/specs.md section 10.1). Used around the read-only
/// schema-introspection connection. Repeated failure across MaxAttempts tries usually means an
/// environmental problem (e.g. the database is unreachable), not a bug worth silently retrying
/// forever -- so it stops and gets a human's attention instead. This never wraps anything that writes
/// to the target database -- CodeGenNew is read-only against it, always (Docs/specs.md section 2).
/// </summary>
public static class RetryRunner
{
    private const int MaxAttempts = 5;

    public static async Task<RetryOutcome> RunAsync(string stepName, Func<Task<string>> action)
    {
        Exception? lastError = null;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                string message = await action();
                return new RetryOutcome { Success = true, Message = message, AttemptsMade = attempt };
            }
            catch (Exception ex)
            {
                lastError = ex;
                Console.Error.WriteLine($"[{stepName}] attempt {attempt}/{MaxAttempts} failed: {ex.Message}");
                if (attempt < MaxAttempts)
                    await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        for (int i = 0; i < 3; i++)
        {
            Console.Beep();
            await Task.Delay(200);
        }

        return new RetryOutcome
        {
            Success = false,
            Message = $"'{stepName}' failed {MaxAttempts} times in a row. Last error: {lastError?.Message}. " +
                      "This usually means the database is unreachable or another environmental issue -- check the connection before retrying.",
            AttemptsMade = MaxAttempts
        };
    }
}
