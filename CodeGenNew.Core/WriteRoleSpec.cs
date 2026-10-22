namespace CodeGenNew.Core;

/// <summary> The generated spec of a screen whose table has write roles (<see cref="WriteRolePlan"/>): a role that may write sees Add New, any other role does not. The sign-in is the one the Auth group
/// keeps in the tab's session storage. Uses only describe, it and expect, so it runs under Jasmine and under Vitest. </summary>
public static class WriteRoleSpec
{
    public static string StorageKey(ProjectSettings project) => (project.ProjectName ?? "app").ToLowerInvariant() + ".auth";

    /// <summary> The role a spec signs in as: one that may write (the administrator role is first, a second one is a plainer role the table also allows). </summary>
    public static string AllowedRole(IReadOnlyList<string> roles) => (roles.Count > 1 ? roles[1] : roles[0]).Replace("'", "\\'");

    /// <summary> The line a screen's own spec starts its beforeEach with, so the Edit button (shown to a role that may write) is on the page. </summary>
    public static string SignIn(ProjectSettings project, IReadOnlyList<string> roles) =>
        $"    sessionStorage.setItem('{StorageKey(project)}', JSON.stringify({{ token: 'abc', expiresUtc: '2999-01-01T00:00:00Z', user: {{ name: 'Pat', role: '{AllowedRole(roles)}' }} }}));\n";

    /// <summary> The describe block to append after the screen's own; <paramref name="roles"/> is <see cref="WriteRolePlan.For(ProjectSettings, string)"/> (the administrator role first). </summary>
    public static string For(ProjectSettings project, string screenClass, IReadOnlyList<string> roles)
    {
        return """

describe('§C write roles', () => {
  const signIn = (role: string) => sessionStorage.setItem('§K', JSON.stringify({ token: 'abc', expiresUtc: '2999-01-01T00:00:00Z', user: { name: 'Pat', role } }));

  async function open(role: string): Promise<HTMLElement> {
    sessionStorage.clear();
    signIn(role);
    await TestBed.configureTestingModule({
      imports: [§C],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])]
    }).compileComponents();
    const fixture = TestBed.createComponent(§C);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const hasAdd = (page: HTMLElement) => Array.from(page.querySelectorAll('button')).some((b) => (b.textContent ?? '').includes('Add New'));

  it('shows Add New to a role that may write', async () => {
    expect(hasAdd(await open('§A'))).toBe(true);
    sessionStorage.clear();
  });

  it('hides Add New from a role that may not write', async () => {
    expect(hasAdd(await open('NoSuchRole'))).toBe(false);
    sessionStorage.clear();
  });
});

""".Replace("§C", screenClass).Replace("§K", StorageKey(project)).Replace("§A", AllowedRole(roles));
    }
}
