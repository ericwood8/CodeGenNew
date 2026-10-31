namespace CodeGenNew.Core;

/// <summary> The members page of the Uno app (<c>TenantMembership</c> with the sign-in): the same page as the WinUI3 one (<see cref="DesktopMembersCode"/>), reading and changing the memberships over the API routes
/// <c>/api/auth/members</c> instead of the database. The page is in the menu for administrators. </summary>
public static class UnoMembersCode
{
    public static string Code(string viewNamespace, string servicesNamespace, bool hasRole) => Fill(CodeText, ("§NS", viewNamespace), ("§SERVICES", servicesNamespace),
        ("§ROLEEDIT", hasRole ? Fill(RoleEditText, RoleTokens) : ""), ("§ROLEBODY", hasRole ? ", role = newRole" : ""), ("§ROLEBOX", hasRole ? Fill(RoleBoxText, RoleTokens) : ""), ("§ROLEPANEL", hasRole ? ", roleBox" : ""),
        ("§ROLEREAD", hasRole ? "\n        string newRole = roleBox.Text.Trim();" : ""),
        ("§MEMBERSPATH", PeopleWords.MembersPath), ("§CANDIDATESPATH", PeopleWords.CandidatesPath), ("§NOBODY", MemberWords.NobodyToAdd), ("§ADMINS", PeopleWords.MembersAdminsOnlyDetail),
        ("§EXISTS", PeopleWords.MemberExistsDetail), ("§SELF", PeopleWords.MemberSelfDetail), ("§PERSONLABEL", MemberWords.PersonLabel), ("§ROLELABEL", MemberWords.RoleLabel), ("§ROLEHINT", MemberWords.RoleHint),
        ("§CONFLICT", HttpStatus.Conflict.ToString()), ("§FORBIDDEN", HttpStatus.Forbidden.ToString()));

    private static readonly (string Token, string Value)[] RoleTokens =
    [
        ("§ROLELABEL", MemberWords.RoleLabel), ("§ROLEHINT", MemberWords.RoleHint), ("§MEMBERSPATH", PeopleWords.MembersPath)
    ];

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string RoleBoxText = "\n        var roleBox = new TextBox { Header = \"§ROLELABEL\", PlaceholderText = \"§ROLEHINT\" };";

    private const string RoleEditText = """

    private async void OnEditRoleClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not int personId)
            return;
        var row = Rows.FirstOrDefault(r => r.PersonId == personId);
        var box = new TextBox { Header = "§ROLELABEL", Text = row?.Role ?? "", PlaceholderText = "§ROLEHINT" };
        var dialog = new ContentDialog
        {
            Title = "Edit role",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = new StackPanel { Spacing = 12, MinWidth = 320, Children = { box } }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        try
        {
            var response = await _api.Http.PutAsJsonAsync($"§MEMBERSPATH/{personId}", new { role = box.Text.Trim() });
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Show(ex.Message);
            return;
        }
        await LoadAsync();
    }
""";

    private const string CodeText = """
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using §SERVICES;

namespace §NS;

/// <summary> One member of the signed-in tenant: the key and the sign-in name of the person, the text that names them and their role in the tenant. </summary>
public sealed class MemberRow
{
    public MemberRow(int personId, string login, string person, string? role)
    {
        PersonId = personId;
        Login = login;
        Person = person;
        Role = role;
    }

    public int PersonId { get; set; }
    public string Login { get; set; }
    public string Person { get; set; }
    public string? Role { get; set; }
}

/// <summary> A person who is not yet a member of the signed-in tenant. </summary>
public sealed record MemberCandidate(int Id, string Name);

// A plain Page: the menu shows it to administrators. Add member, Edit role and Remove call the members routes of the API.
public sealed partial class MembersPage : Page
{
    private readonly ApiClients _api;

    public ObservableCollection<MemberRow> Rows { get; } = [];

    public MembersPage(ApiClients api)
    {
        _api = api;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Problem.IsOpen = false;
        Rows.Clear();
        try
        {
            var members = await _api.Http.GetFromJsonAsync<List<MemberRow>>("§MEMBERSPATH") ?? [];
            foreach (var member in members)
                Rows.Add(member);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            Show("§ADMINS");
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        List<MemberCandidate> candidates;
        try
        {
            candidates = await _api.Http.GetFromJsonAsync<List<MemberCandidate>>("§CANDIDATESPATH") ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            Show("§ADMINS");
            return;
        }
        if (candidates.Count == 0)
        {
            Show("§NOBODY");
            return;
        }
        var picker = new ComboBox { Header = "§PERSONLABEL", ItemsSource = candidates, DisplayMemberPath = "Name", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };§ROLEBOX
        var dialog = new ContentDialog
        {
            Title = "Add member",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = new StackPanel { Spacing = 12, MinWidth = 320, Children = { picker§ROLEPANEL } }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || picker.SelectedItem is not MemberCandidate chosen)
            return;§ROLEREAD
        try
        {
            var response = await _api.Http.PostAsJsonAsync("§MEMBERSPATH", new { personId = chosen.Id§ROLEBODY });
            if ((int)response.StatusCode == §CONFLICT)
            {
                Show("§EXISTS");
                return;
            }
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Show(ex.Message);
            return;
        }
        await LoadAsync();
    }
§ROLEEDIT
    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not int personId)
            return;
        try
        {
            var response = await _api.Http.DeleteAsync($"§MEMBERSPATH/{personId}");
            if ((int)response.StatusCode == §FORBIDDEN)
            {
                Show("§SELF");
                return;
            }
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Show(ex.Message);
            return;
        }
        await LoadAsync();
    }

    private void Show(string message)
    {
        Problem.Message = message;
        Problem.IsOpen = true;
    }
}
""";
}
