using Mutate4Net.Coverage;

namespace Mutate4Net.Tests.Coverage;

public sealed class CoberturaLineCoverageParserTests
{
    [Fact]
    public void Parse_ReadsCoveredLinesFromCoberturaClassFilenames()
    {
        string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "mutate4net-coverage-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string coveragePath = System.IO.Path.Combine(directory, "coverage.cobertura.xml");
        File.WriteAllText(coveragePath, """
            <coverage>
              <packages>
                <package name="Sample">
                  <classes>
                    <class name="Sample.Calculator" filename="src/Sample/Calculator.cs">
                      <lines>
                        <line number="10" hits="0" />
                        <line number="11" hits="3" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """);

        try
        {
            var report = new CoberturaLineCoverageParser().Parse(coveragePath);

            Assert.False(report.Covers("src/Sample/Calculator.cs", 10));
            Assert.True(report.Covers("src/Sample/Calculator.cs", 11));
            Assert.True(report.Covers("Calculator.cs", 11));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

