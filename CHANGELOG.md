# Changelog

## 0.1.0

Initial mutate4net port.

- Added single-file C# mutation scanning with Roslyn semantic checks.
- Added embedded `mutate4net-manifest` support for differential runs.
- Added default `dotnet test` execution, Coverlet/Cobertura coverage filtering, and custom test commands.
- Added explicit production project ownership with `--project`.
- Added selected and excluded test projects with `--test-project` and `--exclude-test-project`.
- Added isolated worker copies, parallel execution, timeout handling, and cleanup hardening.
- Added killed, survived, timed-out, uncovered, and scan report output.
