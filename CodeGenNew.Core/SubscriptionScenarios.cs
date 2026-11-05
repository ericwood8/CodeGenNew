namespace CodeGenNew.Core;

/// <summary> The subscription scenarios the generated HTTP tests check, named once: the ASP.NET, Python and Rust test writers take their test names from here, and a test compares what each one wrote
/// with the stacks listed, so a stack that is behind (or has a scenario the list does not know) is found when the tests are generated and not when someone reads three files side by side.
/// The desktop guard tests check the same rules one layer down and have their own scenarios. </summary>
public sealed record SubscriptionScenario(string Key, string Sentence, IReadOnlyList<string> Stacks)
{
    public const string Api = "API", Python = "Python", Rust = "Rust";

    public string CSharpName => char.ToUpperInvariant(Sentence[0]) + Sentence[1..].Replace(' ', '_');
    public string SnakeName => Sentence.Replace(' ', '_').ToLowerInvariant();
    public string PythonName => "test_" + SnakeName;

    public static string NoSubscriptionSentence(bool required) =>
        "a tenant with no subscription " + (required ? "cannot write when a subscription is required" : "is on no plan and may write");

    private static readonly string[] All = [Api, Python, Rust];

    public static IReadOnlyList<SubscriptionScenario> For(ProjectSettings project) =>
    [
        new("Paused", "writes are paused while the subscription is not active and reads go on", All),
        new("NoSubscription", NoSubscriptionSentence(project.SubscriptionRequired), All),
        new("Limit", "a create past the plan limit is refused until the limit is raised", All),
        new("Concurrent", "two creates at the same moment cannot both pass the plan limit", [Api]),
        new("Trial", "a trial ends at its period end", All),
        new("PlatformAdmin", "only a platform administrator changes a subscription", All),
        new("Renewal", "a renewal adds a period that starts when the old one ends", All),
        new("PlanChoice", "an administrator chooses a plan from the list unless it allows less than the tenant has", All),
        new("AdminSees", "the administrator sees the subscription and a member does not", All),
        new("ViewPlans", "the view lists every plan with what it allows and marks the tenants own", All),
        new("Webhook", "the webhook sets the status of the subscription the gateway names and refuses what is not signed", All)
    ];

    /// <summary> The <c>§NAME_KEY</c> tokens of every scenario, each with the test name <paramref name="name"/> gives it, for a writer that fills them into its text. </summary>
    public static (string Token, string Value)[] Tokens(ProjectSettings project, Func<SubscriptionScenario, string> name) =>
        For(project).Select(s => ("§NAME_" + s.Key.ToUpperInvariant(), name(s))).ToArray();

    public static SubscriptionScenario Get(ProjectSettings project, string key) => For(project).Single(s => s.Key == key);
}
