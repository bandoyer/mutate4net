# mutate4net Porting Plan

## Source Baseline

Port from `bandoyer/mutate4java` `main` at commit `7b05fdd71e8fe36327aff837806dfbff86af0572`.

Primary source references:

- Repository: https://github.com/bandoyer/mutate4java
- README behavior contract: https://github.com/bandoyer/mutate4java/blob/main/README.md
- Specification: https://github.com/bandoyer/mutate4java/blob/main/spec.md

The Java implementation is a single-file mutation tester. It accepts one Java source file, discovers AST-based mutation sites, runs baseline tests with coverage, filters uncovered lines, executes mutants in isolated worker copies, reports killed/survived/timed-out/uncovered results, and writes an embedded manifest footer for differential future runs.

The .NET port should preserve that product shape while replacing Java-specific technology with native .NET equivalents:

- Java compiler tree APIs -> Roslyn syntax and semantic APIs.
- Maven module discovery -> nearest `.csproj` or `.sln`/project ownership discovery.
- JaCoCo XML coverage -> Coverlet/Cobertura XML coverage.
- `mvn test` -> `dotnet test`.
- JUnit `no-mutate` tag exclusion -> VSTest filter convention for category/trait exclusion.

## Porting Goal

Create `mutate4net`, a standalone .NET CLI mutation-testing tool for C# projects that initially conforms to the mutate4java workflow:

- Target exactly one `.cs` source file.
- Discover mutation sites from the C# AST and semantic model.
- Optionally filter by source lines.
- Optionally use line coverage to skip uncovered mutation sites.
- Optionally use an embedded manifest to restrict work to changed declaration scopes.
- Execute selected mutants by editing isolated worker copies.
- Report killed, survived, timed-out, and uncovered sites.
- Update the embedded manifest after successful clean runs.

## Initial Non-Goals

- Whole-project or whole-directory mutation.
- VB/F#/Razor mutation.
- Mutation of generated files or test source files.
- IDE integration.
- Stable JSON output.
- Replacing mature tools such as Stryker.NET feature-for-feature.

## Proposed Solution Layout

```text
mutate4net.sln
src/
  Mutate4Net/
    Mutate4Net.csproj
    Program.cs
    Cli/
    Analysis/
    Coverage/
    Engine/
    Execution/
    Manifest/
    Model/
    ProjectSystem/
    Reporting/
    Selection/
tests/
  Mutate4Net.Tests/
  Mutate4Net.IntegrationTests/
samples/
  BasicCoveredProject/
  BasicUncoveredProject/
```

Keep the same conceptual package boundaries as the Java repo so behavior can be ported class-by-class where it makes sense, while using idiomatic C# records, services, and async process execution.

## Java-to-.NET Component Map

| mutate4java area | mutate4net replacement |
| --- | --- |
| `cli` | `System.CommandLine` or a small internal parser matching the Java options exactly. Prefer internal parser first for parity tests. |
| `analysis` | Roslyn `CSharpSyntaxTree`, `CSharpCompilation`, `SemanticModel`, `CSharpSyntaxWalker`. |
| `coverage` | Coverlet-generated Cobertura parser plus coverage runner around `dotnet test`. |
| `engine` | Orchestrates baseline, coverage, selection, workers, reporting, manifest updates. |
| `exec` | Async process runner, timeout handling, worker workspace copier/cleaner, parallel worker pool. |
| `manifest` | Embedded C# footer comment parser/writer with `mutate4net-manifest` sentinel. |
| `model` | C# records for mutation site, scope, result, coverage site/report, manifest, execution context. |
| `project` | `.csproj`/`.sln` discovery, source path normalization, test source exclusion, worker copy layout. |
| `report` | Console report formatter preserving mutate4java output shape with C# paths. |
| `selection` | Line filter, coverage filter, changed-scope selector, scan report formatter. |

## CLI Contract

Match the existing commands, renamed for C#:

```text
mutate4net path/to/File.cs
mutate4net path/to/File.cs --scan
mutate4net path/to/File.cs --update-manifest
mutate4net path/to/File.cs --reuse-coverage
mutate4net path/to/File.cs --lines 12,18
mutate4net path/to/File.cs --since-last-run
mutate4net path/to/File.cs --mutate-all
mutate4net path/to/File.cs --mutation-warning 50
mutate4net path/to/File.cs --max-workers 4
mutate4net path/to/File.cs --timeout-factor 15
mutate4net path/to/File.cs --test-command "dotnet test --filter Category!=no-mutate"
mutate4net path/to/File.cs --verbose
mutate4net --help
```

