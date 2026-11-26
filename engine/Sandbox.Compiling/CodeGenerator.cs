using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.IO;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Generates C# source from in-memory .cs, declaration-only .csx and .razor files using the addon generators.
/// Does not emit assemblies, execute code or perform sandbox access validation.
/// Configure on one thread; each generation snapshots its inputs before starting background work.
/// </summary>
public sealed class CodeGenerator
{
	private readonly Dictionary<string, string> files = new( StringComparer.OrdinalIgnoreCase );
	private readonly HashSet<MetadataReference> references = new();

	/// <summary>
	/// The assembly identity used for semantic analysis and generated metadata.
	/// </summary>
	public string AssemblyName { get; set; } = "GeneratedCode";

	/// <summary>
	/// Root namespace used by Razor, followed by the source file's folders.
	/// </summary>
	public string RootNamespace { get; set; } = "Sandbox";

	/// <summary>
	/// Namespaces imported by every file. Imports are included in the output source set.
	/// </summary>
	public string[] GlobalUsings { get; set; } = [];

	/// <summary>
	/// Preprocessor symbols used when parsing the input files.
	/// </summary>
	public string[] Defines { get; set; } = ["SANDBOX"];

	/// <summary>
	/// Generates declarations for Breen, which binds script bodies and can invalidate callbacks on live instances.
	/// </summary>
	internal bool ForScriptRuntime { get; set; }

	/// <summary>
	/// Adds or replaces source at a project-relative path. No file is read from disk.
	/// </summary>
	public void AddFile( string path, string contents )
	{
		ArgumentNullException.ThrowIfNull( contents );
		path = NormalizePath( path );
		var extension = Path.GetExtension( path );
		if ( !extension.Equals( ".cs", StringComparison.OrdinalIgnoreCase )
			&& !extension.Equals( ".csx", StringComparison.OrdinalIgnoreCase )
			&& !extension.Equals( ".razor", StringComparison.OrdinalIgnoreCase ) )
		{
			throw new ArgumentException( "Expected a .cs, .csx or .razor file.", nameof( path ) );
		}

		files[path] = contents;
	}

	/// <summary>
	/// Removes an input file, returning whether it was present.
	/// </summary>
	public bool RemoveFile( string path ) => files.Remove( NormalizePath( path ) );

	/// <summary>
	/// Removes all input files, retaining references and settings. Already-started generations keep their snapshots.
	/// </summary>
	public void ClearFiles() => files.Clear();

	/// <summary>
	/// Adds a metadata reference for semantic analysis. References are supplied by the caller, not discovered globally.
	/// </summary>
	public void AddReference( MetadataReference reference )
	{
		ArgumentNullException.ThrowIfNull( reference );
		references.Add( reference );
	}

	/// <summary>
	/// Generates a complete source set from a snapshot of the current inputs, retaining the original inputs for later calls.
	/// Cancellation is observed between generation stages; an active generator pass may finish before cancellation is observed.
	/// </summary>
	public Task<Result> GenerateAsync( CancellationToken cancellationToken = default )
	{
		var inputs = files.OrderBy( x => x.Key, StringComparer.Ordinal ).ToArray();
		var rootNamespace = RootNamespace;
		var forScriptRuntime = ForScriptRuntime;
		var parseOptions = new CSharpParseOptions( LanguageVersion.Default, preprocessorSymbols: Defines.ToArray() );
		var compilation = CSharpCompilation.Create( AssemblyName, references: references.ToArray(),
			options: new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );

		if ( GlobalUsings.Length > 0 )
		{
			var imports = string.Join( "\n", GlobalUsings.Select( name => $"global using {name};" ) );
			compilation = compilation.AddSyntaxTrees( CSharpSyntaxTree.ParseText( imports, parseOptions,
				Generator.Processor.CompilerExtraPath, Encoding.UTF8, cancellationToken ) );
		}

		return Task.Run( () => Generate( compilation, inputs, rootNamespace, parseOptions, forScriptRuntime, cancellationToken ), cancellationToken );
	}

