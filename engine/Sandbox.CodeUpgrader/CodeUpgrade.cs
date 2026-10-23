using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace Sandbox.CodeUpgrader;

/// <summary>
/// Applies opted-in IDE code fixes to an in-memory project before compilation.
/// Does not save documents or execute workspace/UI operations.
/// </summary>
public static class CodeUpgrade
{
	static readonly Type[] FixerTypes = typeof( Fixer ).Assembly.GetTypes()
		.Where( t => !t.IsAbstract && typeof( Fixer ).IsAssignableFrom( t ) && t.GetCustomAttribute<ExportCodeFixProviderAttribute>() is not null )
		.OrderBy( t => t.FullName, StringComparer.Ordinal ).ToArray();

	public static async Task<CSharpCompilation> UpgradeAsync( CSharpCompilation input, CancellationToken cancellationToken = default )
	{
		var fixers = new List<Fixer>();
		var needsUpgrade = false;
		foreach ( var type in FixerTypes )
		{
			var fixer = (Fixer)Activator.CreateInstance( type );
			if ( !fixer.CanApplyDuringCompilation( input.SyntaxTrees ) ) continue;
			fixers.Add( fixer );
			var diagnostics = await GetDiagnosticsAsync( input, fixer, cancellationToken ).ConfigureAwait( false );
			needsUpgrade |= diagnostics.Any( d => fixer.FixableDiagnosticIds.Contains( d.Id ) );
		}
		// Most archives need no edits. Keep their original compilation and avoid a workspace.
		if ( !needsUpgrade ) return input;

		using var workspace = new AdhocWorkspace();
		var project = workspace.AddProject( input.AssemblyName, LanguageNames.CSharp )
			.WithCompilationOptions( input.Options ).WithMetadataReferences( input.References )
			.WithParseOptions( input.SyntaxTrees.First().Options );
		var documents = new Dictionary<DocumentId, SyntaxTree>();
		foreach ( var tree in input.SyntaxTrees )
		{
			var document = project.AddDocument( tree.FilePath, await tree.GetRootAsync( cancellationToken ).ConfigureAwait( false ), filePath: tree.FilePath );
			documents.Add( document.Id, tree );
			project = document.Project;
		}

		// Rebind after each action: a fix may change declarations used by another document/fixer.
		// Bound the loop so a broken or oscillating code fix fails compilation rather than hanging it.
		for ( var pass = 0; pass < 100; pass++ )
		{
			cancellationToken.ThrowIfCancellationRequested();
			var changed = false;
			foreach ( var fixer in fixers )
			{
				var compilation = await project.GetCompilationAsync( cancellationToken ).ConfigureAwait( false );
				var diagnostics = await GetDiagnosticsAsync( compilation, fixer, cancellationToken ).ConfigureAwait( false );
				var diagnostic = diagnostics.Where( d => fixer.FixableDiagnosticIds.Contains( d.Id ) )
					.OrderBy( d => d.Location.SourceTree?.FilePath, StringComparer.Ordinal ).ThenBy( d => d.Location.SourceSpan.Start ).FirstOrDefault();
				if ( diagnostic is null ) continue;
				var document = project.GetDocument( diagnostic.Location.SourceTree );
				if ( document is null ) throw new InvalidOperationException( $"{diagnostic.Id} requires a source document." );

				var actions = new List<CodeAction>();
				await fixer.RegisterCodeFixesAsync( new CodeFixContext( document, diagnostic, ( action, _ ) => actions.Add( action ), cancellationToken ) ).ConfigureAwait( false );
				if ( actions.Count != 1 ) throw new InvalidOperationException( $"{diagnostic.Id} must offer exactly one automatic fix." );
				var operations = await actions[0].GetOperationsAsync( cancellationToken ).ConfigureAwait( false );
				if ( operations.Length != 1 || operations[0] is not ApplyChangesOperation apply )
					throw new InvalidOperationException( $"{diagnostic.Id} must only change source documents." );

				var changes = apply.ChangedSolution.GetChanges( project.Solution );
				var projectChanges = changes.GetProjectChanges().ToArray();
				if ( changes.GetAddedProjects().Any() || changes.GetRemovedProjects().Any() || projectChanges.Length != 1
					|| projectChanges[0].ProjectId != project.Id
					|| projectChanges[0].GetAddedDocuments().Any() || projectChanges[0].GetRemovedDocuments().Any()
					|| !projectChanges[0].GetChangedDocuments().Any() )
					throw new InvalidOperationException( $"{diagnostic.Id} must edit existing documents in this project." );
				project = apply.ChangedSolution.GetProject( project.Id );
				changed = true;
				break;
			}

			if ( changed ) continue;
			// Preserve the caller's compilation, parse options, encoding and tree identities wherever possible.
			var result = input;
			foreach ( var pair in documents )
			{
				var root = await project.GetDocument( pair.Key ).GetSyntaxRootAsync( cancellationToken ).ConfigureAwait( false );
				var tree = pair.Value.WithRootAndOptions( root, pair.Value.Options );
				if ( !tree.GetText( cancellationToken ).ContentEquals( pair.Value.GetText( cancellationToken ) ) )
					result = result.ReplaceSyntaxTree( pair.Value, tree );
			}
			return result;
		}
		throw new InvalidOperationException( "Automatic code fixes did not converge after 100 passes." );
	}

	static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync( Compilation compilation, Fixer fixer, CancellationToken cancellationToken )
	{
		var diagnostics = await compilation.WithAnalyzers( ImmutableArray.Create<DiagnosticAnalyzer>( fixer.Target ) )
			.GetAnalyzerDiagnosticsAsync( cancellationToken ).ConfigureAwait( false );
		if ( diagnostics.Any( d => d.Id == "AD0001" ) )
			throw new InvalidOperationException( string.Join( "\n", diagnostics ) );
		return diagnostics;
	}
}
