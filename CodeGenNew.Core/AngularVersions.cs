namespace CodeGenNew.Core;

/// <summary> How the unit tests of an Angular version run: Karma with Jasmine up to Angular 20, the Vitest unit-test builder from 21. </summary>
public enum AngularTestRunner { Karma, Vitest }

/// <summary> The package ranges and the builders one major version of Angular takes (the <c>package.json</c> and <c>angular.json</c> TS_EssentialBuild writes, the <c>app.config.ts</c> TS_EssentialConfig writes). </summary>
/// <param name="Version"> The major version of the <c>@angular</c> packages. </param>
/// <param name="ZoneJs"> The <c>zone.js</c> range that version runs with. </param>
/// <param name="TypeScript"> The TypeScript range the compiler of that version accepts. </param>
/// <param name="Runner"> How <c>ng test</c> runs. </param>
/// <param name="AnimationsPackage"> Angular Material up to 21 needs <c>@angular/animations</c> (22 dropped it). </param>
/// <param name="GlobalErrorListeners"> <c>provideBrowserGlobalErrorListeners</c> exists from Angular 20. </param>
/// <param name="BuildPackage"> The package that holds the builders: <c>@angular-devkit/build-angular</c> before Angular 20, <c>@angular/build</c> after. </param>
public sealed record AngularPackages(int Version, string ZoneJs, string TypeScript, AngularTestRunner Runner, bool AnimationsPackage, bool GlobalErrorListeners, string BuildPackage)
{
    public string Material => $"^{Version}.0.0";
    public string Cdk => $"^{Version}.0.0";

    /// <summary> The ngx-toastr range: 19 draws with <c>@angular/animations</c> and takes any Angular from 16; 20 does not, but names Angular 21 as its peer, which npm <c>overrides</c> relax for 22. </summary>
    public string Toastr => Version >= 21 ? "^20.0.5" : "^19.1.0";
    public bool ToastrNeedsPeerOverride => Version >= 22;

    /// <summary> The builder of <c>ng test</c> in angular.json. </summary>
    public string TestBuilder => Runner == AngularTestRunner.Vitest ? "@angular/build:unit-test" : Version >= 20 ? "@angular/build:karma" : "@angular-devkit/build-angular:karma";
    public string ApplicationBuilder => $"{BuildPackage}:application";
    public string DevServerBuilder => $"{BuildPackage}:dev-server";

    /// <summary> The application builder of Angular 18 (and 19) refuses an angular.json with no <c>outputPath</c> and no <c>index</c>; the <c>@angular/build</c> package of 20 made both optional. </summary>
    public bool OutputPathRequired => Version < 20;

    /// <summary> TypeScript's <c>module: preserve</c> is what <c>ng new</c> writes from Angular 20; the webpack Karma build of 18 and 19 loses the decorator helpers with it, so those take ES2022 and the bundler resolution. </summary>
    public bool ModulePreserve => Version >= 20;

    /// <summary> The Karma build of 18 and 19 starts its tests through <c>@angular/platform-browser-dynamic</c>; 20 deprecated it and its peer range clashes with a newer 20.x of the other packages. </summary>
    public bool PlatformBrowserDynamic => Runner == AngularTestRunner.Karma && Version < 20;
}

/// <summary> The one table of what each Angular major version needs, so a project on an older Angular can <c>npm install</c> and build what the templates write. A version below 18 gets
/// the 18 row (the oldest the templates are written for), one above the newest known gets the newest row. </summary>
public static class AngularVersions
{
    public const int Oldest = 18;
    public const int Newest = 22;

    public static AngularPackages For(int version) => version switch
    {
        <= 18 => new(18, "~0.14.10", "~5.5.4", AngularTestRunner.Karma, true, false, "@angular-devkit/build-angular"),
        19 => new(19, "~0.15.0", "~5.6.3", AngularTestRunner.Karma, true, false, "@angular-devkit/build-angular"),
        20 => new(20, "~0.15.1", "~5.8.2", AngularTestRunner.Karma, true, true, "@angular/build"),
        21 => new(21, "~0.16.0", "~5.9.2", AngularTestRunner.Vitest, true, true, "@angular/build"),
        _ => new(version, "~0.16.0", "~6.0.2", AngularTestRunner.Vitest, false, true, "@angular/build")
    };
}