	private static Result Generate( CSharpCompilation compilation, KeyValuePair<string, string>[] inputs, string rootNamespace, CSharpParseOptions parseOptions, bool forScriptRuntime, CancellationToken cancellationToken )
	{
		var diagnostics = new List<Diagnostic>();
		var sourcePaths = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var trees = new List<SyntaxTree>();

		foreach ( var (path, contents) in inputs )
		{
			cancellationToken.ThrowIfCancellationRequested();
			var outputPath = path;
			var text = contents;
			if ( path.EndsWith( ".razor", StringComparison.OrdinalIgnoreCase ) )
			{
				var document = Razor.RazorProcessor.GenerateDocument( contents, path, rootNamespace );
				text = document.GeneratedCode;
				// The existing generator recognizes this naming convention and preserves Razor's own line mappings.
				outputPath = $"_gen_{Path.GetFileName( path )}_{path.FastHash():x}.cs";
				var source = SourceText.From( contents );
				foreach ( var diagnostic in document.Diagnostics )
				{
					var span = diagnostic.Span;
					var start = Math.Clamp( span.AbsoluteIndex, 0, source.Length );
					var range = new TextSpan( start, Math.Clamp( span.Length, 0, source.Length - start ) );
					var severity = diagnostic.Severity == Microsoft.AspNetCore.Razor.Language.RazorDiagnosticSeverity.Error
						? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
					diagnostics.Add( Diagnostic.Create( new DiagnosticDescriptor( diagnostic.Id, "Razor", "{0}", "Razor", severity, true ),
						Location.Create( path, range, source.Lines.GetLinePositionSpan( range ) ), diagnostic.GetMessage() ) );
				}
			}

			if ( !sourcePaths.TryAdd( outputPath, path ) )
			{
				diagnostics.Add( Error( "SB6001", $"Duplicate generated path '{outputPath}'." ) );
				continue;
			}

			var tree = CSharpSyntaxTree.ParseText( text, parseOptions, outputPath, Encoding.UTF8, cancellationToken );
			trees.Add( tree );
			if ( path.EndsWith( ".csx", StringComparison.OrdinalIgnoreCase ) )
			{
				foreach ( var statement in tree.GetRoot( cancellationToken ).ChildNodes().OfType<GlobalStatementSyntax>() )
				{
					diagnostics.Add( Error( "SB6003", "Code generation supports declarations in .csx files; top-level script code is not supported.", statement.GetLocation() ) );
				}
			}
		}

		compilation = compilation.AddSyntaxTrees( trees );
		diagnostics.AddRange( compilation.GetParseDiagnostics( cancellationToken ) );
		if ( !diagnostics.Any( x => x.Severity == DiagnosticSeverity.Error ) )
		{
			cancellationToken.ThrowIfCancellationRequested();
			var processor = new Generator.Processor
			{
				AddonName = compilation.AssemblyName,
				AddonFileMap = sourcePaths,
				CachePropertyDelegates = !forScriptRuntime
			};
			processor.Run( compilation );
			compilation = processor.Compilation;
			diagnostics.AddRange( processor.Diagnostics );
			if ( processor.Exception is { } exception )
			{
				diagnostics.Add( Error( "SB5000", $"Code generation failed: {exception.Message}" ) );
			}
		}

		cancellationToken.ThrowIfCancellationRequested();
		if ( !forScriptRuntime ) diagnostics.AddRange( compilation.GetDiagnostics( cancellationToken ) );
		foreach ( var duplicate in compilation.SyntaxTrees.GroupBy( x => x.FilePath, StringComparer.OrdinalIgnoreCase ).Where( group => group.Count() > 1 ) )
		{
			diagnostics.Add( Error( "SB6001", $"Duplicate generated path '{duplicate.Key}'." ) );
		}

		var output = compilation.SyntaxTrees.OrderBy( x => x.FilePath, StringComparer.Ordinal )
			.Select( tree => new SourceFile( tree.FilePath, GetOutputText( tree, parseOptions, cancellationToken ), sourcePaths.GetValueOrDefault( tree.FilePath ) ) )
			.ToImmutableArray();

		return new( output, diagnostics.Distinct().ToImmutableArray() );
	}

	/// <summary>
	/// Makes the selected conditional branches reproducible when the returned text is parsed without the original options.
	/// </summary>
	private static string GetOutputText( SyntaxTree tree, CSharpParseOptions options, CancellationToken cancellationToken )
	{
		var defines = string.Concat( options.PreprocessorSymbolNames.Select( name => $"#define {name}\n" ) );
		return defines + $"#line 1 {SymbolDisplay.FormatLiteral( tree.FilePath, true )}\n" + tree.GetText( cancellationToken );
	}

	private static Diagnostic Error( string id, string message, Location location = null ) => Diagnostic.Create(
		new DiagnosticDescriptor( id, "Code generation", "{0}", "CodeGeneration", DiagnosticSeverity.Error, true ), location ?? Location.None, message );

	private static string NormalizePath( string path )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		path = path.Replace( '\\', '/' );
		var parts = path.Split( '/', StringSplitOptions.RemoveEmptyEntries ).Where( part => part != "." ).ToArray();
		if ( path.StartsWith( '/' ) || path.Contains( ':' ) || parts.Length == 0 || parts.Contains( ".." ) )
		{
			throw new ArgumentException( "Expected a project-relative path without parent traversal.", nameof( path ) );
		}

		return string.Join( '/', parts );
	}

	/// <summary>
	/// One file in the complete generated C# source set.
	/// </summary>
	/// <param name="Path">The logical output path.</param>
	/// <param name="Text">The generated source, including source line mappings.</param>
	/// <param name="SourcePath">The originating input path, or null for shared generated code.</param>
	public sealed record SourceFile( string Path, string Text, string SourcePath );

	/// <summary>
	/// Generated source and diagnostics. Does not certify Breen compatibility or sandbox permissions.
	/// </summary>
	/// <param name="Files">The complete output source set. Failed results may contain partially transformed source.</param>
	/// <param name="Diagnostics">Razor, generator and C# analysis diagnostics.</param>
	public sealed record Result( ImmutableArray<SourceFile> Files, ImmutableArray<Diagnostic> Diagnostics )
	{
		/// <summary>
		/// Whether generation and C# analysis completed without errors. No assembly has been emitted.
		/// </summary>
		public bool Success => !Diagnostics.Any( x => x.Severity == DiagnosticSeverity.Error );
	}
}
