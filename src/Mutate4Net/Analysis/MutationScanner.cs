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
            AddSite(node, node.Token.Span, "true", "false", "replace true with false");
        }
        else if (node.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            AddSite(node, node.Token.Span, "false", "true", "replace false with true");
        }
        else if (node.IsKind(SyntaxKind.NumericLiteralExpression) &&
                 (node.Token.Text == "0" || node.Token.Text == "1"))
        {
            string replacement = node.Token.Text == "0" ? "1" : "0";
            AddSite(node, node.Token.Span, node.Token.Text, replacement, $"replace {node.Token.Text} with {replacement}");
        }

        base.VisitLiteralExpression(node);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        (string Original, string Replacement, bool NumericOnly)? op = OperatorFor(node.Kind());
        if (op is not null && (!op.Value.NumericOnly || IsNumeric(node)))
        {
            AddSite(
                node,
                node.OperatorToken.Span,
                op.Value.Original,
                op.Value.Replacement,
                $"replace {op.Value.Original} with {op.Value.Replacement}");
        }

        base.VisitBinaryExpression(node);
    }

    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.LogicalNotExpression))
        {
            AddSite(node, node.OperatorToken.Span, "!", string.Empty, "replace ! with removed !");
        }
        else if (node.IsKind(SyntaxKind.UnaryMinusExpression) && IsNumeric(node.Operand))
        {
            AddSite(node, node.OperatorToken.Span, "-", string.Empty, "replace - with removed -");
        }

        base.VisitPrefixUnaryExpression(node);
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
        AddNullReplacement(node.Right);
        base.VisitAssignmentExpression(node);
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
        AddSite(expression, expression.Span, original, "null", "replace " + original + " with null");
    }

    private void AddSite(SyntaxNode node, TextSpan span, string original, string replacement, string description)
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

    private static (string Original, string Replacement, bool NumericOnly)? OperatorFor(SyntaxKind kind) => kind switch
    {
        SyntaxKind.AddExpression => ("+", "-", true),
        SyntaxKind.SubtractExpression => ("-", "+", false),
        SyntaxKind.MultiplyExpression => ("*", "/", false),
        SyntaxKind.DivideExpression => ("/", "*", false),
        SyntaxKind.LogicalAndExpression => ("&&", "||", false),
        SyntaxKind.LogicalOrExpression => ("||", "&&", false),
        SyntaxKind.EqualsExpression => ("==", "!=", false),
        SyntaxKind.NotEqualsExpression => ("!=", "==", false),
        SyntaxKind.GreaterThanExpression => (">", ">=", false),
        SyntaxKind.GreaterThanOrEqualExpression => (">=", ">", false),
        SyntaxKind.LessThanExpression => ("<", "<=", false),
        SyntaxKind.LessThanOrEqualExpression => ("<=", "<", false),
        _ => null
    };

    private static string Hash(string value)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
