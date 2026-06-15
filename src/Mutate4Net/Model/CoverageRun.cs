namespace Mutate4Net.Model;

public sealed record CoverageRun(
    TestRun Baseline,
    CoverageReport Report,
    bool ReusedCoverage,
    bool ReportAvailable);

