# mutate4net

`mutate4net` is a single-file mutation tester for C# projects, ported from the mutate4java workflow.

It is intentionally narrow: point it at one production `.cs` file, let it discover safe mutation sites with Roslyn, run tests, and report killed, survived, timed-out, and uncovered mutants.

## Current Status

Implemented:

- `--scan` mutation discovery.
- Embedded `mutate4net-manifest` read/write.
- Differential selection from the embedded manifest.
- Line filtering with `--lines`.
- Baseline `dotnet test` execution.
- Cobertura coverage parsing and filtering.
- Coverlet-based coverage generation for default `dotnet test` runs.
- Isolated worker workspaces.
- Parallel mutant execution with `--max-workers`.
- Explicit test project selection and exclusion.

Still maturing:

- Project discovery is conservative and currently fails when multiple projects include the same source file.
- Coverage generation is still conservative and falls back to all-covered behavior when no Coverlet report can be produced.
- Integration test coverage is still small.
- Reporting and package polish are minimal.

## Build

```powershell
dotnet build mutate4net.sln
dotnet test mutate4net.sln
```

## Run From Source

```powershell
dotnet run --project src/Mutate4Net/Mutate4Net.csproj -- path/to/File.cs --scan
dotnet run --project src/Mutate4Net/Mutate4Net.csproj -- path/to/File.cs --update-manifest
dotnet run --project src/Mutate4Net/Mutate4Net.csproj -- path/to/File.cs
dotnet run --project src/Mutate4Net/Mutate4Net.csproj -- --version
```

## Commands

Scan without running tests:

```powershell
mutate4net path/to/File.cs --scan
```

Update the embedded manifest:

```powershell
mutate4net path/to/File.cs --update-manifest
```

Run mutation testing:

```powershell
mutate4net path/to/File.cs
```

Restrict to specific source lines:

```powershell
mutate4net path/to/File.cs --lines 12,18
```

Line-filtered runs are treated as partial smoke checks and do not update the embedded manifest.

Ignore the manifest and test all discovered sites:

```powershell
mutate4net path/to/File.cs --mutate-all
```

Limit parallel workers:

```powershell
mutate4net path/to/File.cs --max-workers 4
```

Choose the owning production project when a source file is included by more than one `.csproj`:

```powershell
mutate4net path/to/File.cs --project src/App/App.csproj
```

Use a custom test command. Custom commands currently treat all mutation sites as covered:

```powershell
mutate4net path/to/File.cs --test-command "dotnet test --filter Category!=no-mutate"
```

Run only selected test projects while still generating coverage:

```powershell
mutate4net path/to/File.cs --test-project tests/App.Unit/App.Unit.csproj --test-project tests/App.Functional/App.Functional.csproj
```

Discover test projects under the solution/root, but exclude one by project name or path:

```powershell
mutate4net path/to/File.cs --exclude-test-project CMS.Test.Browser
```

Reuse an existing coverage report at `.mutate4net/coverage/coverage.cobertura.xml`:

```powershell
mutate4net path/to/File.cs --reuse-coverage
```

## Coverage

For default mutation runs, mutate4net first runs `dotnet test` with Coverlet MSBuild properties and looks for:

```text
.mutate4net/coverage/coverage.cobertura.xml
```

When multiple test projects are selected, it writes one report per project and unions the covered lines. If no MSBuild-property report is produced, mutate4net retries coverage with `--collect "XPlat Code Coverage"` and reads any collector reports under `.mutate4net/coverage`.

If the report exists, uncovered mutation sites are reported and skipped. If coverage is unavailable, mutate4net currently treats all discovered sites as covered.

For many projects this means the test project should reference either `coverlet.msbuild` or `coverlet.collector`.

## Exit Codes

- `0`: success, scan success, manifest update success, or all executed mutants killed.
- `1`: CLI or analysis error.
- `2`: baseline tests failed.
- `3`: one or more mutants survived.

## Manifest Workflow

The manifest is a footer comment appended to source files:

```csharp
/* mutate4net-manifest
version=1
moduleHash=...
scope.0.id=...
scope.0.kind=method
scope.0.startLine=12
scope.0.endLine=20
scope.0.semanticHash=...
*/
```

When a manifest is present, mutate4net uses it to select only changed scopes. If nothing changed, no mutants are executed. Use `--mutate-all` to ignore the manifest.
