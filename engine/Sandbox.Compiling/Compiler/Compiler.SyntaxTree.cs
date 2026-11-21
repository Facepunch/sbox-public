using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Sandbox;

partial class Compiler
{
	/// <summary>
	/// Generated file that will get stuff like global usings and assembly attributes.
	/// </summary>
	private const string CompilerExtraPath = Sandbox.Generator.Processor.CompilerExtraPath;

	/// <summary>
	/// Collect all of the code that should compiled into this assembly
	/// </summary>
	private void GetSyntaxTree( CodeArchive archive, CSharpParseOptions options, CompilerOutput output )
	{
		try
		{
			foreach ( var location in SourceLocations )
			{
				CollectFromFilesystem( location, archive, options, output );
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, e.Message );
			AddCollectError( output, $"Error collecting source files: {e.Message}" );
		}
	}

	private static void AddCollectError( CompilerOutput output, string message )
	{
		var desc = new DiagnosticDescriptor( "SB1001", "Source Error", message, "compiler", DiagnosticSeverity.Error, true );

		lock ( output.Diagnostics )
		{
			output.Diagnostics.Add( Diagnostic.Create( desc, null ) );
		}
	}

	private SyntaxTree GetGeneratedCode( CSharpParseOptions options )
	{
		var source = GeneratedCode.ToString().ReplaceLineEndings( "\n" );
		source += "\n[assembly: System.Runtime.Versioning.TargetFramework( \".NETCoreApp,Version=v9.0\", FrameworkDisplayName = \".NET 9.0\" )]";
		return CSharpSyntaxTree.ParseText( source, options, CompilerExtraPath, Encoding.UTF8 );
	}

	/// <summary>
	/// Stamp the assembly used by hotload and the local build cache without changing the archived source.
	/// </summary>
	private static SyntaxTree StampAssembly( SyntaxTree tree, Version version )
	{
		if ( tree.FilePath != CompilerExtraPath )
			return tree;

		// Older archives carry the original stamps in the compiler-generated tree.
		if ( tree.GetCompilationUnitRoot().AttributeLists.SelectMany( list => list.Attributes )
			.Any( attribute => attribute.Name.GetLastToken().ValueText is "AssemblyVersion" or "AssemblyVersionAttribute" ) )
			return tree;

		var text = tree.GetText();
		var attributes = $"\n[assembly: global::System.Reflection.AssemblyMetadata( \"CompileTime\", {DateTime.UtcNow.ToString( "O" ).QuoteSafe()} )]" +
			$"\n[assembly: global::System.Reflection.AssemblyVersion(\"{version}\")]" +
			$"\n[assembly: global::System.Reflection.AssemblyFileVersion(\"{version}\")]";

		return tree.WithChangedText( text.WithChanges( new TextChange( new TextSpan( text.Length, 0 ), attributes ) ) );
	}

	/// <summary>
	/// Strips out disabled text trivia from the syntax tree. This is stuff like `#if false` blocks that are not compiled.
	/// </summary>
	/// <param name="tree"></param>
	/// <returns></returns>
	public static SyntaxTree StripDisabledTextTrivia( SyntaxTree tree )
	{
		var root = tree.GetRoot();

		var disabledTrivia = root.DescendantTrivia( descendIntoTrivia: true )
								 .Where( t => t.IsKind( SyntaxKind.DisabledTextTrivia ) )
								 .ToList();

		if ( disabledTrivia.Count == 0 )
			return tree;

		var newRoot = root.ReplaceTrivia(
			disabledTrivia,
			( oldTrivia, _ ) => default
		);

		return tree.WithRootAndOptions( newRoot, tree.Options );
	}

	/// <summary>
	/// Check if a file should be wrapped in conditional compilation directives
	/// </summary>
	private string GetReplacementDirective( string filePath )
	{
		foreach ( var pair in _config.ReplacementDirectives )
		{
			if ( filePath.EndsWith( pair.Key, StringComparison.OrdinalIgnoreCase ) )
				return pair.Value;
		}
		return null;
	}

