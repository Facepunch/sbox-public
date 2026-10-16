using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Facepunch;

/// <summary>
/// What the last run generated from: the tools that ran it, and for each file a hash of its text
/// and a hash of its declarations (its text with method bodies left out).
/// </summary>
class State
{
	const string Path = "obj/.generated/codegen.state";

	string Tools;
	Dictionary<string, (string Text, string Declarations)> Files = new();

	/// <summary>
	/// The state for these trees. A file whose text hasn't changed keeps its old declarations hash.
	/// </summary>
	public static State Create( List<SyntaxTree> trees, State previous )
	{
		var state = new State { Tools = ToolsId() };
		var hashes = new (string Text, string Declarations)[trees.Count];

		Parallel.For( 0, trees.Count, i =>
		{
			var tree = trees[i];
			var text = tree.GetText().ToString();
			var textHash = Hash( text );

			if ( previous?.Files.TryGetValue( tree.FilePath, out var old ) == true && old.Text == textHash )
				hashes[i] = old;
			else
				hashes[i] = (textHash, DeclarationsHash( tree, text ));
		} );

		for ( int i = 0; i < trees.Count; i++ )
			state.Files[trees[i].FilePath] = hashes[i];

		return state;
	}

	/// <summary>
	/// The trees to generate again since the previous state, or null when it has to be all of them:
	/// the tools changed, a file was added or removed, or a change reached a declaration that other
	/// files' generated code can depend on.
	/// </summary>
	public List<SyntaxTree> ChangedSince( State previous, List<SyntaxTree> trees )
	{
		if ( previous is null )
			return null;

		if ( previous.Tools != Tools )
			return Full( "the generator changed" );

		if ( previous.Files.Count != Files.Count )
			return Full( "files were added or removed" );

		var changed = new List<SyntaxTree>();

		foreach ( var tree in trees )
		{
			if ( !previous.Files.TryGetValue( tree.FilePath, out var old ) )
				return Full( $"{tree.FilePath} is new" );

			var current = Files[tree.FilePath];
			if ( current.Declarations != old.Declarations )
				return Full( $"declarations changed in {tree.FilePath}" );

			if ( current.Text != old.Text || !File.Exists( Program.GetTargetPath( tree.FilePath ) ) )
				changed.Add( tree );
		}

		return changed;
	}

	static List<SyntaxTree> Full( string reason )
	{
		Console.WriteLine( $"Generating every file: {reason}" );
		return null;
	}

	public static State Load()
	{
		try
		{
			if ( !File.Exists( Path ) ) return null;

			var lines = File.ReadAllLines( Path );
			var state = new State { Tools = lines[0] };

			foreach ( var line in lines.Skip( 1 ) )
			{
				var parts = line.Split( '\t' );
				state.Files[parts[0]] = (parts[1], parts[2]);
			}

			return state;
		}
		catch ( Exception e )
		{
			Console.WriteLine( $"Ignoring unreadable {Path} ({e.Message})" );
			return null;
		}
	}

	public void Save()
	{
		var sb = new StringBuilder();
		sb.AppendLine( Tools );

		foreach ( var (file, hashes) in Files )
			sb.AppendLine( $"{file}\t{hashes.Text}\t{hashes.Declarations}" );

		File.WriteAllText( Path, sb.ToString() );
	}

	public static void Delete()
	{
		if ( File.Exists( Path ) ) File.Delete( Path );
	}

	/// <summary>
	/// Everything that could change what the generator makes of the same source.
	/// </summary>
	static string ToolsId()
	{
		var assemblies = new[] { typeof( Program ), typeof( Sandbox.Generator.Processor ), typeof( Sandbox.CodeGeneratorAttribute ), typeof( CSharpCompilation ), typeof( object ) };
		return string.Join( ",", assemblies.Select( x => x.Assembly.ManifestModule.ModuleVersionId ) );
	}

	/// <summary>
	/// Hash of the file with its method, accessor and expression bodies cut out. Other files only
	/// ever see a file's declarations - signatures, attributes, docs, initializers - so if this is
	/// unchanged, so is everything their generated code was made from.
	/// </summary>
	static string DeclarationsHash( SyntaxTree tree, string text )
	{
		var sb = new StringBuilder( text.Length );
		var position = 0;

		foreach ( var body in tree.GetRoot().DescendantNodes( x => !IsBody( x ) ).Where( IsBody ) )
		{
			sb.Append( text, position, body.SpanStart - position );
			sb.Append( "{}" );
			position = body.Span.End;
		}

		sb.Append( text, position, text.Length - position );
		return Hash( sb.ToString() );
	}

	static string Hash( string text ) => Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( text ) ) );

	static bool IsBody( SyntaxNode node ) => node switch
	{
		BlockSyntax block => block.Parent is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax,
		ArrowExpressionClauseSyntax arrow => arrow.Parent is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or BasePropertyDeclarationSyntax,
		_ => false
	};
}
