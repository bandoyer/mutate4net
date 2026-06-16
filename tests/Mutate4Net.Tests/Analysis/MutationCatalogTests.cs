using Mutate4Net.Analysis;

namespace Mutate4Net.Tests.Analysis;

public sealed class MutationCatalogTests
{
    [Fact]
    public async Task AnalyzeAsync_DiscoversInitialMutationSet()
    {
        const string source = """
            class Sample
            {
                bool Check(int count, string name)
                {
                    string label = "x";
                    object value = label;
                    return count == 0 && !string.IsNullOrEmpty(name);
                }

                int Math(int value)
                {
                    return -value + 1;
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site => site.Description == "replace == with !=");
        Assert.Contains(analysis.Sites, site => site.Description == "replace 0 with 1");
        Assert.Contains(analysis.Sites, site => site.Description == "replace && with ||");
        Assert.Contains(analysis.Sites, site => site.Description == "replace ! with removed !");
        Assert.Contains(analysis.Sites, site => site.Description == "replace - with removed -");
        Assert.Contains(analysis.Sites, site => site.Description == "replace + with -");
        Assert.Contains(analysis.Sites, site => site.Description == "replace 1 with 0");
        Assert.Contains(analysis.Sites, site => site is { Category: "string", MutatorId: "string-literal" });
        Assert.Contains(analysis.Sites, site => site.Replacement == "null");
        Assert.Contains(analysis.Sites, site => site is { Category: "boolean", MutatorId: "boolean-negation" });
        Assert.Contains(analysis.Sites, site => site is { Category: "arithmetic", MutatorId: "arithmetic-operator" });
        Assert.Contains(analysis.Sites, site => site is { Category: "null", MutatorId: "null-replacement" });
        Assert.NotEmpty(analysis.Scopes);
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotTreatStringConcatenationAsNumericAddition()
    {
        const string source = """
            class Sample
            {
                string Join(string left, string right)
                {
                    return left + right;
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.DoesNotContain(analysis.Sites, site => site.Original == "+" && site.Replacement == "-");
    }

    [Fact]
    public async Task AnalyzeAsync_DiscoversRicherExpressionMutators()
    {
        const string source = """
            class Sample
            {
                int Change(int count)
                {
                    count += 2;
                    count++;
                    --count;
                    return count * 42;
                }

                string Label()
                {
                    string value = "";
                    return "ready";
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "literal",
            MutatorId: "numeric-literal",
            Description: "replace 2 with 0"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "literal",
            MutatorId: "numeric-literal",
            Description: "replace 42 with 0"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "assignment",
            MutatorId: "assignment-operator",
            Description: "replace += with -="
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "update",
            MutatorId: "update-operator",
            Description: "replace ++ with --"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "update",
            MutatorId: "update-operator",
            Description: "replace -- with ++"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "string",
            MutatorId: "string-literal",
            Description: "replace empty string with \"mutate4net\""
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "string",
            MutatorId: "string-literal",
            Description: "replace \"ready\" with empty string"
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotTreatStringCompoundAssignmentAsNumericAssignment()
    {
        const string source = """
            class Sample
            {
                void Append(string suffix)
                {
                    string value = "start";
                    value += suffix;
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.DoesNotContain(analysis.Sites, site => site is
        {
            Category: "assignment",
            MutatorId: "assignment-operator"
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DiscoversRicherArithmeticAndBitwiseMutators()
    {
        const string source = """
            class Sample
            {
                int Ops(int left, int right, int shift)
                {
                    int value = left % right;
                    value = value << shift;
                    value = value >> shift;
                    value = value >>> shift;
                    value &= right;
                    value |= right;
                    value ^= right;
                    value <<= shift;
                    value >>= shift;
                    value >>>= shift;
                    return (left & right) | (left ^ right);
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "arithmetic",
            MutatorId: "arithmetic-operator",
            Description: "replace % with *"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "arithmetic",
            MutatorId: "arithmetic-operator",
            Description: "replace << with >>"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "arithmetic",
            MutatorId: "arithmetic-operator",
            Description: "replace >>> with <<"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "arithmetic",
            MutatorId: "bitwise-operator",
            Description: "replace & with |"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "arithmetic",
            MutatorId: "bitwise-operator",
            Description: "replace | with &"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "arithmetic",
            MutatorId: "bitwise-operator",
            Description: "replace ^ with &"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "assignment",
            MutatorId: "assignment-operator",
            Description: "replace &= with |="
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "assignment",
            MutatorId: "assignment-operator",
            Description: "replace >>>= with <<="
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DiscoversObjectInitializerMemberRemoval()
    {
        const string source = """
            class Sample
            {
                Holder Create()
                {
                    return new Holder
                    {
                        First = 1,
                        Second = "two"
                    };
                }

                Holder CreateSingle()
                {
                    return new Holder { First = 1 };
                }

                sealed class Holder
                {
                    public int First { get; set; }
                    public string Second { get; set; } = "";
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site =>
            site is
            {
                Category: "object",
                MutatorId: "object-initializer-member",
                Description: "remove object initializer member",
                Replacement: ""
            } &&
            site.Original.Contains("First = 1", StringComparison.Ordinal));
        Assert.Contains(analysis.Sites, site =>
            site is
            {
                Category: "object",
                MutatorId: "object-initializer-member",
                Description: "remove object initializer member",
                Replacement: ""
            } &&
            site.Original.Contains("Second = \"two\"", StringComparison.Ordinal));
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "object",
            MutatorId: "object-initializer-member",
            Original: "First = 1",
            Replacement: ""
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DiscoversSupportedStringMethodMutators()
    {
        const string source = """
            using System.Globalization;
            using static System.String;

            class Sample
            {
                bool Blank(string value)
                {
                    return string.IsNullOrEmpty(value) || IsNullOrWhiteSpace(value);
                }

                string Normalize(string value, CultureInfo culture)
                {
                    return value.ToLower().ToUpperInvariant() + value.ToUpper(culture);
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "string",
            MutatorId: "string-method",
            Original: "IsNullOrEmpty",
            Replacement: "IsNullOrWhiteSpace"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "string",
            MutatorId: "string-method",
            Original: "IsNullOrWhiteSpace",
            Replacement: "IsNullOrEmpty"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "string",
            MutatorId: "string-method",
            Original: "ToLower",
            Replacement: "ToUpper"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "string",
            MutatorId: "string-method",
            Original: "ToUpperInvariant",
            Replacement: "ToLowerInvariant"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "string",
            MutatorId: "string-method",
            Original: "ToUpper",
            Replacement: "ToLower"
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotTreatCustomMethodsAsStringMutators()
    {
        const string source = """
            class Sample
            {
                bool Check(string value)
                {
                    return IsNullOrEmpty(value);
                }

                bool IsNullOrEmpty(string value)
                {
                    return value.Length == 0;
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.DoesNotContain(analysis.Sites, site => site is
        {
            Category: "string",
            MutatorId: "string-method"
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DiscoversConditionalExpressionBranches()
    {
        const string source = """
            class Sample
            {
                int Choose(bool flag, int left, int right)
                {
                    return flag ? left + 1 : right - 1;
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "conditional",
            MutatorId: "conditional-expression",
            Description: "replace conditional expression with true branch",
            Replacement: "left + 1"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "conditional",
            MutatorId: "conditional-expression",
            Description: "replace conditional expression with false branch",
            Replacement: "right - 1"
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DiscoversCoalescingAndCheckedMutators()
    {
        const string source = """
            class Sample
            {
                string Choose(string? value, string fallback)
                {
                    string result = value ?? fallback;
                    result ??= "default";
                    return result;
                }

                int Add(int left, int right)
                {
                    int total = checked(left + right);
                    unchecked
                    {
                        total += 1;
                    }

                    return total;
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "coalescing",
            MutatorId: "coalescing-expression",
            Description: "replace coalescing expression with left side",
            Original: "value ?? fallback",
            Replacement: "value"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "coalescing",
            MutatorId: "coalescing-expression",
            Description: "replace coalescing expression with right side",
            Original: "value ?? fallback",
            Replacement: "fallback"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "coalescing",
            MutatorId: "coalescing-assignment",
            Description: "replace ??= with =",
            Original: "??=",
            Replacement: "="
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "checked",
            MutatorId: "checked-context",
            Description: "replace checked with unchecked",
            Original: "checked",
            Replacement: "unchecked"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "checked",
            MutatorId: "checked-context",
            Description: "replace unchecked with checked",
            Original: "unchecked",
            Replacement: "checked"
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DiscoversPatternNegationMutators()
    {
        const string source = """
            class Sample
            {
                bool Matches(object? value, int[] numbers)
                {
                    return value is string
                        || value is not null
                        || numbers is [1, ..]
                        || value is var captured
                        || value is _;
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "pattern",
            MutatorId: "pattern-negation",
            Description: "replace is pattern with is not pattern",
            Original: "is",
            Replacement: "is not"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "pattern",
            MutatorId: "pattern-negation",
            Description: "replace is not pattern with is pattern",
            Original: "not",
            Replacement: ""
        });
        Assert.DoesNotContain(analysis.Sites, site =>
            site is
            {
                Category: "pattern",
                MutatorId: "pattern-negation",
                Replacement: "is not"
            } &&
            site.Line == 8);
        Assert.DoesNotContain(analysis.Sites, site =>
            site is
            {
                Category: "pattern",
                MutatorId: "pattern-negation",
                Replacement: "is not"
            } &&
            site.Line == 9);
    }

    [Fact]
    public async Task AnalyzeAsync_DiscoversSwitchExpressionArmMutators()
    {
        const string source = """
            class Sample
            {
                string Label(int value)
                {
                    return value switch
                    {
                        0 => "zero",
                        1 => "one",
                        _ => "many"
                    };
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "switch",
            MutatorId: "switch-expression",
            Description: "replace switch expression with arm expression",
            Replacement: "\"zero\""
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "switch",
            MutatorId: "switch-expression",
            Description: "replace switch expression with arm expression",
            Replacement: "\"one\""
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "switch",
            MutatorId: "switch-expression",
            Description: "replace switch expression with arm expression",
            Replacement: "\"many\""
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DiscoversCollectionEmptyMutators()
    {
        const string source = """
            using System.Collections.Generic;

            class Sample
            {
                int[] Array()
                {
                    return new[] { 1, 2 };
                }

                List<int> List()
                {
                    return new List<int> { 1, 2 };
                }

                int[] CollectionExpression()
                {
                    int[] values = [1, 2, 3];
                    return values;
                }

                Holder ObjectInitializer()
                {
                    return new Holder { Value = 1 };
                }

                sealed class Holder
                {
                    public int Value { get; set; }
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "collection",
            MutatorId: "collection-empty",
            Original: "{ 1, 2 }",
            Replacement: "{ }"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "collection",
            MutatorId: "collection-empty",
            Original: "[1, 2, 3]",
            Replacement: "[]"
        });
        Assert.DoesNotContain(analysis.Sites, site => site is
        {
            Category: "collection",
            MutatorId: "collection-empty",
            Original: "{ Value = 1 }"
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DiscoversBooleanConditionMutators()
    {
        const string source = """
            class Sample
            {
                int Count(int value)
                {
                    if (value > 0)
                    {
                        value++;
                    }

                    while (value < 10)
                    {
                        value++;
                    }

                    do
                    {
                        value++;
                    }
                    while (value != 12);

                    for (; value < 20; value++)
                    {
                        value++;
                    }

                    return value == 20 ? 1 : 0;
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "boolean",
            MutatorId: "boolean-condition",
            Original: "value > 0",
            Replacement: "true"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "boolean",
            MutatorId: "boolean-condition",
            Original: "value > 0",
            Replacement: "false"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "boolean",
            MutatorId: "boolean-condition",
            Original: "value < 20",
            Replacement: "true"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "boolean",
            MutatorId: "boolean-condition",
            Original: "value == 20",
            Replacement: "false"
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotAddBooleanConditionMutatorsForLiteralConditions()
    {
        const string source = """
            class Sample
            {
                void Count()
                {
                    if (true)
                    {
                    }

                    while (false)
                    {
                    }
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.DoesNotContain(analysis.Sites, site => site is
        {
            Category: "boolean",
            MutatorId: "boolean-condition"
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DiscoversReturnValueMutators()
    {
        const string source = """
            class Sample
            {
                bool IsPositive(int value)
                {
                    return value > 0;
                }

                int Double(int value)
                {
                    return value * 2;
                }

                string Normalize(string value)
                {
                    return value.Trim();
                }

                bool IsZero(int value) => value == 0;
                int Next(int value) => value + 1;
                string Lower(string value) => value.ToLowerInvariant();
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "return",
            MutatorId: "return-value",
            Original: "value > 0",
            Replacement: "true"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "return",
            MutatorId: "return-value",
            Original: "value > 0",
            Replacement: "false"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "return",
            MutatorId: "return-value",
            Original: "value * 2",
            Replacement: "0"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "return",
            MutatorId: "return-value",
            Original: "value.Trim()",
            Replacement: "\"\""
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "return",
            MutatorId: "return-value",
            Original: "value == 0",
            Replacement: "false"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "return",
            MutatorId: "return-value",
            Original: "value + 1",
            Replacement: "0"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "return",
            MutatorId: "return-value",
            Original: "value.ToLowerInvariant()",
            Replacement: "\"\""
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotAddReturnValueMutatorsForLiteralReturns()
    {
        const string source = """
            class Sample
            {
                bool Flag()
                {
                    return true;
                }

                int Count() => 0;
                string Name() => "ready";
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.DoesNotContain(analysis.Sites, site => site is
        {
            Category: "return",
            MutatorId: "return-value"
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DiscoversSupportedLinqMethodMutators()
    {
        const string source = """
            using System.Linq;

            class Sample
            {
                int FirstValue(int[] values)
                {
                    return values.First();
                }

                int LastPositive(int[] values)
                {
                    return values.LastOrDefault(value => value > 0);
                }

                int ByIndex(int[] values, int index)
                {
                    return values.ElementAt(index);
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "linq",
            MutatorId: "linq-method",
            Original: "First",
            Replacement: "FirstOrDefault"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "linq",
            MutatorId: "linq-method",
            Original: "LastOrDefault",
            Replacement: "Last"
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "linq",
            MutatorId: "linq-method",
            Original: "ElementAt",
            Replacement: "ElementAtOrDefault"
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotTreatCustomMethodsAsLinqMutators()
    {
        const string source = """
            class Sample
            {
                int Pick()
                {
                    return First();
                }

                int First()
                {
                    return 1;
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.DoesNotContain(analysis.Sites, site => site is
        {
            Category: "linq",
            MutatorId: "linq-method"
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DiscoversRemovableInvocationStatements()
    {
        const string source = """
            using System.Threading.Tasks;

            class Sample
            {
                async Task SaveAsync(Notifier notifier)
                {
                    int count = 0;
                    count = 1;
                    count += 2;
                    count++;
                    --count;
                    notifier.Notify();
                    await notifier.FlushAsync();
                }
            }

            class Notifier
            {
                public void Notify()
                {
                }

                public Task FlushAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "statement",
            MutatorId: "statement-removal",
            Description: "remove assignment statement",
            Original: "count = 1;",
            Replacement: ""
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "statement",
            MutatorId: "statement-removal",
            Description: "remove assignment statement",
            Original: "count += 2;",
            Replacement: ""
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "statement",
            MutatorId: "statement-removal",
            Description: "remove update statement",
            Original: "count++;",
            Replacement: ""
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "statement",
            MutatorId: "statement-removal",
            Description: "remove update statement",
            Original: "--count;",
            Replacement: ""
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "statement",
            MutatorId: "statement-removal",
            Description: "remove invocation statement",
            Original: "notifier.Notify();",
            Replacement: ""
        });
        Assert.Contains(analysis.Sites, site => site is
        {
            Category: "statement",
            MutatorId: "statement-removal",
            Description: "remove invocation statement",
            Original: "await notifier.FlushAsync();",
            Replacement: ""
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotRemoveEmbeddedExpressionStatements()
    {
        const string source = """
            class Sample
            {
                void Save(bool shouldSave, Notifier notifier)
                {
                    int count = 0;

                    if (shouldSave)
                        notifier.Notify();

                    if (shouldSave)
                        count = 1;

                    if (shouldSave)
                        count++;
                }
            }

            class Notifier
            {
                public void Notify()
                {
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.DoesNotContain(analysis.Sites, site => site is
        {
            Category: "statement",
            MutatorId: "statement-removal"
        });
    }

    [Fact]
    public async Task AnalyzeAsync_DiscoversPatternOperators()
    {
        const string source = """
            class Sample
            {
                bool IsPermanent(int statusCode)
                {
                    return statusCode is >= 500 and < 600;
                }
            }
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Contains(analysis.Sites, site => site.Description == "replace >= with >");
        Assert.Contains(analysis.Sites, site => site.Description == "replace and with or");
        Assert.Contains(analysis.Sites, site => site.Description == "replace < with <=");
    }

    [Fact]
    public async Task AnalyzeAsync_UsesProjectSourcesForSemanticTypes()
    {
        using var workspace = ProjectWorkspace.Create();
        string project = workspace.Write("App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        workspace.Write("Helper.cs", """
            public sealed class Helper
            {
                public static Helper Create() => new();
            }
            """);
        string target = workspace.Write("Sample.cs", """
            public sealed class Sample
            {
                public Helper Make()
                {
                    return Helper.Create();
                }
            }
            """);

        var analysis = await new MutationCatalog().AnalyzeAsync(target, project);

        Assert.Contains(analysis.Sites, site => site.Original == "Helper.Create()" && site.Replacement == "null");
    }

    [Fact]
    public async Task AnalyzeAsync_StripsEmbeddedManifestBeforeScanning()
    {
        const string source = """
            class Sample
            {
                bool Flag() => true;
            }

            /* mutate4net-manifest
            version=1
            scope.0.semanticHash=false
            */
            """;
        using var sample = SampleFile.Create(source);

        var analysis = await new MutationCatalog().AnalyzeAsync(sample.Path);

        Assert.Single(analysis.Sites);
        Assert.Equal("replace true with false", analysis.Sites[0].Description);
    }

    private sealed class ProjectWorkspace : IDisposable
    {
        private readonly string _root;

        private ProjectWorkspace(string root)
        {
            _root = root;
        }

        public static ProjectWorkspace Create()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "mutate4net-analysis-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new ProjectWorkspace(root);
        }

        public string Write(string relativePath, string contents)
        {
            string path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