Validation parity:

- Accept exactly one `.cs` target file.
- Reject directories, zero files, multiple files, non-`.cs` files, and test-source targets.
- Preserve conflict rules from mutate4java: `--scan`, `--update-manifest`, `--lines`, `--since-last-run`, `--mutate-all`, and `--reuse-coverage` combinations should behave the same way.

Exit code parity:

- `0`: success, scan success, manifest update success, no covered mutants, or all executed mutants killed.
- `1`: CLI usage error.
- `2`: baseline failed.
- `3`: at least one mutant survived.

## C# Mutation Set

Implement the Java mutation set first, adapted to C# syntax and types:

- Boolean literals: `true` <-> `false`.
- Equality and comparison: `==`, `!=`, `<`, `<=`, `>`, `>=`.
- Arithmetic: `+` <-> `-`, `*` <-> `/`.
- Conditional boolean operators: `&&` <-> `||`.
- Unary operators: `!expr` -> `expr`, `-expr` -> `expr`.
- Integer constants: `0` <-> `1`.
- Reference or nullable-valued rvalues: replace with `null`.

Use Roslyn semantic checks to avoid low-quality mutations:

- Only mutate `+` to `-` when the binary expression is numeric, not string concatenation.
- Only mutate unary `-` when the operand is numeric.
- Only replace expressions with `null` when the expression type is a reference type or nullable value type and the mutation is syntactically valid.
- Avoid comments, strings, chars, interpolated string text, XML docs, generic angle brackets, attributes unless intentionally supported later.

## Scope and Manifest Rules

Use declaration-level scopes equivalent to mutate4java:

- Class, record, struct, interface declarations.
- Methods, constructors, local functions, operators, conversion operators.
- Properties, indexers, accessors.
- Field initializers and event initializers.
- Static and instance constructors/initializers.

Manifest format:

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

Rules:

- Strip the manifest before parsing, mutation discovery, scope discovery, and hashing.
- Use stable scope ids based on declaration identity, not only line numbers.
- Hash normalized source spans for scope semantic hashes.
- Hash the aggregate scope data for `moduleHash`.
- Write the manifest after successful clean mutation runs, after successful runs with no executed mutants, and for `--update-manifest`.
- Do not write the manifest after baseline failure, surviving mutants, or scan mode.

## Project and Test Execution Model

Project discovery:

- Find the nearest ancestor `.csproj` that includes the target file.
- If multiple projects include the file, fail with a clear message unless a future `--project` option is added.
- Treat the project directory as the initial module root.
- Detect test projects by SDK/package/name heuristics and reject mutation targets under test roots.

Default baseline command:

```text
dotnet test <owning-project-or-sln> --no-restore --filter "Category!=no-mutate&TestCategory!=no-mutate"
```

This filter should be verified across xUnit, NUnit, and MSTest sample projects. If it proves too framework-specific, expose a documented `--test-command` escape hatch and keep the default simple.

Coverage command:

Prefer Coverlet collector or MSBuild integration that produces deterministic Cobertura XML under a known tool-owned output directory, for example:

```text
dotnet test <project> --no-restore \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=cobertura \
  /p:CoverletOutput=<module>/.mutate4net/coverage/coverage
```

Coverage rules:

- Parse Cobertura XML at line granularity.
- Match coverage file paths against the target file using normalized relative and absolute path forms.
- If `--reuse-coverage` is set, reuse the existing Cobertura XML if present.
- If `--test-command` is supplied, treat sites as covered unless a future explicit `--coverage-file` option is added.

## Worker Model

Use the same isolation idea as mutate4java:

```text
<module>/.mutate4net/workers/run-<id>/worker-N/
```

Each worker should:

- Copy the owning project/module tree.
- Exclude `bin`, `obj`, `.git`, `.vs`, `.idea`, `TestResults`, and `.mutate4net`.
- Apply one mutation at a time to the worker copy of the target file.
- Run the test command from the worker root.
- Restore the worker file before the next job.
- Treat timeout as killed.

Default workers:

- `max(1, Environment.ProcessorCount / 2)`, capped by selected mutation count.

Timeout:

- Baseline duration multiplied by `--timeout-factor`, default `10`.
- Enforce a sane minimum floor so extremely fast baselines do not produce unusably tiny mutant timeouts.

## Implementation Phases

### Phase 0: Repo Skeleton

