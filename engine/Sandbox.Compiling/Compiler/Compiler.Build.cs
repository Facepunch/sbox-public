using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Collections.Immutable;
using System.Threading;

namespace Sandbox;

partial class Compiler
{
	private static readonly DiagnosticDescriptor WhitelistRule = new DiagnosticDescriptor(
		id: "SB1000",
		title: "Whitelist Error",
		messageFormat: "'{0}' is not allowed when whitelist is enabled",
		helpLinkUri: "https://sbox.game/dev/doc/code/code-basics/api-whitelist/",
		category: "Usage",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true );

	CodeArchive _currentArchive;

	/// <summary>
	/// Task completed at the end of <see cref="BuildAsync"/>, for other compilers to await if
	/// they reference this one.
	/// </summary>
	private TaskCompletionSource<CompilerOutput> _compileTcs;

	/// <summary>
	/// Fill this compiler from a code archive
	/// </summary>
	public void UpdateFromArchive( CodeArchive a )
	{
		_currentArchive = a;

		CopyReferencesFromArchive( a );
		MarkForRecompile();
	}

	/// <summary>
	/// Called by <see cref="CompileGroup"/> before a build starts. Prepares this compiler
	/// to be referenced by other compilers before they build with <see cref="BuildAsync"/>.
	/// </summary>
	internal void PreBuild()
	{
		Assert.False( IsBuilding, "This compiler is already building" );

		// We set up the TCS here so other compilers can use it when they BuildReferencesAsync(),
		// avoiding a race condition if we set it up at the start of BuildAsync()

		_compileTcs = new TaskCompletionSource<CompilerOutput>();
	}

	/// <summary>
	/// Build and load the assembly.
	/// </summary>
	internal async Task BuildAsync()
	{
		Assert.True( IsBuilding, $"{nameof( PreBuild )} must be called first" );

		log.Trace( "Build Start" );

		var output = new CompilerOutput( this );

		Interlocked.Increment( ref compileCounter );

		output.Version = Version.Parse( $"0.0.{compileCounter}.0" );

		try
		{
			// Do the expensive archive building on a worker thread

			var archive = await Task.Run( () => BuildArchive( output ) );

			// Build a list of references, waiting for other compilers to finish if needed

			var refs = await BuildReferencesAsync( archive );

			// Actually compile, again on a worker thread since it's expensive

			await Task.Run( () => BuildInternal( refs, output ) );
		}
		catch ( System.Exception e )
		{
			output.Exception = e;
			log.Warning( e, e.Message );
		}
		finally
		{
			Output = output;

			_compileTcs.SetResult( output );

			log.Trace( "Build Finished" );
		}
	}

	void CopyReferencesFromArchive( CodeArchive a )
	{
		_references.Clear();

		foreach ( var reference in a.References )
		{
			_references.Add( reference );
		}
	}

	CodeArchive BuildArchive( CompilerOutput output )
	{
		if ( _currentArchive is not null )
		{
			output.Archive = _currentArchive;
			return _currentArchive;
		}

		var archive = new CodeArchive();
		archive.CompilerName = Name;
		archive.Configuration = _config;
		output.Archive = archive;

		var parseOptions = _config.GetParseOptions();

		//
		// References
		//
		foreach ( var e in _references )
		{
			archive.References.Add( e );
		}

		//
		// Syntax trees
		//
		GetSyntaxTree( archive, parseOptions );

		if ( GetGeneratedCode( output.Version, parseOptions ) is SyntaxTree generated )
		{
			archive.SyntaxTrees.Add( generated );
		}

		archive.SyntaxTrees.Sort( ( a, b ) => string.CompareOrdinal( a.FilePath, b.FilePath ) );

		return archive;
	}

	void BuildInternal( IReadOnlyList<PortableExecutableReference> refs, CompilerOutput output )
	{
		var archive = output.Archive;
		var releaseMode = archive.Configuration.ReleaseMode == ReleaseMode.Release;
		var conf = archive.Configuration;

		var options = incrementalState.Compilation?.Options;


		options ??= new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary )
							.WithConcurrentBuild( true )
							.WithGeneralDiagnosticOption( ReportDiagnostic.Info )
							.WithPlatform( Microsoft.CodeAnalysis.Platform.AnyCpu );

