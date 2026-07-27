using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal static class InjectStyle
{
    public static bool IsCanonical(PropertyDeclarationSyntax p)
    {
        var onlyPrivateOrInternal =
            (p.Modifiers.Any(SyntaxKind.PrivateKeyword) || p.Modifiers.Any(SyntaxKind.ProtectedKeyword))
            && !p.Modifiers.Any(SyntaxKind.PublicKeyword) && !p.Modifiers.Any(SyntaxKind.InternalKeyword);

        var defaultBang =
            p.Initializer?.Value is PostfixUnaryExpressionSyntax s &&
            s.IsKind(SyntaxKind.SuppressNullableWarningExpression) &&
            s.Operand.IsKind(SyntaxKind.DefaultLiteralExpression);

        return onlyPrivateOrInternal && defaultBang;
    }

    public static PropertyDeclarationSyntax Canonicalize(PropertyDeclarationSyntax p)
    {
        var trailing = p.GetTrailingTrivia();

        var kept = p.Modifiers.Where(m =>
            !m.IsKind(SyntaxKind.PublicKeyword) &&
            !m.IsKind(SyntaxKind.PrivateKeyword) &&
            !m.IsKind(SyntaxKind.ProtectedKeyword) &&
            !m.IsKind(SyntaxKind.InternalKeyword));

        var modifiers = TokenList(new[] { Token(SyntaxKind.PrivateKeyword) }.Concat(kept));

        var initializer = EqualsValueClause(
            PostfixUnaryExpression(
                SyntaxKind.SuppressNullableWarningExpression,
                LiteralExpression(SyntaxKind.DefaultLiteralExpression)));

        var result = p;

        if (result.AccessorList is { } accessors)
            result = result.WithAccessorList(
                accessors.WithCloseBraceToken(
                    accessors.CloseBraceToken.WithTrailingTrivia(Space)));

        return result
            .WithModifiers(modifiers)
            .WithInitializer(initializer)
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken).WithTrailingTrivia(trailing))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }
}