- Create solution and projects.
- Add nullable, analyzers, deterministic builds, and formatting config.
- Add test projects and a first smoke test.
- Decide target framework: use a supported LTS target, with multi-targeting only if there is a concrete compatibility need.

### Phase 1: CLI and Models

- Port CLI argument model and validation rules.
- Create immutable records for mutation sites, scopes, coverage reports, manifests, test runs, and mutation results.
- Implement usage text and exit codes.
- Add parser parity tests from mutate4java test cases.

### Phase 2: Roslyn Analysis

- Parse a single `.cs` file after stripping any embedded manifest.
- Build a Roslyn compilation using the owning project via MSBuildWorkspace or a minimal project loader.
- Implement mutation site discovery with semantic checks.
- Implement source spans, line numbers, and source replacement text.
- Implement scope discovery and stable scope ids.
- Add unit tests for each mutation operator and C# exclusion case.

### Phase 3: Manifest Support

- Implement manifest strip/parse/serialize/write.
- Implement SHA-256 hashing and value escaping.
- Implement changed-scope detection.
- Add tests for round trip, malformed manifests, unchanged module, changed registered scope, and new/unregistered scope.

### Phase 4: Project Discovery and Process Execution

- Find owning project/root for a target `.cs` file.
- Normalize paths for Windows, Linux, and macOS.
- Implement async process runner with output capture and timeout.
- Implement default `dotnet test` command and custom `--test-command`.
- Add unit tests with fake process executor and integration tests with tiny sample projects.

### Phase 5: Coverage

- Generate Coverlet/Cobertura coverage during baseline.
- Parse Cobertura XML into covered source lines.
- Implement `--reuse-coverage`.
- Implement coverage filtering and uncovered reporting.
- Add samples with covered and uncovered mutation sites.

### Phase 6: Selection and Scan Mode

- Implement default differential selection behavior.
- Implement `--lines`, `--since-last-run`, and `--mutate-all`.
- Implement scan output with `*` for changed scopes.
- Add tests matching mutate4java report semantics.

### Phase 7: Mutation Execution

- Apply mutations safely by source span.
- Implement worker workspace copy/cleanup.
- Implement parallel worker pool.
- Implement killed, survived, timed-out result classification.
- Ensure original workspace is never left mutated, even on failure.
- Add integration tests that prove killed, survived, and timeout outcomes.

### Phase 8: Reporting and Polish

- Match mutate4java's console report shape, adjusted to `mutate4net`.
- Add warning threshold behavior.
- Add verbose worker progress.
- Add README usage, examples, and workflow recommendation.
- Add GitHub Actions CI for build, unit tests, and integration tests.
- Package as a .NET tool.

## Acceptance Criteria

The first conforming version should satisfy these scenarios:

- `mutate4net --help` prints usage and exits `0`.
- Invalid CLI combinations exit `1`.
- `--scan` lists mutation sites without running tests or writing a manifest.
- `--update-manifest` writes a `mutate4net-manifest` footer without running tests.
- A red baseline exits `2` and does not mutate or write a manifest.
- Covered killed mutants exit `0`.
- Any surviving mutant exits `3`.
- Uncovered mutation sites are reported and skipped.
- `--lines` restricts execution to requested lines.
- A second run with unchanged manifest executes zero mutants.
- A changed scope with an existing manifest mutates only that scope.
- Parallel workers never modify the original source file and do not collide with each other.

## Key Risks and Decisions

- Roslyn project loading can be slower and more environment-sensitive than parsing a single file. Start with `MSBuildWorkspace` for correct semantics, then optimize if startup cost hurts.
- Coverlet output paths vary by integration mode. Force a tool-owned output directory and test on Windows path separators early.
- Test filter syntax differs across xUnit, NUnit, and MSTest conventions. Keep `--test-command` prominent and verify a least-common-denominator default.
- `null` replacement in nullable-aware C# needs semantic caution. Start conservative to avoid generating uncompilable mutants.
- Worker copying can be expensive for large repos. MVP should copy the owning project tree; later versions can add smarter sparse copy or build-output reuse.
- Generated files and source generators complicate source ownership. MVP should target physical `.cs` files only and skip generated-code patterns.

## Suggested Milestones

1. CLI + Roslyn scan mode over a single `.cs` file.
2. Manifest write/read + differential scan.
3. Baseline `dotnet test` + mutation execution serially.
4. Coverlet coverage filtering.
5. Parallel isolated workers.
6. Packaging as `mutate4net` local/global .NET tool.

