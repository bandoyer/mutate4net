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
    private enum OperatorRequirement
    {
        Any,
        Numeric,
        Bitwise
    }

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
        AddCoalescingExpressionReplacement(node);

        (string Original, string Replacement, OperatorRequirement Requirement, string MutatorId, string Category)? op = OperatorFor(node.Kind());
        if (op is not null && MeetsRequirement(node, op.Value.Requirement))
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

    public override void VisitIsPatternExpression(IsPatternExpressionSyntax node)
    {
        AddPatternNegationReplacement(node);
        base.VisitIsPatternExpression(node);
    }

    public override void VisitSwitchExpression(SwitchExpressionSyntax node)
    {
        AddSwitchExpressionReplacement(node);
        base.VisitSwitchExpression(node);
    }

    public override void VisitCheckedExpression(CheckedExpressionSyntax node)
    {
        AddCheckedReplacement(node, node.Keyword);
        base.VisitCheckedExpression(node);
    }

    public override void VisitCheckedStatement(CheckedStatementSyntax node)
    {
        AddCheckedReplacement(node, node.Keyword);
        base.VisitCheckedStatement(node);
    }

    public override void VisitIfStatement(IfStatementSyntax node)
    {
        AddBooleanConditionReplacement(node.Condition);
        base.VisitIfStatement(node);
    }

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        AddBooleanConditionReplacement(node.Condition);
        base.VisitWhileStatement(node);
    }

    public override void VisitDoStatement(DoStatementSyntax node)
    {
        AddBooleanConditionReplacement(node.Condition);
        base.VisitDoStatement(node);
    }

    public override void VisitForStatement(ForStatementSyntax node)
    {
        AddBooleanConditionReplacement(node.Condition);
        base.VisitForStatement(node);
    }

    public override void VisitWhenClause(WhenClauseSyntax node)
    {
        AddBooleanConditionReplacement(node.Condition);
        base.VisitWhenClause(node);
    }

    public override void VisitCatchFilterClause(CatchFilterClauseSyntax node)
    {
        AddBooleanConditionReplacement(node.FilterExpression);
        base.VisitCatchFilterClause(node);
    }

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        AddBooleanConditionReplacement(node.Condition);

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
        AddStringMethodReplacement(node);
        AddAsyncInvocationReplacement(node);
        AddArgumentValueReplacements(node.ArgumentList.Arguments);
        base.VisitInvocationExpression(node);
    }

    public override void VisitAwaitExpression(AwaitExpressionSyntax node)
    {
        AddAwaitRemoval(node);
        base.VisitAwaitExpression(node);
    }

    public override void VisitBlock(BlockSyntax node)
    {
        AddControlFlowBlockReplacement(node);
        base.VisitBlock(node);
    }

    public override void VisitElseClause(ElseClauseSyntax node)
    {
        AddElseClauseRemoval(node);
        base.VisitElseClause(node);
    }

    public override void VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (RemovableStatementDescription(node) is { } description)
        {
            string original = _source[node.Span.Start..node.Span.End];
            AddSite(
                node,
                node.Span,
                original,
                string.Empty,
                description,
                "statement-removal",
                "statement");
        }

        base.VisitExpressionStatement(node);
    }

    public override void VisitThrowStatement(ThrowStatementSyntax node)
    {
        AddThrowStatementRemoval(node);
        AddThrownExceptionReplacement(node.Expression);
        base.VisitThrowStatement(node);
    }

    public override void VisitThrowExpression(ThrowExpressionSyntax node)
    {
        AddThrowExpressionDefaultReplacement(node);
        AddThrownExceptionReplacement(node.Expression);
        base.VisitThrowExpression(node);
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        AddArgumentValueReplacements(node.ArgumentList?.Arguments);
        base.VisitObjectCreationExpression(node);
    }

    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        AddArgumentValueReplacements(node.ArgumentList?.Arguments);
        base.VisitImplicitObjectCreationExpression(node);
    }

    public override void VisitInitializerExpression(InitializerExpressionSyntax node)
    {
        AddObjectInitializerMemberRemoval(node);
        AddCollectionInitializerReplacement(node);
        base.VisitInitializerExpression(node);
    }

    public override void VisitCollectionExpression(CollectionExpressionSyntax node)
    {
        if (node.Elements.Count > 0)
        {
            string original = _source[node.Span.Start..node.Span.End];
            AddSite(
                node,
                node.Span,
                original,
                "[]",
                "replace collection expression with empty collection",
                "collection-empty",
                "collection");
        }

        base.VisitCollectionExpression(node);
    }

    public override void VisitReturnStatement(ReturnStatementSyntax node)
    {
        AddReturnValueReplacement(node.Expression);
        AddNullReplacement(node.Expression);
        base.VisitReturnStatement(node);
    }

    public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
    {
        AddReturnValueReplacement(node.Expression);
        AddNullReplacement(node.Expression);
        base.VisitArrowExpressionClause(node);
    }

    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        AddReturnValueReplacement(node.ExpressionBody);
        AddNullReplacement(node.ExpressionBody);
        base.VisitSimpleLambdaExpression(node);
    }

    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        AddReturnValueReplacement(node.ExpressionBody);
        AddNullReplacement(node.ExpressionBody);
        base.VisitParenthesizedLambdaExpression(node);
    }

    public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        AddNullReplacement(node.Initializer?.Value);
        base.VisitVariableDeclarator(node);
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.CoalesceAssignmentExpression))
        {
            AddSite(
                node,
                node.OperatorToken.Span,
                "??=",
                "=",
                "replace ??= with =",
                "coalescing-assignment",
                "coalescing");
        }
        else if (AssignmentOperatorFor(node.Kind()) is { } assignmentOp &&
            MeetsRequirement(node.Left, assignmentOp.Requirement))
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
        SimpleNameSyntax? name = InvocationName(node);
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

    private void AddAsyncInvocationReplacement(InvocationExpressionSyntax node)
    {
        SimpleNameSyntax? name = InvocationName(node);
        if (name is null ||
            _semanticModel.GetSymbolInfo(node).Symbol is not IMethodSymbol method)
        {
            return;
        }

        AddTaskDelayReplacement(node, method);
        AddTaskYieldReplacement(node, method);
        AddConfigureAwaitReplacement(node, method);
        AddCancellationTokenReplacements(node);
    }

    private void AddArgumentValueReplacements(SeparatedSyntaxList<ArgumentSyntax>? arguments)
    {
        if (arguments is null)
        {
            return;
        }

        foreach (ArgumentSyntax argument in arguments.Value)
        {
            AddArgumentValueReplacement(argument);
        }
    }

    private void AddArgumentValueReplacement(ArgumentSyntax argument)
    {
        if (!argument.RefKindKeyword.IsKind(SyntaxKind.None))
        {
            return;
        }

        ExpressionSyntax expression = argument.Expression;
        if (expression.IsKind(SyntaxKind.NullLiteralExpression) ||
            expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
            expression is LiteralExpressionSyntax ||
            expression is DefaultExpressionSyntax ||
            expression is ThrowExpressionSyntax ||
            IsCancellationToken(expression))
        {
            return;
        }

        string original = _source[expression.Span.Start..expression.Span.End];
        foreach (string replacement in ArgumentValueReplacements(expression))
        {
            AddSite(
                expression,
                expression.Span,
                original,
                replacement,
                $"replace argument with {replacement}",
                "argument-value",
                "argument");
        }
    }

    private IEnumerable<string> ArgumentValueReplacements(ExpressionSyntax expression)
    {
        if (IsBool(expression))
        {
            yield return "true";
            yield return "false";
        }
        else if (IsNumeric(expression))
        {
            yield return "0";
        }
        else if (IsString(expression))
        {
            yield return "\"\"";
        }
        else if (IsReferenceOrNullable(expression))
        {
            yield return "null";
        }
        else if (IsEnumOrStruct(expression))
        {
            yield return "default";
        }
    }

    private void AddStringMethodReplacement(InvocationExpressionSyntax node)
    {
        SimpleNameSyntax? name = InvocationName(node);
        if (name is null)
        {
            return;
        }

        if (_semanticModel.GetSymbolInfo(node).Symbol is not IMethodSymbol method ||
            !IsSystemStringMethod(method) ||
            StringMethodReplacementFor(method) is not { } replacement)
        {
            return;
        }

        AddSite(
            node,
            name.Identifier.Span,
            name.Identifier.ValueText,
            replacement,
            $"replace {name.Identifier.ValueText} with {replacement}",
            "string-method",
            "string");
    }

    private void AddTaskDelayReplacement(InvocationExpressionSyntax node, IMethodSymbol method)
    {
        if (!IsSystemTaskMethod(method, "Delay"))
        {
            return;
        }

        string original = _source[node.Span.Start..node.Span.End];
        AddSite(
            node,
            node.Span,
            original,
            "global::System.Threading.Tasks.Task.CompletedTask",
            "replace Task.Delay with completed task",
            "task-delay",
            "async");
    }

    private void AddTaskYieldReplacement(InvocationExpressionSyntax node, IMethodSymbol method)
    {
        if (!IsSystemTaskMethod(method, "Yield") ||
            node.Parent is not AwaitExpressionSyntax)
        {
            return;
        }

        string original = _source[node.Span.Start..node.Span.End];
        AddSite(
            node,
            node.Span,
            original,
            "global::System.Threading.Tasks.Task.CompletedTask",
            "replace Task.Yield with completed task",
            "task-yield",
            "async");
    }

    private void AddConfigureAwaitReplacement(InvocationExpressionSyntax node, IMethodSymbol method)
    {
        if (method.Name != "ConfigureAwait" ||
            node.Parent is not AwaitExpressionSyntax ||
            node.Expression is not MemberAccessExpressionSyntax memberAccess ||
            method.Parameters.Length != 1 ||
            method.Parameters[0].Type.SpecialType != SpecialType.System_Boolean)
        {
            return;
        }

        string original = _source[node.Span.Start..node.Span.End];
        string replacement = _source[memberAccess.Expression.Span.Start..memberAccess.Expression.Span.End];
        AddSite(
            node,
            node.Span,
            original,
            replacement,
            "remove ConfigureAwait call",
            "configure-await",
            "async");
    }

    private void AddCancellationTokenReplacements(InvocationExpressionSyntax node)
    {
        foreach (ArgumentSyntax argument in node.ArgumentList.Arguments)
        {
            if (!IsCancellationToken(argument.Expression) ||
                IsDefaultCancellationToken(argument.Expression))
            {
                continue;
            }

            string original = _source[argument.Expression.Span.Start..argument.Expression.Span.End];
            AddSite(
                argument.Expression,
                argument.Expression.Span,
                original,
                "global::System.Threading.CancellationToken.None",
                "replace cancellation token with CancellationToken.None",
                "cancellation-token",
                "async");
        }
    }

    private void AddCoalescingExpressionReplacement(BinaryExpressionSyntax node)
    {
        if (!node.IsKind(SyntaxKind.CoalesceExpression))
        {
            return;
        }

        string original = _source[node.Span.Start..node.Span.End];
        AddSite(
            node,
            node.Span,
            original,
            _source[node.Left.Span.Start..node.Left.Span.End],
            "replace coalescing expression with left side",
            "coalescing-expression",
            "coalescing");
        AddSite(
            node,
            node.Span,
            original,
            _source[node.Right.Span.Start..node.Right.Span.End],
            "replace coalescing expression with right side",
            "coalescing-expression",
            "coalescing");
    }

    private void AddCheckedReplacement(SyntaxNode node, SyntaxToken keyword)
    {
        string original = keyword.ValueText;
        string replacement = original == "checked" ? "unchecked" : "checked";
        AddSite(
            node,
            keyword.Span,
            original,
            replacement,
            $"replace {original} with {replacement}",
            "checked-context",
            "checked");
    }

    private void AddCollectionInitializerReplacement(InitializerExpressionSyntax node)
    {
        if (!node.IsKind(SyntaxKind.ArrayInitializerExpression) &&
            !node.IsKind(SyntaxKind.CollectionInitializerExpression))
        {
            return;
        }

        if (node.Expressions.Count == 0)
        {
            return;
        }

        string original = _source[node.Span.Start..node.Span.End];
        AddSite(
            node,
            node.Span,
            original,
            "{ }",
            "replace collection initializer with empty initializer",
            "collection-empty",
            "collection");
    }

    private void AddPatternNegationReplacement(IsPatternExpressionSyntax node)
    {
        if (node.Pattern is UnaryPatternSyntax notPattern &&
            notPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword))
        {
            AddSite(
                node,
                notPattern.OperatorToken.Span,
                "not",
                string.Empty,
                "replace is not pattern with is pattern",
                "pattern-negation",
                "pattern");
            return;
        }

        if (!CanNegatePattern(node.Pattern))
        {
            return;
        }

        AddSite(
            node,
            node.IsKeyword.Span,
            "is",
            "is not",
            "replace is pattern with is not pattern",
            "pattern-negation",
            "pattern");
    }

    private void AddSwitchExpressionReplacement(SwitchExpressionSyntax node)
    {
        if (node.Arms.Count == 0)
        {
            return;
        }

        string original = _source[node.Span.Start..node.Span.End];
        foreach (SwitchExpressionArmSyntax arm in node.Arms)
        {
            string replacement = _source[arm.Expression.Span.Start..arm.Expression.Span.End];
            AddSite(
                arm.Expression,
                node.Span,
                original,
                replacement,
                "replace switch expression with arm expression",
                "switch-expression",
                "switch");
        }
    }

    private void AddBooleanConditionReplacement(ExpressionSyntax? condition)
    {
        if (condition is null ||
            condition.IsKind(SyntaxKind.TrueLiteralExpression) ||
            condition.IsKind(SyntaxKind.FalseLiteralExpression) ||
            !IsBool(condition))
        {
            return;
        }

        string original = _source[condition.Span.Start..condition.Span.End];
        AddSite(condition, condition.Span, original, "true", "replace condition with true", "boolean-condition", "boolean");
        AddSite(condition, condition.Span, original, "false", "replace condition with false", "boolean-condition", "boolean");
    }

    private void AddReturnValueReplacement(ExpressionSyntax? expression)
    {
        if (expression is null ||
            expression is LiteralExpressionSyntax)
        {
            return;
        }

        string original = _source[expression.Span.Start..expression.Span.End];
        if (IsBool(expression))
        {
            AddSite(expression, expression.Span, original, "true", "replace return value with true", "return-value", "return");
            AddSite(expression, expression.Span, original, "false", "replace return value with false", "return-value", "return");
        }
        else if (IsNumeric(expression))
        {
            AddSite(expression, expression.Span, original, "0", "replace return value with 0", "return-value", "return");
        }
        else if (IsString(expression))
        {
            AddSite(expression, expression.Span, original, "\"\"", "replace return value with empty string", "return-value", "return");
        }
    }

    private void AddObjectInitializerMemberRemoval(InitializerExpressionSyntax node)
    {
        if (!node.IsKind(SyntaxKind.ObjectInitializerExpression) ||
            node.Expressions.Count == 0)
        {
            return;
        }

        for (int i = 0; i < node.Expressions.Count; i++)
        {
            ExpressionSyntax expression = node.Expressions[i];
            TextSpan span = ObjectInitializerMemberRemovalSpan(node.Expressions, i);
            string original = _source[span.Start..span.End];
            AddSite(
                expression,
                span,
                original,
                string.Empty,
                "remove object initializer member",
                "object-initializer-member",
                "object");
        }
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

    private void AddControlFlowBlockReplacement(BlockSyntax node)
    {
        if (node.Statements.Count == 0 ||
            !IsControlFlowBodyBlock(node))
        {
            return;
        }

        string original = _source[node.Span.Start..node.Span.End];
        AddSite(
            node,
            node.Span,
            original,
            "{ }",
            "replace block body with empty block",
            "block-removal",
            "statement");
    }

    private void AddElseClauseRemoval(ElseClauseSyntax node)
    {
        string original = _source[node.Span.Start..node.Span.End];
        AddSite(
            node,
            node.Span,
            original,
            string.Empty,
            "remove else clause",
            "else-clause-removal",
            "statement");
    }

    private void AddAwaitRemoval(AwaitExpressionSyntax node)
    {
        if (node.Parent is not ExpressionStatementSyntax ||
            node.Expression is not InvocationExpressionSyntax)
        {
            return;
        }

        AddSite(
            node,
            node.AwaitKeyword.Span,
            "await",
            string.Empty,
            "remove await from invocation statement",
            "await-removal",
            "async");
    }

    private void AddThrowStatementRemoval(ThrowStatementSyntax node)
    {
        if (node.Parent is not BlockSyntax)
        {
            return;
        }

        string original = _source[node.Span.Start..node.Span.End];
        AddSite(
            node,
            node.Span,
            original,
            string.Empty,
            "remove throw statement",
            "throw-statement",
            "exception");
    }

    private void AddThrowExpressionDefaultReplacement(ThrowExpressionSyntax node)
    {
        string original = _source[node.Span.Start..node.Span.End];
        AddSite(
            node,
            node.Span,
            original,
            "default",
            "replace throw expression with default",
            "throw-expression",
            "exception");
    }

    private void AddThrownExceptionReplacement(ExpressionSyntax? expression)
    {
        if (expression is null ||
            expression.IsKind(SyntaxKind.NullLiteralExpression) ||
            !IsException(expression))
        {
            return;
        }

        string original = _source[expression.Span.Start..expression.Span.End];
        AddSite(
            expression,
            expression.Span,
            original,
            "null",
            "replace thrown exception with null",
            "throw-exception",
            "exception");
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

    private bool IsBool(ExpressionSyntax expression)
    {
        TypeInfo typeInfo = _semanticModel.GetTypeInfo(expression);
        ITypeSymbol? type = typeInfo.ConvertedType ?? typeInfo.Type;
        return type?.SpecialType == SpecialType.System_Boolean;
    }

    private bool MeetsRequirement(ExpressionSyntax expression, OperatorRequirement requirement) => requirement switch
    {
        OperatorRequirement.Any => true,
        OperatorRequirement.Numeric => IsNumeric(expression),
        OperatorRequirement.Bitwise => IsBitwise(expression),
        _ => false
    };

    private bool IsBitwise(ExpressionSyntax expression)
    {
        ITypeSymbol? type = UnwrapNullable(_semanticModel.GetTypeInfo(expression).Type);
        return type is not null &&
               (type.SpecialType == SpecialType.System_Boolean ||
                type.TypeKind == TypeKind.Enum ||
                IsIntegralType(type));
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

    private bool IsException(ExpressionSyntax expression)
    {
        TypeInfo typeInfo = _semanticModel.GetTypeInfo(expression);
        ITypeSymbol? type = typeInfo.ConvertedType ?? typeInfo.Type;
        INamedTypeSymbol? exceptionType = _semanticModel.Compilation.GetTypeByMetadataName("System.Exception");
        if (exceptionType is null)
        {
            return false;
        }

        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, exceptionType))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsEnumOrStruct(ExpressionSyntax expression)
    {
        TypeInfo typeInfo = _semanticModel.GetTypeInfo(expression);
        ITypeSymbol? type = typeInfo.ConvertedType ?? typeInfo.Type;
        return type?.TypeKind is TypeKind.Enum or TypeKind.Struct &&
            type.SpecialType != SpecialType.System_Void;
    }

    private bool IsCancellationToken(ExpressionSyntax expression)
    {
        TypeInfo typeInfo = _semanticModel.GetTypeInfo(expression);
        ITypeSymbol? type = typeInfo.ConvertedType ?? typeInfo.Type;
        INamedTypeSymbol? cancellationTokenType = _semanticModel.Compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        return type is not null &&
            cancellationTokenType is not null &&
            SymbolEqualityComparer.Default.Equals(type, cancellationTokenType);
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

    private static bool IsIntegralType(ITypeSymbol type) => type.SpecialType is
        SpecialType.System_SByte or
        SpecialType.System_Byte or
        SpecialType.System_Int16 or
        SpecialType.System_UInt16 or
        SpecialType.System_Int32 or
        SpecialType.System_UInt32 or
        SpecialType.System_Int64 or
        SpecialType.System_UInt64;

    private static ITypeSymbol? UnwrapNullable(ITypeSymbol? type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableType
            ? nullableType.TypeArguments[0]
            : type;

    private static (string Original, string Replacement, OperatorRequirement Requirement, string MutatorId, string Category)? OperatorFor(SyntaxKind kind) => kind switch
    {
        SyntaxKind.AddExpression => ("+", "-", OperatorRequirement.Numeric, "arithmetic-operator", "arithmetic"),
        SyntaxKind.SubtractExpression => ("-", "+", OperatorRequirement.Numeric, "arithmetic-operator", "arithmetic"),
        SyntaxKind.MultiplyExpression => ("*", "/", OperatorRequirement.Numeric, "arithmetic-operator", "arithmetic"),
        SyntaxKind.DivideExpression => ("/", "*", OperatorRequirement.Numeric, "arithmetic-operator", "arithmetic"),
        SyntaxKind.ModuloExpression => ("%", "*", OperatorRequirement.Numeric, "arithmetic-operator", "arithmetic"),
        SyntaxKind.LeftShiftExpression => ("<<", ">>", OperatorRequirement.Bitwise, "arithmetic-operator", "arithmetic"),
        SyntaxKind.RightShiftExpression => (">>", "<<", OperatorRequirement.Bitwise, "arithmetic-operator", "arithmetic"),
        SyntaxKind.UnsignedRightShiftExpression => (">>>", "<<", OperatorRequirement.Bitwise, "arithmetic-operator", "arithmetic"),
        SyntaxKind.BitwiseAndExpression => ("&", "|", OperatorRequirement.Bitwise, "bitwise-operator", "arithmetic"),
        SyntaxKind.BitwiseOrExpression => ("|", "&", OperatorRequirement.Bitwise, "bitwise-operator", "arithmetic"),
        SyntaxKind.ExclusiveOrExpression => ("^", "&", OperatorRequirement.Bitwise, "bitwise-operator", "arithmetic"),
        SyntaxKind.LogicalAndExpression => ("&&", "||", OperatorRequirement.Any, "logical-operator", "logical"),
        SyntaxKind.LogicalOrExpression => ("||", "&&", OperatorRequirement.Any, "logical-operator", "logical"),
        SyntaxKind.EqualsExpression => ("==", "!=", OperatorRequirement.Any, "equality-operator", "equality"),
        SyntaxKind.NotEqualsExpression => ("!=", "==", OperatorRequirement.Any, "equality-operator", "equality"),
        SyntaxKind.GreaterThanExpression => (">", ">=", OperatorRequirement.Any, "equality-operator", "equality"),
        SyntaxKind.GreaterThanOrEqualExpression => (">=", ">", OperatorRequirement.Any, "equality-operator", "equality"),
        SyntaxKind.LessThanExpression => ("<", "<=", OperatorRequirement.Any, "equality-operator", "equality"),
        SyntaxKind.LessThanOrEqualExpression => ("<=", "<", OperatorRequirement.Any, "equality-operator", "equality"),
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

    private static (string Original, string Replacement, OperatorRequirement Requirement)? AssignmentOperatorFor(SyntaxKind kind) => kind switch
    {
        SyntaxKind.AddAssignmentExpression => ("+=", "-=", OperatorRequirement.Numeric),
        SyntaxKind.SubtractAssignmentExpression => ("-=", "+=", OperatorRequirement.Numeric),
        SyntaxKind.MultiplyAssignmentExpression => ("*=", "/=", OperatorRequirement.Numeric),
        SyntaxKind.DivideAssignmentExpression => ("/=", "*=", OperatorRequirement.Numeric),
        SyntaxKind.ModuloAssignmentExpression => ("%=", "*=", OperatorRequirement.Numeric),
        SyntaxKind.AndAssignmentExpression => ("&=", "|=", OperatorRequirement.Bitwise),
        SyntaxKind.OrAssignmentExpression => ("|=", "&=", OperatorRequirement.Bitwise),
        SyntaxKind.ExclusiveOrAssignmentExpression => ("^=", "&=", OperatorRequirement.Bitwise),
        SyntaxKind.LeftShiftAssignmentExpression => ("<<=", ">>=", OperatorRequirement.Bitwise),
        SyntaxKind.RightShiftAssignmentExpression => (">>=", "<<=", OperatorRequirement.Bitwise),
        SyntaxKind.UnsignedRightShiftAssignmentExpression => (">>>=", "<<=", OperatorRequirement.Bitwise),
        _ => null
    };

    private static bool IsControlFlowBodyBlock(BlockSyntax node) => node.Parent switch
    {
        IfStatementSyntax ifStatement => ReferenceEquals(ifStatement.Statement, node),
        ElseClauseSyntax elseClause => ReferenceEquals(elseClause.Statement, node),
        WhileStatementSyntax whileStatement => ReferenceEquals(whileStatement.Statement, node),
        DoStatementSyntax doStatement => ReferenceEquals(doStatement.Statement, node),
        ForStatementSyntax forStatement => ReferenceEquals(forStatement.Statement, node),
        ForEachStatementSyntax forEachStatement => ReferenceEquals(forEachStatement.Statement, node),
        ForEachVariableStatementSyntax forEachVariableStatement => ReferenceEquals(forEachVariableStatement.Statement, node),
        UsingStatementSyntax usingStatement => ReferenceEquals(usingStatement.Statement, node),
        FixedStatementSyntax fixedStatement => ReferenceEquals(fixedStatement.Statement, node),
        LockStatementSyntax lockStatement => ReferenceEquals(lockStatement.Statement, node),
        TryStatementSyntax tryStatement => ReferenceEquals(tryStatement.Block, node),
        CatchClauseSyntax catchClause => ReferenceEquals(catchClause.Block, node),
        FinallyClauseSyntax finallyClause => ReferenceEquals(finallyClause.Block, node),
        CheckedStatementSyntax checkedStatement => ReferenceEquals(checkedStatement.Block, node),
        UnsafeStatementSyntax unsafeStatement => ReferenceEquals(unsafeStatement.Block, node),
        _ => false
    };

    private static TextSpan ObjectInitializerMemberRemovalSpan(
        SeparatedSyntaxList<ExpressionSyntax> expressions,
        int index)
    {
        ExpressionSyntax expression = expressions[index];
        if (expressions.Count == 1)
        {
            return expression.Span;
        }

        if (index < expressions.Count - 1)
        {
            SyntaxToken nextSeparator = expressions.GetSeparator(index);
            return TextSpan.FromBounds(expression.SpanStart, nextSeparator.FullSpan.End);
        }

        SyntaxToken previousSeparator = expressions.GetSeparator(index - 1);
        return TextSpan.FromBounds(previousSeparator.FullSpan.Start, expression.Span.End);
    }

    private static bool CanNegatePattern(PatternSyntax pattern) => pattern switch
    {
        VarPatternSyntax => false,
        DiscardPatternSyntax => false,
        UnaryPatternSyntax => false,
        _ => true
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

    private static string? RemovableStatementDescription(ExpressionStatementSyntax node)
    {
        if (node.Parent is not BlockSyntax)
        {
            return null;
        }

        return node.Expression switch
        {
            InvocationExpressionSyntax => "remove invocation statement",
            AwaitExpressionSyntax { Expression: InvocationExpressionSyntax } => "remove invocation statement",
            AssignmentExpressionSyntax => "remove assignment statement",
            PrefixUnaryExpressionSyntax prefix when UpdateOperatorFor(prefix.Kind()) is not null => "remove update statement",
            PostfixUnaryExpressionSyntax postfix when UpdateOperatorFor(postfix.Kind()) is not null => "remove update statement",
            _ => null
        };
    }

    private static bool IsSystemLinqMethod(IMethodSymbol method)
    {
        IMethodSymbol originalMethod = method.ReducedFrom ?? method;
        INamedTypeSymbol? containingType = originalMethod.ContainingType;
        return containingType is not null &&
               containingType.ContainingNamespace.ToDisplayString() == "System.Linq" &&
               containingType.Name is "Enumerable" or "Queryable";
    }

    private static bool IsSystemStringMethod(IMethodSymbol method) =>
        method.ContainingType?.SpecialType == SpecialType.System_String;

    private static bool IsSystemTaskMethod(IMethodSymbol method, string name)
    {
        INamedTypeSymbol? containingType = method.ContainingType;
        return method.Name == name &&
               containingType is not null &&
               containingType.Name == "Task" &&
               containingType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";
    }

    private static SimpleNameSyntax? InvocationName(InvocationExpressionSyntax node) => node.Expression switch
    {
        SimpleNameSyntax simpleName => simpleName,
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
        MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
        _ => null
    };

    private static string? StringMethodReplacementFor(IMethodSymbol method) => method.Name switch
    {
        "IsNullOrEmpty" when IsStaticStringPredicate(method) => "IsNullOrWhiteSpace",
        "IsNullOrWhiteSpace" when IsStaticStringPredicate(method) => "IsNullOrEmpty",
        "ToLower" => "ToUpper",
        "ToUpper" => "ToLower",
        "ToLowerInvariant" when method.Parameters.Length == 0 => "ToUpperInvariant",
        "ToUpperInvariant" when method.Parameters.Length == 0 => "ToLowerInvariant",
        _ => null
    };

    private static bool IsStaticStringPredicate(IMethodSymbol method) =>
        method.IsStatic &&
        method.Parameters.Length == 1 &&
        method.Parameters[0].Type.SpecialType == SpecialType.System_String;

    private bool IsDefaultCancellationToken(ExpressionSyntax expression)
    {
        if (expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
            expression is DefaultExpressionSyntax)
        {
            return true;
        }

        return _semanticModel.GetSymbolInfo(expression).Symbol is IPropertySymbol property &&
            property.Name == "None" &&
            IsCancellationTokenType(property.ContainingType);
    }

    private bool IsCancellationTokenType(ITypeSymbol? type)
    {
        INamedTypeSymbol? cancellationTokenType = _semanticModel.Compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        return type is not null &&
            cancellationTokenType is not null &&
            SymbolEqualityComparer.Default.Equals(type, cancellationTokenType);
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
