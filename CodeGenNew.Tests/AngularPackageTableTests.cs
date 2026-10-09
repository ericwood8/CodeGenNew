using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The one table of package ranges, builders and test runner per Angular major version (AngularVersions), and the build files TS_EssentialBuild and the config TS_EssentialConfig write from it. </summary>
[TestClass]
public class AngularPackageTableTests
{
    private static async Task<Dictionary<string, string>> Run(string template, params (string Key, string Value)[] values)
    {
        var project = ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));
        var result = await Repo.Cache.RunAsync(Repo.Template(template), project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
    }

    [TestMethod]
    public void Each_version_gets_the_ranges_its_compiler_and_runner_need()
    {
        var v18 = AngularVersions.For(18);
        Assert.AreEqual("~0.14.10", v18.ZoneJs);
        Assert.AreEqual("~5.5.4", v18.TypeScript);
        Assert.AreEqual("^18.0.0", v18.Material);
        Assert.AreEqual(AngularTestRunner.Karma, v18.Runner);
        Assert.AreEqual("@angular-devkit/build-angular:karma", v18.TestBuilder);
        Assert.IsFalse(v18.GlobalErrorListeners);
        Assert.IsTrue(v18.OutputPathRequired);
        Assert.IsFalse(v18.ModulePreserve);

        var v19 = AngularVersions.For(19);
        Assert.AreEqual("~0.15.0", v19.ZoneJs);
        Assert.AreEqual("~5.6.3", v19.TypeScript);
        Assert.AreEqual("@angular-devkit/build-angular:application", v19.ApplicationBuilder);
        Assert.IsTrue(v19.OutputPathRequired);
        Assert.IsFalse(v19.ModulePreserve);
        Assert.IsTrue(v19.PlatformBrowserDynamic);
        Assert.IsFalse(AngularVersions.For(20).PlatformBrowserDynamic);

        var v20 = AngularVersions.For(20);
        Assert.AreEqual("~5.8.2", v20.TypeScript);
        Assert.AreEqual("@angular/build:karma", v20.TestBuilder);
        Assert.IsTrue(v20.GlobalErrorListeners);
        Assert.IsTrue(v20.ModulePreserve);

        var v22 = AngularVersions.For(22);
        Assert.AreEqual("~0.16.0", v22.ZoneJs);
        Assert.AreEqual("~6.0.2", v22.TypeScript);
        Assert.AreEqual(AngularTestRunner.Vitest, v22.Runner);
        Assert.AreEqual("@angular/build:unit-test", v22.TestBuilder);
        Assert.IsFalse(v22.AnimationsPackage);
        Assert.IsTrue(v22.ToastrNeedsPeerOverride);

        Assert.AreEqual(18, AngularVersions.For(10).Version);   // older than the oldest known takes the oldest row
        Assert.AreEqual("~6.0.2", AngularVersions.For(30).TypeScript);   // newer than the newest known takes the newest row
        Assert.AreEqual("^30.0.0", AngularVersions.For(30).Material);
    }

    [TestMethod]
    public async Task Angular_18_gets_a_package_json_it_can_install_and_a_karma_test_builder()
    {
        var files = await Run("TS_EssentialBuild_v1.tt", ("AngularVersion", "18"));

        using var json = System.Text.Json.JsonDocument.Parse(files["package.json"]);
        var dependencies = json.RootElement.GetProperty("dependencies");
        Assert.AreEqual("~0.14.10", dependencies.GetProperty("zone.js").GetString());
        Assert.AreEqual("^18.0.0", dependencies.GetProperty("@angular/material").GetString());
        Assert.AreEqual("^18.0.0", dependencies.GetProperty("@angular/animations").GetString());
        Assert.AreEqual("^18.0.0", dependencies.GetProperty("@angular/platform-browser-dynamic").GetString());
        var dev = json.RootElement.GetProperty("devDependencies");
        Assert.AreEqual("~5.5.4", dev.GetProperty("typescript").GetString());
        Assert.AreEqual("^18.0.0", dev.GetProperty("@angular-devkit/build-angular").GetString());
        Assert.IsTrue(dev.TryGetProperty("karma", out _));
        Assert.IsFalse(dev.TryGetProperty("vitest", out _));
        Assert.IsFalse(dev.TryGetProperty("@angular/build", out _));

        string angular = files["angular.json"];
        using var angularJson = System.Text.Json.JsonDocument.Parse(angular);
        Expect.Contains(angular, "\"builder\": \"@angular-devkit/build-angular:application\"");
        Expect.Contains(angular, "\"builder\": \"@angular-devkit/build-angular:dev-server\"");
        Expect.Contains(angular, "\"builder\": \"@angular-devkit/build-angular:karma\"");
        Expect.Contains(angular, "\"zone.js/testing\"");
        Expect.Contains(angular, "\"outputPath\": \"dist/frontend\"");
        Expect.Contains(angular, "\"index\": \"src/index.html\"");
        Expect.Contains(files["tsconfig.spec.json"], "\"jasmine\"");
        Expect.Contains(files["tsconfig.json"], "\"module\": \"ES2022\"");
        Expect.Contains(files["tsconfig.json"], "\"moduleResolution\": \"bundler\"");
        Expect.DoesNotContain(files["tsconfig.json"], "preserve");
    }

    [TestMethod]
    public async Task Angular_19_and_20_keep_karma_and_20_moves_to_the_build_package()
    {
        var v19 = await Run("TS_EssentialBuild_v1.tt", ("AngularVersion", "19"));
        Expect.Contains(v19["package.json"], "\"zone.js\": \"~0.15.0\"");
        Expect.Contains(v19["package.json"], "\"typescript\": \"~5.6.3\"");
        Expect.Contains(v19["angular.json"], "\"outputPath\": \"dist/frontend\"");
        Expect.Contains(v19["package.json"], "\"@angular/platform-browser-dynamic\"");

        var v20 = await Run("TS_EssentialBuild_v1.tt", ("AngularVersion", "20"));
        Expect.Contains(v20["package.json"], "\"@angular/build\": \"^20.0.0\"");
        Expect.DoesNotContain(v20["package.json"], "build-angular");
        Expect.Contains(v20["package.json"], "\"karma\"");
        Expect.DoesNotContain(v20["angular.json"], "outputPath");
        Expect.DoesNotContain(v20["package.json"], "platform-browser-dynamic");
        Expect.Contains(v20["angular.json"], "\"builder\": \"@angular/build:karma\"");
        Expect.Contains(v20["tsconfig.json"], "\"module\": \"preserve\"");
        System.Text.Json.JsonDocument.Parse(v19["package.json"]);
        System.Text.Json.JsonDocument.Parse(v20["package.json"]);
    }

    [TestMethod]
    public async Task Angular_22_is_what_it_was_vitest_and_no_animations_package()
    {
        var files = await Run("TS_EssentialBuild_v1.tt");

        Expect.Contains(files["package.json"], "\"zone.js\": \"~0.16.0\"");
        Expect.Contains(files["package.json"], "\"vitest\"");
        Expect.DoesNotContain(files["package.json"], "karma");
        Expect.DoesNotContain(files["package.json"], "@angular/animations");
        Expect.Contains(files["angular.json"], "\"builder\": \"@angular/build:unit-test\"");
        Expect.Contains(files["tsconfig.spec.json"], "vitest/globals");
        System.Text.Json.JsonDocument.Parse(files["package.json"]);
        System.Text.Json.JsonDocument.Parse(files["angular.json"]);
    }

    [TestMethod]
    public async Task The_global_error_listeners_exist_from_Angular_20()
    {
        string v18 = (await Run("TS_EssentialConfig_v1.tt", ("AngularVersion", "18")))["app.config.ts"];
        Expect.DoesNotContain(v18, "provideBrowserGlobalErrorListeners");
        Expect.Contains(v18, "import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';");

        string v20 = (await Run("TS_EssentialConfig_v1.tt", ("AngularVersion", "20")))["app.config.ts"];
        Expect.Contains(v20, "provideBrowserGlobalErrorListeners(),");
    }

    [TestMethod]
    public async Task The_specs_of_the_bases_run_under_Jasmine_as_well_as_Vitest()
    {
        var files = await Run("TS_EssentialCrud_v1.tt");

        foreach (string spec in new[] { files["crud.service.spec.ts"], files["crud-screen.spec.ts"], files["api-error.spec.ts"] })
        {
            Expect.DoesNotContain(spec, "vi.");
            Expect.DoesNotContain(spec, "toHaveBeenCalled");
        }
    }
}
