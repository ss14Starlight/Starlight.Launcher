using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

#pragma warning disable RS1038
[DiagnosticAnalyzer(LanguageNames.CSharp)]
#pragma warning restore RS1038
public sealed class InjectStyleAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MY0002";
    private static readonly DiagnosticDescriptor _rule = new(
        DiagnosticId,
        title: "[Inject] property style",
#pragma warning disable RS1032
        messageFormat: "The [Inject] property '{0}' must be private or protected and should have 'default!' as value!",
#pragma warning restore RS1032
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(_rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var inject = start.Compilation
                .GetTypeByMetadataName("Microsoft.AspNetCore.Components.InjectAttribute");
            if (inject is null) return;

            start.RegisterSyntaxNodeAction(ctx => Analyze(ctx, inject), SyntaxKind.PropertyDeclaration);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol inject)
    {
        var p = (PropertyDeclarationSyntax)context.Node;

        var hasInject = p.AttributeLists.SelectMany(l => l.Attributes).Any(a =>
            context.SemanticModel.GetSymbolInfo(a, context.CancellationToken).Symbol is IMethodSymbol ctor &&
            SymbolEqualityComparer.Default.Equals(ctor.ContainingType, inject));

        if (!hasInject || InjectStyle.IsCanonical(p)) return;

        context.ReportDiagnostic(Diagnostic.Create(_rule, p.Identifier.GetLocation(), p.Identifier.Text));
    }
}

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InjectStyleCodeFixProvider)), Shared]
public sealed class InjectStyleCodeFixProvider : CodeFixProvider
{
    private const string Title = "Rewrite to 'private ... = default!;'";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(InjectStyleAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics[0];
        var property = root?.FindNode(diagnostic.Location.SourceSpan)
            .FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (property is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(Title,
                ct => FixAsync(context.Document, property, ct),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> FixAsync(Document doc, PropertyDeclarationSyntax property, CancellationToken ct)
    {
        var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        return doc.WithSyntaxRoot(root!.ReplaceNode(property, InjectStyle.Canonicalize(property)));
    }
}
