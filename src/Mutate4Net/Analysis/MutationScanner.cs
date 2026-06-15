using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Mutate4Net.Model;

namespace Mutate4Net.Analysis;

internal sealed class MutationScanner : CSharpSyntaxWalker
{
    private readonly string _file;
    private readonly string _source;
    private readonly SyntaxTree _tree;
    private readonly SemanticModel _semanticModel;
    private readonly LineNumberTable _lineNumbers;
    private readonly List<MutationSite> _sites = [];
    private readonly Dictionary<string, MutationScope> _scopes = new(StringComparer.Ordinal);

    public MutationScanner(string file, string source, SyntaxTree tree, SemanticModel semanticModel)
        : base(SyntaxWalkerDepth.Node)
    {
        _file = file;
        _source = source;
        _tree = tree;
        _semanticModel = semanticModel;
        _lineNumbers = new LineNumberTable(source);
    }

    public IReadOnlyList<MutationSite> Sites => _sites;

    public IReadOnlyCollection<MutationScope> Scopes => _scopes.Values;

    public override void VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.TrueLiteralExpression))
        {
            AddSite(node, node.Token.Span, "true", "false", "replace true with false", "boolean-literal", "boolean");
        }
        else if (node.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            AddSite(node, node.Token.Span, "false", "true", "replace false with true", "boolean-literal", "boolean");
        }
        else if (node.IsKind(SyntaxKind.NumericLiteralExpression) &&
                 IsNumeric(node))
        {
            string replacement = IsNumericValue(node.Token.Value, 0) ? "1" : "0";
            AddSite(node, node.Token.Span, node.Token.Text, replacement, $"replace {node.Token.Text} with {replacement}", "numeric-literal", "literal");
        }
        else if (node.IsKind(SyntaxKind.StringLiteralExpression) &&
                 IsString(node))
        {
            string replacement = node.Token.ValueText.Length == 0 ? "\"mutate4net\"" : "\"\"";
            string description = node.Token.ValueText.Length == 0
                ? "replace empty string with \"mutate4net\""
                : $"replace {node.Token.Text} with empty string";
            AddSite(node, node.Token.Span, node.Token.Text, replacement, description, "string-literal", "string");
        }

        base.VisitLiteralExpression(node);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        (string Original, string Replacement, bool NumericOnly, string MutatorId, string Category)? op = OperatorFor(node.Kind());
        if (op is not null && (!op.Value.NumericOnly || IsNumeric(node)))
        {
            AddSite(
                node,
                node.OperatorToken.Span,
                op.Value.Original,
                op.Value.Replacement,
                $"replace {op.Value.Original} with {op.Value.Replacement}",
                op.Value.MutatorId,
                op.Value.Category);
        }

        base.VisitBinaryExpression(node);
    }

    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.LogicalNotExpression))
        {
            AddSite(node, node.OperatorToken.Span, "!", string.Empty, "replace ! with removed !", "boolean-negation", "boolean");
        }
        else if (UpdateOperatorFor(node.Kind()) is { } updateOp &&
                 IsNumeric(node.Operand))
        {
            AddSite(
                node,
                node.OperatorToken.Span,
                updateOp.Original,
                updateOp.Replacement,
                $"replace {updateOp.Original} with {updateOp.Replacement}",
                "update-operator",
                "update");
        }
        else if (node.IsKind(SyntaxKind.UnaryMinusExpression) && IsNumeric(node.Operand))
        {
            AddSite(node, node.OperatorToken.Span, "-", string.Empty, "replace - with removed -", "unary-operator", "unary");
        }

        base.VisitPrefixUnaryExpression(node);
    }

    public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
    {
        if (UpdateOperatorFor(node.Kind()) is { } updateOp &&
            IsNumeric(node.Operand))
        {
            AddSite(
                node,
                node.OperatorToken.Span,
                updateOp.Original,
                updateOp.Replacement,
                $"replace {updateOp.Original} with {updateOp.Replacement}",
                "update-operator",
                "update");
        }

        base.VisitPostfixUnaryExpression(node);
    }

    public override void VisitRelationalPattern(RelationalPatternSyntax node)
    {
        (string Original, string Replacement)? op = RelationalPatternOperatorFor(node.OperatorToken.Kind());
        if (op is not null)
        {
            AddSite(
                node,
                node.OperatorToken.Span,
                op.Value.Original,
                op.Value.Replacement,
                $"replace {op.Value.Original} with {op.Value.Replacement}",
                "equality-operator",
                "equality");
        }

        base.VisitRelationalPattern(node);
    }

    public override void VisitBinaryPattern(BinaryPatternSyntax node)
    {
        (string Original, string Replacement)? op = BinaryPatternOperatorFor(node.Kind());
        if (op is not null)
        {
            AddSite(
                node,
                node.OperatorToken.Span,
                op.Value.Original,
                op.Value.Replacement,
                $"replace {op.Value.Original} with {op.Value.Replacement}",
                "logical-operator",
                "logical");
        }

        base.VisitBinaryPattern(node);
    }

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        string original = _source[node.Span.Start..node.Span.End];
        AddSite(
            node,
            node.Span,
            original,
            _source[node.WhenTrue.Span.Start..node.WhenTrue.Span.End],
            "replace conditional expression with true branch",
            "conditional-expression",
            "conditional");
        AddSite(
            node,
            node.Span,
            original,
            _source[node.WhenFalse.Span.Start..node.WhenFalse.Span.End],
            "replace conditional expression with false branch",
            "conditional-expression",
            "conditional");

        base.VisitConditionalExpression(node);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        AddLinqMethodReplacement(node);
        base.VisitInvocationExpression(node);
    }

    public override void VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (IsRemovableInvocationStatement(node))
        {
            string original = _source[node.Span.Start..node.Span.End];
            AddSite(
                node,
                node.Span,
                original,
                string.Empty,
                "remove invocation statement",
                "statement-removal",
                "statement");
        }

        base.VisitExpressionStatement(node);
    }

    public override void VisitReturnStatement(ReturnStatementSyntax node)
    {
        AddNullReplacement(node.Expression);
        base.VisitReturnStatement(node);
    }

    public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        AddNullReplacement(node.Initializer?.Value);
        base.VisitVariableDeclarator(node);
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        if (AssignmentOperatorFor(node.Kind()) is { } assignmentOp &&
            IsNumeric(node.Left))
        {
            AddSite(
                node,
                node.OperatorToken.Span,
                assignmentOp.Original,
                assignmentOp.Replacement,
                $"replace {assignmentOp.Original} with {assignmentOp.Replacement}",
                "assignment-operator",
                "assignment");
        }

        AddNullReplacement(node.Right);
        base.VisitAssignmentExpression(node);
    }

    private void AddLinqMethodReplacement(InvocationExpressionSyntax node)
    {
        SimpleNameSyntax? name = node.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            _ => null
        };
        if (name is null)
        {
            return;
        }

        if (_semanticModel.GetSymbolInfo(node).Symbol is not IMethodSymbol method ||
            !IsSystemLinqMethod(method) ||
            LinqReplacementFor(method) is not { } replacement)
        {
            return;
        }

        AddSite(
            node,
            name.Identifier.Span,
            name.Identifier.ValueText,
            replacement,
            $"replace {name.Identifier.ValueText} with {replacement}",
            "linq-method",
            "linq");
    }

    private void AddNullReplacement(ExpressionSyntax? expression)
    {
        if (expression is null ||
            expression.IsKind(SyntaxKind.NullLiteralExpression) ||
            !IsReferenceOrNullable(expression))
        {
            return;
        }

        string original = _source[expression.Span.Start..expression.Span.End];
        AddSite(expression, expression.Span, original, "null", "replace " + original + " with null", "null-replacement", "null");
    }

    private void AddSite(
        SyntaxNode node,
        TextSpan span,
        string original,
        string replacement,
        string description,
        string mutatorId,
        string category)
    {
        ScopeRef scope = ResolveScope(node);
        _sites.Add(new MutationSite(
            _file,
            _lineNumbers.LineNumber(span.Start),
            span.Start,
            span.End,
            original,
            replacement,
            description,
            mutatorId,
            category,
            scope.Id,
            scope.Kind,
            scope.StartLine,
            scope.EndLine));
    }

    private ScopeRef ResolveScope(SyntaxNode node)
    {
        SyntaxNode? scopeNode = node.AncestorsAndSelf().FirstOrDefault(IsScopeNode);
        if (scopeNode is null)
        {
            return new ScopeRef("global", "global", 1, Math.Max(1, _lineNumbers.LineNumber(_source.Length)));
        }

        string kind = ScopeKind(scopeNode);
        string displayName = ScopeName(scopeNode);
        FileLinePositionSpan lineSpan = _tree.GetLineSpan(scopeNode.Span);
        int startLine = lineSpan.StartLinePosition.Line + 1;
        int endLine = lineSpan.EndLinePosition.Line + 1;
        string id = $"{kind}:{ContainingTypeName(scopeNode)}#{displayName}:{startLine}";

        if (!_scopes.ContainsKey(id))
        {
            string semanticHash = Hash(_source[scopeNode.Span.Start..scopeNode.Span.End]);
            _scopes[id] = new MutationScope(id, kind, startLine, endLine, semanticHash);
        }

        return new ScopeRef(id, kind, startLine, endLine);
    }

    private static bool IsScopeNode(SyntaxNode node) =>
        node is MethodDeclarationSyntax ||
        node is ConstructorDeclarationSyntax ||
        node is DestructorDeclarationSyntax ||
        node is OperatorDeclarationSyntax ||
        node is ConversionOperatorDeclarationSyntax ||
        node is LocalFunctionStatementSyntax ||
        node is PropertyDeclarationSyntax ||
        node is IndexerDeclarationSyntax ||
        node is AccessorDeclarationSyntax ||
        node is FieldDeclarationSyntax ||
        node is EventFieldDeclarationSyntax ||
        node is BaseTypeDeclarationSyntax;

    private static string ScopeKind(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax => "method",
        ConstructorDeclarationSyntax => "constructor",
        DestructorDeclarationSyntax => "destructor",
        OperatorDeclarationSyntax => "operator",
        ConversionOperatorDeclarationSyntax => "conversion",
        LocalFunctionStatementSyntax => "local-function",
        PropertyDeclarationSyntax => "property",
        IndexerDeclarationSyntax => "indexer",
        AccessorDeclarationSyntax => "accessor",
        FieldDeclarationSyntax => "field",
        EventFieldDeclarationSyntax => "event",
        ClassDeclarationSyntax => "class",
        StructDeclarationSyntax => "struct",
        InterfaceDeclarationSyntax => "interface",
        RecordDeclarationSyntax => "record",
        _ => "scope"
    };

    private static string ScopeName(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method => $"{method.Identifier.ValueText}({method.ParameterList.Parameters.Count})",
        ConstructorDeclarationSyntax constructor => $"ctor({constructor.ParameterList.Parameters.Count})",
        DestructorDeclarationSyntax => "dtor(0)",
        OperatorDeclarationSyntax op => $"operator {op.OperatorToken.ValueText}({op.ParameterList.Parameters.Count})",
        ConversionOperatorDeclarationSyntax conversion => $"conversion {conversion.Type}({conversion.ParameterList.Parameters.Count})",
        LocalFunctionStatementSyntax local => $"{local.Identifier.ValueText}({local.ParameterList.Parameters.Count})",
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        IndexerDeclarationSyntax => "this[]",
        AccessorDeclarationSyntax accessor => accessor.Keyword.ValueText,
        FieldDeclarationSyntax field => string.Join(",", field.Declaration.Variables.Select(v => v.Identifier.ValueText)),
        EventFieldDeclarationSyntax field => string.Join(",", field.Declaration.Variables.Select(v => v.Identifier.ValueText)),
        BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
        _ => "scope"
    };

    private static string ContainingTypeName(SyntaxNode node)
    {
        BaseTypeDeclarationSyntax? type = node.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
        return type?.Identifier.ValueText ?? "<global>";
    }

    private bool IsNumeric(ExpressionSyntax expression)
    {
        ITypeSymbol? type = _semanticModel.GetTypeInfo(expression).Type;
        return type is not null && IsNumericType(type);
    }

    private bool IsString(ExpressionSyntax expression)
    {
        ITypeSymbol? type = _semanticModel.GetTypeInfo(expression).Type;
        return type?.SpecialType == SpecialType.System_String;
    }

    private bool IsReferenceOrNullable(ExpressionSyntax expression)
    {
        TypeInfo typeInfo = _semanticModel.GetTypeInfo(expression);
        ITypeSymbol? type = typeInfo.ConvertedType ?? typeInfo.Type;
        if (type is null || type.TypeKind == TypeKind.Error || type.SpecialType == SpecialType.System_Void)
        {
            return false;
        }

        return type.IsReferenceType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    private static bool IsNumericType(ITypeSymbol type) => type.SpecialType is
        SpecialType.System_SByte or
        SpecialType.System_Byte or
        SpecialType.System_Int16 or
        SpecialType.System_UInt16 or
        SpecialType.System_Int32 or
        SpecialType.System_UInt32 or
        SpecialType.System_Int64 or
        SpecialType.System_UInt64 or
        SpecialType.System_Single or
        SpecialType.System_Double or
        SpecialType.System_Decimal;

    private static (string Original, string Replacement, bool NumericOnly, string MutatorId, string Category)? OperatorFor(SyntaxKind kind) => kind switch
    {
        SyntaxKind.AddExpression => ("+", "-", true, "arithmetic-operator", "arithmetic"),
        SyntaxKind.SubtractExpression => ("-", "+", false, "arithmetic-operator", "arithmetic"),
        SyntaxKind.MultiplyExpression => ("*", "/", false, "arithmetic-operator", "arithmetic"),
        SyntaxKind.DivideExpression => ("/", "*", false, "arithmetic-operator", "arithmetic"),
        SyntaxKind.LogicalAndExpression => ("&&", "||", false, "logical-operator", "logical"),
        SyntaxKind.LogicalOrExpression => ("||", "&&", false, "logical-operator", "logical"),
        SyntaxKind.EqualsExpression => ("==", "!=", false, "equality-operator", "equality"),
        SyntaxKind.NotEqualsExpression => ("!=", "==", false, "equality-operator", "equality"),
        SyntaxKind.GreaterThanExpression => (">", ">=", false, "equality-operator", "equality"),
        SyntaxKind.GreaterThanOrEqualExpression => (">=", ">", false, "equality-operator", "equality"),
        SyntaxKind.LessThanExpression => ("<", "<=", false, "equality-operator", "equality"),
        SyntaxKind.LessThanOrEqualExpression => ("<=", "<", false, "equality-operator", "equality"),
        _ => null
    };

    private static (string Original, string Replacement)? RelationalPatternOperatorFor(SyntaxKind kind) => kind switch
    {
        SyntaxKind.GreaterThanToken => (">", ">="),
        SyntaxKind.GreaterThanEqualsToken => (">=", ">"),
        SyntaxKind.LessThanToken => ("<", "<="),
        SyntaxKind.LessThanEqualsToken => ("<=", "<"),
        _ => null
    };

    private static (string Original, string Replacement)? BinaryPatternOperatorFor(SyntaxKind kind) => kind switch
    {
        SyntaxKind.AndPattern => ("and", "or"),
        SyntaxKind.OrPattern => ("or", "and"),
        _ => null
    };

    private static (string Original, string Replacement)? AssignmentOperatorFor(SyntaxKind kind) => kind switch
    {
        SyntaxKind.AddAssignmentExpression => ("+=", "-="),
        SyntaxKind.SubtractAssignmentExpression => ("-=", "+="),
        SyntaxKind.MultiplyAssignmentExpression => ("*=", "/="),
        SyntaxKind.DivideAssignmentExpression => ("/=", "*="),
        SyntaxKind.ModuloAssignmentExpression => ("%=", "*="),
        _ => null
    };

    private static (string Original, string Replacement)? UpdateOperatorFor(SyntaxKind kind) => kind switch
    {
        SyntaxKind.PreIncrementExpression => ("++", "--"),
        SyntaxKind.PostIncrementExpression => ("++", "--"),
        SyntaxKind.PreDecrementExpression => ("--", "++"),
        SyntaxKind.PostDecrementExpression => ("--", "++"),
        _ => null
    };

    private static string? LinqReplacementFor(IMethodSymbol method)
    {
        IParameterSymbol[] callParameters = LinqCallParameters(method).ToArray();
        return method.Name switch
        {
            "First" when HasNoArgumentsOrPredicate(callParameters) => "FirstOrDefault",
            "FirstOrDefault" when HasNoArgumentsOrPredicate(callParameters) => "First",
            "Last" when HasNoArgumentsOrPredicate(callParameters) => "LastOrDefault",
            "LastOrDefault" when HasNoArgumentsOrPredicate(callParameters) => "Last",
            "Single" when HasNoArgumentsOrPredicate(callParameters) => "SingleOrDefault",
            "SingleOrDefault" when HasNoArgumentsOrPredicate(callParameters) => "Single",
            "ElementAt" when callParameters.Length == 1 => "ElementAtOrDefault",
            "ElementAtOrDefault" when callParameters.Length == 1 => "ElementAt",
            _ => null
        };
    }

    private static IEnumerable<IParameterSymbol> LinqCallParameters(IMethodSymbol method)
    {
        if (method.ReducedFrom is null &&
            method.IsExtensionMethod &&
            method.Parameters.Length > 0)
        {
            return method.Parameters.Skip(1);
        }

        return method.Parameters;
    }

    private static bool HasNoArgumentsOrPredicate(IReadOnlyList<IParameterSymbol> parameters) =>
        parameters.Count == 0 ||
        parameters.Count == 1 && IsPredicateLike(parameters[0].Type);

    private static bool IsPredicateLike(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Delegate)
        {
            return true;
        }

        return type is INamedTypeSymbol
        {
            Name: "Expression",
            ContainingNamespace: { } ns,
            TypeArguments.Length: 1
        } expressionType &&
        ns.ToDisplayString() == "System.Linq.Expressions" &&
        expressionType.TypeArguments[0].TypeKind == TypeKind.Delegate;
    }

    private static bool IsRemovableInvocationStatement(ExpressionStatementSyntax node) =>
        node.Parent is BlockSyntax &&
        (node.Expression is InvocationExpressionSyntax ||
         node.Expression is AwaitExpressionSyntax { Expression: InvocationExpressionSyntax });

    private static bool IsSystemLinqMethod(IMethodSymbol method)
    {
        IMethodSymbol originalMethod = method.ReducedFrom ?? method;
        INamedTypeSymbol? containingType = originalMethod.ContainingType;
        return containingType is not null &&
               containingType.ContainingNamespace.ToDisplayString() == "System.Linq" &&
               containingType.Name is "Enumerable" or "Queryable";
    }

    private static bool IsNumericValue(object? value, decimal expected)
    {
        try
        {
            return value is not null && Convert.ToDecimal(value) == expected;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static string Hash(string value)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