		options = options
					.WithDeterministic( releaseMode ? true : false )
					.WithOptimizationLevel( releaseMode ? OptimizationLevel.Release : OptimizationLevel.Debug )
					.WithGeneralDiagnosticOption( conf.TreatWarningsAsErrors ? ReportDiagnostic.Error : ReportDiagnostic.Default )
					.WithSpecificDiagnosticOptions( conf.GetReportDiagnostics() )
					.WithNullableContextOptions( conf.Nullables ? NullableContextOptions.Enable : NullableContextOptions.Disable )
					.WithAllowUnsafe( conf.Unsafe );

		CSharpCompilation compiler;


		if ( incrementalState.HasState )
		{
			compiler = incrementalState.Compilation
							.WithAssemblyName( AssemblyName )
							.WithOptions( options );

			var oldRefs = compiler.References.ToHashSet();

			if ( !oldRefs.SetEquals( refs ) )
			{
				compiler = compiler.WithReferences( refs );
			}

			compiler = ReplaceSyntaxTrees( compiler, archive.SyntaxTrees );
		}
		else
		{
			compiler = CSharpCompilation.Create( AssemblyName, archive.SyntaxTrees, refs, options );
		}

		//
		// Process Razor files and add the generated syntax trees to the compilation
		//
		var razorTrees = ProcessRazorFiles( archive, output );
		if ( razorTrees.Count > 0 )
		{
			compiler = compiler.AddSyntaxTrees( razorTrees );
		}

		bool ilHotloadSupported;
		ImmutableArray<SyntaxTree> beforeIlHotloadProcessingTrees;

		{
			var processor = RunGenerators( compiler, output );

			compiler = processor.Compilation;

			ilHotloadSupported = processor.ILHotloadSupported;
			beforeIlHotloadProcessingTrees = processor.BeforeILHotloadProcessingTrees;

			// If you have any errors in codegen don't bother compiling, developer should sort it out
			if ( processor.Diagnostics.Any( x => x.Severity == DiagnosticSeverity.Error ) )
			{
				return;
			}
		}

		// check for blacklisted methods/types used in compilation
		// we need this because the c# compiler will post optimize and use tons of blacklisted methods
		// run this after generators because they can contain user inputs too
		if ( _config.Whitelist )
		{
			RunBlacklistWalker( compiler, output );

			// Errors, fail
			if ( output.Diagnostics.Any( x => x.Severity == DiagnosticSeverity.Error ) )
			{
				return;
			}
		}

		using ( var xmlStream = new System.IO.MemoryStream() )
		using ( var peStream = new System.IO.MemoryStream() )
		{
			var emitOptions = new EmitOptions()
				.WithDebugInformationFormat( DebugInformationFormat.Embedded );

			BuildResult = compiler.Emit( peStream: peStream, xmlDocumentationStream: xmlStream, options: emitOptions );

			if ( BuildResult.Success )
			{
				output.Successful = true;

				peStream.Seek( 0, System.IO.SeekOrigin.Begin );

				if ( _config.Whitelist && Group.AccessControl is { } access )
				{
					var result = access.VerifyAssembly( peStream, out TrustedBinaryStream stream );
					if ( !result.Success )
					{
						log.Error( "Whitelist violation(s), build unsuccessful." );

						output.Successful = false;

						foreach ( var error in result.WhitelistErrors )
						{
							foreach ( var location in error.Locations )
							{
								output.Diagnostics.Add( Diagnostic.Create( WhitelistRule, location.RoslynLocation ?? Location.None, error.Name ) );
							}
						}
					}
				}
			}

			output.AssemblyData = peStream.ToArray();
			output.XmlDocumentation = System.Text.Encoding.UTF8.GetString( xmlStream.ToArray() );
		}

		output.Diagnostics.AddRange( BuildResult.Diagnostics );

		if ( !BuildResult.Success )
		{
			return;
		}

		incrementalState.Update( archive.SyntaxTrees.ToImmutableArray(), beforeIlHotloadProcessingTrees, compiler );

		using ( var a_stream = new System.IO.MemoryStream( output.AssemblyData ) )
		{
			MetadataReference = output.MetadataReference = Microsoft.CodeAnalysis.MetadataReference.CreateFromStream( a_stream );

			if ( MetadataReference == null )
				throw new System.Exception( "metaRef is null!" );
		}

		if ( !ilHotloadSupported )
		{
			_recentMetadataReferences.Clear();
		}

		_recentMetadataReferences.Add( output.Version, MetadataReference );
	}
}
