using System.Text.RegularExpressions;

namespace Sandbox.ContentBuild;

/// <summary>
/// The engine's compiled asset types (game/bin/assettypes.txt): which source extensions compile, to what,
/// and how this builder treats each compiler.
/// </summary>
public sealed partial class AssetTypeTable
{
	readonly Dictionary<string, (AssetKind Kind, string Output)> types = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Textures, sounds, animgraphs, skeletons, generic data, manifests and standalone physics read only source files
	/// (an animgraph's subgraphs are inputs, the models it names are only references). Materials, models and animation
	/// clips can read other assets' compiled outputs and describe which. Shaders are built before content by their own
	/// compiler, and maps and their visibility by Hammer. The rest can't be built from a source file of their
	/// own: sound event and stack scripts and remap tables have no compiler, meshes and morphs refuse, and
	/// legacy animations, sequences and animation groups are only built inside a model compile. Their sources fail
	/// the build rather than be built on a guess.
	/// </summary>
	static AssetKind KindOf( string compiler ) => compiler switch
	{
		"CompileTexture" or "CompileSound" or "CompileAnimgraph" or "CompileAnimSkeleton" or "CompileGenericData" or "CompileResourceManifest" or "CompileVPhysXData" => AssetKind.Compiled,
		"CompileMaterial" or "CompileModel" or "CompileAnimClip" => AssetKind.Described,
		"CompileShader" or "CompileMap" or "CompileMapVisibility" => AssetKind.None,
		_ => AssetKind.Unsupported,
	};

	public AssetTypeTable( string assetTypes )
	{
		// Each type is a block: its extension, its compiler, and other source extensions it accepts
		foreach ( var block in File.ReadAllText( assetTypes ).Split( "_class" ) )
		{
			var compiler = Field( block, "m_CompilerIdentifier" );
			var extension = Field( block, "m_Ext" );
			if ( compiler is null || extension is null || Field( block, "m_bPreventDirectCompile" ) == "true" ) continue;

			var type = (KindOf( compiler ), $".{extension}_c");
			types.TryAdd( "." + extension, type );

			var additional = AdditionalExtensions().Match( block );
			foreach ( Match ext in QuotedValue().Matches( additional.Success ? additional.Groups[1].Value : "" ) )
				types.TryAdd( "." + ext.Groups[1].Value, type );
		}
	}

	/// <summary>
	/// A native compiler compiles sources with this extension, whatever this builder makes of it.
	/// </summary>
	public bool Contains( string sourceExtension ) => types.ContainsKey( sourceExtension );

	public AssetKind Classify( string sourceExtension, out string outputExtension )
	{
		var found = types.TryGetValue( sourceExtension, out var type );
		outputExtension = found ? type.Output : null;
		return found ? type.Kind : AssetKind.None;
	}

	static string Field( string block, string name )
	{
		var match = Regex.Match( block, name + @"\s*=\s*""?([\w.]+)""?" );
		return match.Success ? match.Groups[1].Value : null;
	}

	[GeneratedRegex( @"m_AdditionalExtensions\s*=\s*\[([^\]]*)\]" )]
	private static partial Regex AdditionalExtensions();

	[GeneratedRegex( @"""(\w+)""" )]
	private static partial Regex QuotedValue();
}
