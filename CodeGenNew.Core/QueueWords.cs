namespace CodeGenNew.Core;

/// <summary> The sentences of the queue routes, the same in every stack. </summary>
public static class QueueWords
{
    public const string NotClaimed = "Only a claimed row can be completed or failed.";
    public const string NotFailed = "Only a failed row can be retried.";
    public const string WrongStateType = ProblemTypes.Prefix + "queue-wrong-state";

    /// <summary> The default of <c>QueueMaxRetries</c> and the number of times a claim looks for the next pending row before it gives up (another worker took the one it found). </summary>
    public const int ClaimAttempts = 5;

    /// <summary> The most rows <c>GET failed</c> answers. </summary>
    public const int FailedRows = 200;
}
