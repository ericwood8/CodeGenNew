"""Reads every table of a generated app through its generated DbContext, to find a model EF Core cannot build or a column it cannot read.

  python ef_read_all.py --app C:/Github/CodeGenNew/Output/AvatarDev.App [--scratch DIR] [--ef-version 10.0.12]

Compiles the app's Entities, Data and Repositories folders (not the screens) into a throwaway console project, uses the app's appsettings.json connection string, and calls ToListAsync
on each DbSet of the context. Prints `FAIL <DbSet>: <first line>` for a table that fails and ends with `tables ok=N fail=M`. It finds what a build cannot: a relationship that targets
the wrong key (InvalidOperationException when the model is built), a NULL in a column EF treats as required (SqlNullValueException), a missing column or table. It only reads.
The scratch folder defaults to a new temp folder. The context class and namespace are found from the generated Data folder.
"""
import argparse
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path


def fix_path(path):
    m = re.match(r"^/([a-zA-Z])/(.*)$", path) if os.name == "nt" else None
    return f"{m.group(1).upper()}:/{m.group(2)}" if m else path


PROGRAM = """global using Microsoft.EntityFrameworkCore;
global using System.ComponentModel.DataAnnotations;
global using System.ComponentModel.DataAnnotations.Schema;
global using {ns}.Data;
global using {ns}.Entities;
global using {ns}.Repositories;

using var ctx = new {context}();
int fail = 0, ok = 0;
foreach (var p in typeof({context}).GetProperties().Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)))
{{
    var set = p.GetValue(ctx)!;
    var m = typeof(EntityFrameworkQueryableExtensions).GetMethod("ToListAsync")!.MakeGenericMethod(p.PropertyType.GetGenericArguments()[0]);
    try {{ await (Task)m.Invoke(null, [set, default(CancellationToken)])!; ok++; }}
    catch (Exception e) {{ fail++; Console.WriteLine("FAIL " + p.Name + ": " + (e.InnerException ?? e).Message.Split('\\n')[0]); }}
}}
Console.WriteLine($"tables ok={{ok}} fail={{fail}}");
"""

PROJECT = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.cs" />
    <Compile Include="{app}/Entities/**/*.cs" />
    <Compile Include="{app}/Enums/**/*.cs" />
    <Compile Include="{app}/Data/**/*.cs" />
    <Compile Include="{app}/Repositories/**/*.cs" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="{ef}" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="{ef}" />
  </ItemGroup>
  <ItemGroup><None Include="{app}/appsettings.json" CopyToOutputDirectory="PreserveNewest" /></ItemGroup>
</Project>
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--app", required=True)
    parser.add_argument("--scratch")
    parser.add_argument("--ef-version", default="10.0.12")
    args = parser.parse_args()
    app = Path(fix_path(args.app))
    contexts = [(f, m) for f in (app / "Data").glob("*.cs") if (m := re.search(r"class\s+(\w+)\s*:\s*DbContext", f.read_text(encoding="utf-8-sig")))]
    if not contexts:
        sys.exit("no DbContext class found in " + str(app / "Data"))
    namespace = re.search(r"namespace\s+([\w.]+)", contexts[0][0].read_text(encoding="utf-8-sig")).group(1).removesuffix(".Data")
    scratch = Path(fix_path(args.scratch)) if args.scratch else Path(tempfile.mkdtemp(prefix="ef_read_all_"))
    scratch.mkdir(parents=True, exist_ok=True)
    (scratch / "h.csproj").write_text(PROJECT.format(app=app.as_posix(), ef=args.ef_version), encoding="utf-8")
    (scratch / "Program.cs").write_text(PROGRAM.format(ns=namespace, context=contexts[0][1].group(1)), encoding="utf-8")
    sys.exit(subprocess.run(["dotnet", "run", "--project", str(scratch / "h.csproj")]).returncode)


if __name__ == "__main__":
    main()
