using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.IO;

namespace Sandbox;

/// <summary>
/// Holds state for incremental compilation. Ask Matt or James to add comments, I don't know.
/// </summary>
class IncrementalCompileState
{
	public ImmutableArray<SyntaxTree> OldSyntaxTrees;
	public ImmutableArray<SyntaxTree> SyntaxTrees;
	public ImmutableArray<SyntaxTree> PreHotloadSyntaxTrees;

	public ImmutableDictionary<string, ulong> FileHashMap { get; private set; }

	public CSharpCompilation Compilation;

	public bool HasState => Compilation is not null;

	internal void Reset()
	{
		Compilation = null;
		OldSyntaxTrees = default;
		SyntaxTrees = default;
		PreHotloadSyntaxTrees = default;

		FileHashMap = [];
	}

	internal void Update( CodeArchive archive, IEnumerable<SyntaxTree> syntaxTrees, ImmutableArray<SyntaxTree> beforeIlHotloadProcessingTrees, CSharpCompilation compiler )
	{
		OldSyntaxTrees = SyntaxTrees;
		SyntaxTrees = syntaxTrees.ToImmutableArray();
		PreHotloadSyntaxTrees = beforeIlHotloadProcessingTrees;
		Compilation = compiler;

		FileHashMap = archive.FileHashMap.ToImmutableDictionary();
	}

	public Dictionary<string, object> GetChangeSummary( IEnumerable<BaseFileSystem> fileLocations = default )
	{
		if ( OldSyntaxTrees.IsDefaultOrEmpty || SyntaxTrees.IsDefaultOrEmpty )
		{
			return new Dictionary<string, object>();
		}

		var filePaths = OldSyntaxTrees.Select( x => x.FilePath )
			.Union( SyntaxTrees.Select( x => x.FilePath ) )
			.OrderBy( x => x )
			.ToArray();

		var result = new Dictionary<string, object>();
		using var writer = new StringWriter();

		foreach ( var filePath in filePaths )
		{
			var localPath = fileLocations?.Select( x =>
			{
				try
				{
					return x.GetRelativePath( filePath );
				}
				catch
				{
					return null;
				}
			} ).FirstOrDefault( x => x != null ) ?? Path.GetFileName( filePath );

			var oldTree = OldSyntaxTrees.FirstOrDefault( x => x.FilePath.Equals( filePath, StringComparison.OrdinalIgnoreCase ) );
			var newTree = SyntaxTrees.FirstOrDefault( x => x.FilePath.Equals( filePath, StringComparison.OrdinalIgnoreCase ) );

			if ( oldTree == null && newTree != null )
			{
				writer.WriteLine( $"{localPath}: Added" );
				continue;
			}

			if ( oldTree != null && newTree == null )
			{
				writer.WriteLine( $"{localPath}: Removed" );
				continue;
			}

			var changes = Generator.ILHotloadProcessor.GetChanges( oldTree, newTree );

			if ( string.IsNullOrEmpty( changes ) )
			{
				continue;
			}

			writer.WriteLine( $"{localPath}: Modified" );
			writer.WriteLine( $"  {changes.Replace( "\n", "\n  " )}" );
		}

		result["Details"] = writer.ToString();

		return result;
	}
}
