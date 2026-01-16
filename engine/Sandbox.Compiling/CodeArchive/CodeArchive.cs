using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
		bs.Write( JsonSerializer.Serialize( References ) );
		bs.Write( JsonSerializer.Serialize( SyntaxTrees.Select( x => new AdditionalFile( x.GetText().ToString(), x.FilePath ) ) ) );
		bs.Write( JsonSerializer.Serialize( AdditionalFiles ) );
		bs.Write( JsonSerializer.Serialize( FileMap ) );

		using var compressedBs = bs.Compress();
		return compressedBs.ToArray();
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
