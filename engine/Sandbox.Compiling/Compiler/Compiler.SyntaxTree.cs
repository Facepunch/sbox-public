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
	private void GetSyntaxTree( CodeArchive archive, CSharpParseOptions options )
	{
		try
		{
			foreach ( var location in SourceLocations )
			{
				CollectFromFilesystem( location, archive, options );
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, e.Message );
		}
	}

	private SyntaxTree GetGeneratedCode( Version version, CSharpParseOptions options )
	{
		var generatedCode = GeneratedCode.ToString();

		generatedCode += $"{Environment.NewLine}[assembly: System.Runtime.Versioning.TargetFramework( \".NETCoreApp,Version=v9.0\", FrameworkDisplayName = \".NET 9.0\" )]";

		if ( version != null )
		{
			generatedCode += $"{Environment.NewLine}[assembly: global::System.Reflection.AssemblyVersion(\"{version}\")]";
			generatedCode += $"{Environment.NewLine}[assembly: global::System.Reflection.AssemblyFileVersion(\"{version}\")]";
		}

		if ( string.IsNullOrEmpty( generatedCode ) )
			return default;

		var tree = CSharpSyntaxTree.ParseText( text: generatedCode, options: options, path: CompilerExtraPath, encoding: System.Text.Encoding.UTF8 );
		return tree;

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

	void CollectFromFilesystem( BaseFileSystem filesystem, CodeArchive targetArchive, CSharpParseOptions options )
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

			if ( contents is null ) return;

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
			}

			if ( localPath.EndsWith( ".cs", StringComparison.OrdinalIgnoreCase ) )
			{
				var sourceText = SourceText.From( contents, Encoding.UTF8 );

				// Get base parse options
				var fileOptions = options;

				// Create syntax tree with file-specific options
				SyntaxTree tree;
				if ( oldTrees?.TryGetValue( physicalPath, out tree ) ?? false )
				{
					tree = tree.WithChangedText( sourceText );
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
			}
		} );
	}

	private static CSharpCompilation ReplaceSyntaxTrees( CSharpCompilation compilation, IList<SyntaxTree> syntaxTrees )
	{
		var oldTreeArray = compilation.SyntaxTrees;

		var oldTrees = oldTreeArray
						.DistinctBy( x => x.FilePath )
						.ToDictionary( x => x.FilePath, x => x );

		var newTrees = syntaxTrees
						.DistinctBy( x => x.FilePath )
						.ToDictionary( x => x.FilePath, x => x );

		var removed = oldTreeArray
						.Where( x => !newTrees.ContainsKey( x.FilePath ) )
						.ToArray();

		var added = syntaxTrees
						.Where( x => !oldTrees.ContainsKey( x.FilePath ) )
						.ToArray();

		if ( removed.Length > 0 )
		{
			compilation = compilation.RemoveSyntaxTrees( removed );
		}

		if ( added.Length > 0 )
		{
			compilation = compilation.AddSyntaxTrees( added );
		}

		foreach ( var oldTree in oldTreeArray )
		{
			if ( newTrees.TryGetValue( oldTree.FilePath, out var newTree ) )
			{
				compilation = compilation.ReplaceSyntaxTree( oldTree, newTree );
			}
		}

		return compilation;
	}

	internal Dictionary<string, object> ChangeSummary => incrementalState.GetChangeSummary( SourceLocations );
}
