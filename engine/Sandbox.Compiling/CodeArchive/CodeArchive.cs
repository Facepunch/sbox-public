using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Sandbox;

public class CodeArchive
{
	/// <summary>
	/// The name of the compiler
	/// </summary>
	public string CompilerName { get; set; }

	/// <summary>
	/// The compiler's configuration settings
	/// </summary>
	public Compiler.Configuration Configuration { get; set; }

	/// <summary>
	/// The syntax trees that should be compiled
	/// </summary>
	public List<SyntaxTree> SyntaxTrees { get; } = new();

	/// <summary>
	/// Hashes of source files, used for incremental compiles. Not serialized right now.
	/// </summary>
	public ConcurrentDictionary<string, ulong> FileHashMap { get; } = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Represents a file to send to the compiler along with all the code. This is usually
	/// something that the generator turns into code, such as a Razor file.
	/// </summary>
	public record AdditionalFile( string Text, string LocalPath );

	/// <summary>
	/// Additional files that the compiler/generator needs. This is going to be .razor files.
	/// </summary>
	public List<AdditionalFile> AdditionalFiles { get; } = new();

	/// <summary>
	/// Converts the syntax tree paths from physical paths to project local paths
	/// </summary>
	public Dictionary<string, string> FileMap { get; } = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// References that this compiler/generator needs to compile the code
	/// </summary>
	public HashSet<string> References { get; } = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The version of the code archive
	/// 1005 - Inital version
	/// 1006 - Razor updates. Add razor namespaces on older versions.
	/// 1007 - Razor changed to our own Microsoft.AspNetCore.Components assembly
	/// </summary>
	public long Version { get; set; }


	/// <summary>
	/// If true then we shouldn't automatically add @namespace to razor output, we shouldn't
	/// use the folder namespace structure.
	/// </summary>
	internal bool Version_UsesOldRazorNamespaces => Version < 1007;

	public CodeArchive()
	{
		Version = 1007;
	}

	public CodeArchive( byte[] data )
	{
		Deserialize( data );
	}

	/// <summary>
	/// Serialize to a byte array
	/// </summary>
	public byte[] Serialize()
	{
		using var bs = ByteStream.Create( 128 );

		bs.Write( "GMCA" ); // gmod code archive
		bs.Write( Version ); // version

		bs.Write( CompilerName );
		bs.Write( JsonSerializer.Serialize( Configuration ) );
		bs.Write( JsonSerializer.Serialize( References.Order( StringComparer.Ordinal ) ) );

		var trees = SyntaxTrees.ToList();
		trees.Sort( CompareSourceOrder );

		var fileMap = new Dictionary<string, string>();
		var sources = new List<AdditionalFile>( trees.Count );
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var tree in trees )
		{
			var localPath = SourcePath( tree );
			var path = tree.FilePath == Generator.Processor.CompilerExtraPath ? tree.FilePath : localPath;
			if ( !seen.Add( path ) )
			{
				// Two source roots can contain the same filename. Keep a distinct tree path for Roslyn.
				path = $"/.obj/sources/{sources.Count}/{localPath}";
			}

			if ( path != localPath )
				fileMap.Add( path, localPath );

			sources.Add( new( tree.GetText().ToString(), path ) );
		}

		var razor = AdditionalFiles.Select( file => file with { LocalPath = PortablePath( file.LocalPath ) } );
		bs.Write( JsonSerializer.Serialize( sources ) );
		bs.Write( JsonSerializer.Serialize( razor.OrderBy( file => file.LocalPath, StringComparer.Ordinal ).ThenBy( file => file.Text, StringComparer.Ordinal ) ) );
		bs.Write( JsonSerializer.Serialize( fileMap ) );

		using var compressedBs = bs.Compress();
		var bytes = compressedBs.ToArray();
		bytes[9] = 255; // Gzip's OS field is platform-specific. Use the portable "unknown" value.
		return bytes;
	}

	// Partial type initializers depend on tree order. Use the same order for local builds and archives.
	internal int CompareSourceOrder( SyntaxTree a, SyntaxTree b )
	{
		var order = string.CompareOrdinal( SourcePath( a ), SourcePath( b ) );
		return order != 0 ? order : string.CompareOrdinal( a.GetText().ToString(), b.GetText().ToString() );
	}

	string SourcePath( SyntaxTree tree ) => PortablePath( FileMap.GetValueOrDefault( tree.FilePath, tree.FilePath ) );

	static string PortablePath( string path )
	{
		path = path.Replace( '\\', '/' );
		if ( path != Generator.Processor.CompilerExtraPath && (path.StartsWith( '/' ) || path is [_, ':', ..]) )
			throw new InvalidOperationException( $"Missing project-relative archive path for '{path}'" );

		return path;
	}

	/// <summary>
	/// Deserialize from a byte array
	/// </summary>
	internal void Deserialize( byte[] data )
	{
		using var compressed = ByteStream.CreateReader( data );
		using var bs = compressed.Decompress();

		var ident = bs.Read<string>();
		if ( ident != "GMCA" ) throw new Exception( "Invalid archive" );

		Version = bs.Read<long>();
		if ( Version < 1005 ) throw new Exception( "Invalid archive version" );
		if ( Version > 1007 ) throw new Exception( "Invalid archive version" );

		CompilerName = bs.Read<string>();
		Configuration = JsonSerializer.Deserialize<Compiler.Configuration>( bs.Read<string>() );

		// parse the syntaxtree's using the same options as the creator
		var parseOptions = Configuration.GetParseOptions();

		References.Clear();
		var references = JsonSerializer.Deserialize<List<string>>( bs.Read<string>() );
		foreach ( string reference in references )
		{
			// 7th June 2025 - We removed Sandbox.Game, which held the scene and ui systems. They're in engine now.
			if ( reference.Equals( "Sandbox.Game", StringComparison.OrdinalIgnoreCase ) ) continue;

			References.Add( reference );
		}

		// 14th Nov 2025 - Older archives didn't reference Microsoft.AspNetCore.Components, but they should now.
		if ( Version < 1007 )
		{
			References.Add( "Microsoft.AspNetCore.Components" );
		}

		SyntaxTrees.Clear();
		var syntaxTrees = JsonSerializer.Deserialize<List<AdditionalFile>>( bs.Read<string>() );
		foreach ( var file in syntaxTrees )
		{
			var snytaxTree = CSharpSyntaxTree.ParseText( file.Text, path: file.LocalPath, encoding: System.Text.Encoding.UTF8, options: parseOptions );
			SyntaxTrees.Add( snytaxTree );
		}

		AdditionalFiles.Clear();
		var additionalFiles = JsonSerializer.Deserialize<List<AdditionalFile>>( bs.Read<string>() );
		foreach ( var file in additionalFiles )
		{
			AdditionalFiles.Add( file );
		}

		var fileMap = JsonSerializer.Deserialize<Dictionary<string, string>>( bs.Read<string>() );
		FileMap.Clear();
		foreach ( (string k, string v) in fileMap )
		{
			FileMap[k] = v;
		}

		if ( Version < 1006 )
		{
			// In archives previous to 1006 we didn't define a global using for global using Microsoft.AspNetCore.Components
			// and after that we moved the razor stuff into that namespace to be compatible with visual studio.
			var tree = CSharpSyntaxTree.ParseText( "global using Microsoft.AspNetCore.Components; \nglobal using Microsoft.AspNetCore.Components.Rendering;\n", path: "__gen_RazorNamespace.cs", encoding: System.Text.Encoding.UTF8, options: parseOptions );
			SyntaxTrees.Add( tree );
		}
	}
}
