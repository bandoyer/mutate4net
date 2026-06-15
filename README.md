# mutate4net

`mutate4net` is a single-file mutation tester for C# projects, ported from the mutate4java workflow.

It is intentionally narrow: point it at one production `.cs` file, let it discover safe mutation sites with Roslyn, run tests, and report killed, survived, timed-out, and uncovered mutants.

## Current Status

Implemented:

- `--scan` mutation discovery.
- Embedded `mutate4net-manifest` read/write.
- Differential selection from the embedded manifest.
- Line filtering with `--lines`.
- Project-aware Roslyn analysis for SDK-style project sources and project references.
- Baseline `dotnet test` execution.
- Cobertura coverage parsing and filtering.
- Coverlet-based coverage generation for default `dotnet test` runs.
- Isolated worker workspaces.
- Parallel mutant execution with `--max-workers`.
- Explicit test project selection and exclusion.
- Structured VSTest filters with zero-test detection.
- Mutator metadata and include/exclude mutator filters.
- Richer expression mutators for string literals/methods, numeric literals, compound assignments, increment/decrement operators, conditional branches, selected LINQ calls, and conservative statement removal.

Still maturing:

- Project discovery is conservative unless `--project` is supplied for ambiguous ownership.
- Coverage generation is still conservative and falls back to all-covered behavior when no Coverlet report can be produced.
- Integration test coverage is still small.
- Reporting and package polish are minimal.

## Build

```powershell
dotnet build mutate4net.sln
dotnet test mutate4net.sln
```

## Install As A Tool

Build a local package:

```powershell
dotnet pack src/Mutate4Net/Mutate4Net.csproj --configuration Release -o artifacts/packages
```

Install it globally from the local package feed:

```powershell
dotnet tool install --global mutate4net --add-source artifacts/packages --version 0.1.0
mutate4net --version
```

For a repo-local tool manifest:

```powershell
dotnet new tool-manifest
dotnet tool install mutate4net --add-source artifacts/packages --version 0.1.0
dotnet tool run mutate4net -- --version
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

Restrict to one or more mutator categories or IDs:

```powershell
mutate4net path/to/File.cs --mutator boolean,logical
mutate4net path/to/File.cs --exclude-mutator null
```

Mutator-filtered runs are treated as partial smoke checks and do not update the embedded manifest. Current mutator categories include `arithmetic`, `assignment`, `boolean`, `conditional`, `equality`, `linq`, `literal`, `logical`, `null`, `statement`, `string`, `unary`, and `update`.

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

Prefer `--test-filter` when you only need a VSTest filter. mutate4net keeps the generated `dotnet test` command compatible with coverage, worker path remapping, and multi-project test selection:

```powershell
mutate4net path/to/File.cs --test-project tests/App.Unit/App.Unit.csproj --test-filter "FullyQualifiedName~CalculatorTests"
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

If `dotnet test` reports that a command ran zero tests, mutate4net treats that as a failed test command. This prevents empty filters from appearing as survived mutants.

## Worker Copy Tuning

mutate4net copies the module root into `.mutate4net/workers/run-*/worker-*` before applying mutants. It always skips common heavy directories such as `bin`, `obj`, `.git`, `.vs`, `.mutate4net`, `node_modules`, `packages`, `artifacts`, `coverage`, `dist`, hidden tool directories, temp folders, logs, and Stryker output.

For large repositories, add a `.mutate4netignore` file at the module root to skip additional files or directories from worker copies:

```text
# comments and blank lines are ignored
docs/
scripts/generated/
*.tmp
src/**/Generated.cs
```

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

## Release Checklist

1. Update `Version` in `src/Mutate4Net/Mutate4Net.csproj` and add a `CHANGELOG.md` entry.
2. Run `dotnet test mutate4net.sln --configuration Release`.
3. Run `dotnet pack src/Mutate4Net/Mutate4Net.csproj --configuration Release -o artifacts/packages`.
4. Install the produced package in a clean repo and verify `mutate4net --version`.
5. Tag the commit as `v<version>` after the package has been validated.