	void CollectFromFilesystem( BaseFileSystem filesystem, CodeArchive targetArchive, CSharpParseOptions options, CompilerOutput output )
	{
		var files = filesystem.FindFile( "/", "*.*", true );

		var oldTrees = incrementalState.HasState ? incrementalState.SyntaxTrees
						.DistinctBy( x => x.FilePath )
						.ToDictionary( x => x.FilePath, x => x ) : null;

		System.Threading.Tasks.Parallel.ForEach( files, localPath =>
		{
			bool isAdditionalFile = localPath.EndsWith( ".razor", StringComparison.OrdinalIgnoreCase );
			bool isSourceFile = localPath.EndsWith( ".cs", StringComparison.OrdinalIgnoreCase );

			if ( !isAdditionalFile && !isSourceFile )
				return;

			// folder/is/here/file.cs => folder/is/here
			{
				var folderName = System.IO.Path.GetDirectoryName( localPath );
				var pathFolders = folderName.Split( new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries );

				// is this ignored
				if ( pathFolders.Any( x => _config.IgnoreFolders.Contains( x, StringComparer.OrdinalIgnoreCase ) ) )
					return;

				if ( pathFolders.Contains( "obj", StringComparer.OrdinalIgnoreCase ) )
					return;
			}

			// Calculate the path on real filesystem so debuggers can find it.
			var physicalPath = filesystem.GetFullPath( localPath ) ?? localPath;
			var contents = filesystem is MemoryFileSystem ? filesystem.ReadAllText( localPath ) : ReadTextForgiving( physicalPath );

			if ( contents is null )
			{
				AddCollectError( output, $"Couldn't read {localPath}" );
				return;
			}

			var hash = contents.FastHash64();

			lock ( targetArchive.FileMap )
			{
				targetArchive.FileMap[physicalPath] = localPath;
			}

			if ( !UseAbsoluteSourcePaths )
			{
				physicalPath = localPath;
			}

			if ( localPath.EndsWith( ".razor", StringComparison.OrdinalIgnoreCase ) )
			{
				lock ( targetArchive.AdditionalFiles )
				{
					targetArchive.AdditionalFiles.Add( new CodeArchive.AdditionalFile( contents, localPath ) );
				}
				targetArchive.FileHashMap[localPath] = hash;
			}

			if ( localPath.EndsWith( ".cs", StringComparison.OrdinalIgnoreCase ) )
			{
				var sourceText = SourceText.From( contents, Encoding.UTF8 );

				// Get base parse options
				var fileOptions = options;

				// Create syntax tree with file-specific options
				SyntaxTree tree;
				if ( oldTrees?.TryGetValue( physicalPath, out var existing ) ?? false )
				{
					if ( incrementalState.FileHashMap.TryGetValue( physicalPath, out var oldHash ) && oldHash == hash )
					{
						// unchanged since last compile, reuse the existing tree
						tree = existing;
					}
					else
					{
						// file content changed, create a new tree with the modified text
						tree = existing.WithChangedText( sourceText );
					}
				}
				else
				{
					tree = CSharpSyntaxTree.ParseText( text: sourceText, options: fileOptions, path: physicalPath );

					if ( _config.StripDisabledTextTrivia )
						tree = StripDisabledTextTrivia( tree );
				}

				// Handle replacements if needed
				var directive = GetReplacementDirective( localPath );
				if ( !string.IsNullOrEmpty( directive ) )
				{
					var wrappedText = $"#if {directive}\r\n{sourceText}\r\n#endif";
					var wrappedSourceText = SourceText.From( wrappedText, Encoding.UTF8 );
					tree = CSharpSyntaxTree.ParseText( wrappedSourceText, fileOptions, physicalPath );

					if ( _config.StripDisabledTextTrivia )
						tree = StripDisabledTextTrivia( tree );
				}

				lock ( targetArchive.SyntaxTrees )
				{
					targetArchive.SyntaxTrees.Add( tree );
				}
				targetArchive.FileHashMap[physicalPath] = hash;
			}
		} );
	}

	internal CSharpCompilation ReplaceSyntaxTrees( CSharpCompilation compilation, IList<SyntaxTree> syntaxTrees, out List<SyntaxTree> modifiedSyntaxTrees )
	{
		var oldTreeArray = compilation.SyntaxTrees;
		modifiedSyntaxTrees = new List<SyntaxTree>();

		var compiled = oldTreeArray
						.DistinctBy( x => x.FilePath )
						.ToDictionary( x => x.FilePath, x => x );

		var prevInputs = incrementalState.SyntaxTrees
						.DistinctBy( t => t.FilePath )
						.ToDictionary( t => t.FilePath );

		var newTrees = syntaxTrees
						.DistinctBy( x => x.FilePath )
						.ToDictionary( x => x.FilePath, x => x );

		var removed = oldTreeArray
						.Where( x => !newTrees.ContainsKey( x.FilePath ) )
						.ToArray();

		var added = syntaxTrees
						.Where( x => !compiled.ContainsKey( x.FilePath ) )
						.ToArray();

		if ( removed.Length > 0 )
		{
			compilation = compilation.RemoveSyntaxTrees( removed );
		}

		if ( added.Length > 0 )
		{
			compilation = compilation.AddSyntaxTrees( added );
			modifiedSyntaxTrees.AddRange( added );
		}

		foreach ( var (path, newTree) in newTrees )
		{
			if ( !prevInputs.TryGetValue( path, out var prevInput ) )
				continue; // handled as add

			if ( !compiled.TryGetValue( path, out var compiledTree ) )
				continue; // removed or not generated

			if ( ReferenceEquals( prevInput, newTree ) )
				continue;

			if ( _currentArchive is not null )
			{
				// special case for networked archives, where syntax trees are always different instances
				if ( prevInput.GetText().ContentEquals( newTree.GetText() ) )
					continue;
			}

			compilation = compilation.ReplaceSyntaxTree( compiledTree, newTree );
			modifiedSyntaxTrees.Add( newTree );
		}

		return compilation;
	}

	internal Dictionary<string, object> ChangeSummary => incrementalState.GetChangeSummary( SourceLocations );
}
