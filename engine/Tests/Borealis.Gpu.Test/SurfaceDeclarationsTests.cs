using Borealis.Source2;
using Borealis.Source2.Shaders;
using Sandbox.ShaderCompiler;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BorealisGpuTests;

/// <summary>
/// What the material compiler, the material editor and the feature rules read of each surface shader, and what each core
/// <c>.vmat</c> of them compiles to, against snapshots (<c>Content/surface-declarations</c>) captured from the hand-written
/// <c>MATERIAL_INPUTS</c> blocks the surfaces' attributes replaced. A difference is a material that compiles, opens or
/// converts differently. <c>BOREALIS_WRITE_SURFACE_SNAPSHOTS=1</c> writes them again. Compiles nothing; no device.
/// </summary>
[TestClass]
public partial class SurfaceDeclarationsTests
{
	/// <summary>The surfaces whose material inputs are generated, by module path under a content root's <c>shaders</c>.</summary>
	public static readonly string[] Surfaces = ["standard", "glass", "blendable", "vertex_color", "static_overlay", "foliage", "fur", "skin", "terrain_preview", "water_simple", "citizen_eye"];

	static ContentFiles Content => ContentFiles.FindGame();

	static string ModulePath( string name )
	{
		foreach ( var root in Content.Roots )
		{
			var path = Path.Combine( root, "shaders", name + ".slang" );
			if ( File.Exists( path ) ) return path;
		}

		Assert.Inconclusive( $"No {name}.slang" );
		return null;
	}

	static string CoreShaders => Path.Combine( Content.Roots.First( r => File.Exists( Path.Combine( r, "shaders", "sbox", "pass.slangh" ) ) ), "shaders" );

	static PassModule Module( string name )
	{
		var path = ModulePath( name );
		return PassModule.Read( path, [Path.GetDirectoryName( path ), CoreShaders] );
	}

	static string SnapshotFolder => Path.Combine( Fixtures.CompiledShaderRoot, "surface-declarations" );

	/// <summary>Each surface's inputs, editor description and rules in every feature state the snapshot holds.</summary>
	[TestMethod]
	public void SurfacesDeclareWhatTheirBlocksDid()
	{
		var failures = new List<string>();
		foreach ( var name in Surfaces ) Check( name, Describe( Module( name ) ), failures );
		if ( failures.Count > 0 ) Assert.Fail( string.Join( "\n", failures ) );
	}

	/// <summary>Every <c>.vmat</c> under the content roots whose shader is one of the surfaces compiles as it did.</summary>
	[TestMethod]
	public void MaterialsCompileAsTheyDid()
	{
		var failures = new List<string>();
		var modules = Surfaces.ToDictionary( s => s, Module );
		var text = new StringBuilder();
		foreach ( var root in Content.Roots )
		{
			foreach ( var file in Directory.EnumerateFiles( root, "*.vmat", SearchOption.AllDirectories ).Order( StringComparer.Ordinal ) )
			{
				var material = VmatSource.Parse( File.ReadAllText( file ) );
				if ( MaterialCompile.ShaderName( material.Shader ) is not { } shader ) continue;
				var resolved = ShaderAliases.Resolve( shader );
				var surface = Path.GetFileNameWithoutExtension( resolved );
				if ( !modules.TryGetValue( surface, out var module ) || !resolved.Equals( "shaders/" + surface + ".shader", StringComparison.OrdinalIgnoreCase ) ) continue;

				var relative = Path.GetRelativePath( root, file ).Replace( '\\', '/' );
				var inputs = MaterialInputs.Read( module, material.Features );
				var compiled = MaterialCompile.Compile( material, relative, resolved, inputs, f => Content.Roots.Any( r => File.Exists( Path.Combine( r, f ) ) ), _ => (16, 16) );
				text.Append( "== " ).Append( relative ).Append( '\n' ).Append( compiled.Data );
				foreach ( var made in compiled.Generated ) text.Append( "-- " ).Append( made.Path ).Append( '\n' ).Append( made.Definition );
				text.Append( "-- signature\n" ).Append( compiled.VertexSignature ).Append( '\n' );
			}
		}

		Check( "materials", text.ToString(), failures );
		if ( failures.Count > 0 ) Assert.Fail( string.Join( "\n", failures ) );
	}

	/// <summary>
	/// Compare <paramref name="actual"/>, in sections that start with <c>== key</c>, with the snapshot: a hash per section, so
	/// the files stay small. <c>BOREALIS_WRITE_SURFACE_SNAPSHOTS=1</c> writes it; <c>BOREALIS_SURFACE_SNAPSHOT_TEXT=folder</c>
	/// also writes the whole text there, to diff a section that changed.
	/// </summary>
	static void Check( string name, string actual, List<string> failures )
	{
		var path = Path.Combine( SnapshotFolder, name + ".txt" );
		actual = actual.Replace( "\r\n", "\n" );
		if ( Environment.GetEnvironmentVariable( "BOREALIS_SURFACE_SNAPSHOT_TEXT" ) is { Length: > 0 } folder )
		{
			Directory.CreateDirectory( folder );
			File.WriteAllText( Path.Combine( folder, name + ".txt" ), actual );
		}

		var sections = new List<(string Key, string Hash)>();
		foreach ( var section in ("\n" + actual).Split( "\n== ", StringSplitOptions.RemoveEmptyEntries ) )
		{
			var newline = section.IndexOf( '\n' );
			var key = newline < 0 ? section : section[..newline];
			sections.Add( (key, Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( Encoding.UTF8.GetBytes( section ) ) )[..16]) );
		}

		if ( Environment.GetEnvironmentVariable( "BOREALIS_WRITE_SURFACE_SNAPSHOTS" ) == "1" )
		{
			Directory.CreateDirectory( SnapshotFolder );
			File.WriteAllText( path, string.Concat( sections.Select( s => $"{s.Key}\t{s.Hash}\n" ) ) );
			return;
		}

		if ( !File.Exists( path ) )
		{
			failures.Add( $"{name}: no snapshot" );
			return;
		}

		var expected = File.ReadAllLines( path ).Where( l => l.Length > 0 ).Select( l => l.Split( '\t' ) ).ToDictionary( l => l[0], l => l[1] );
		var changed = sections.Where( s => !expected.TryGetValue( s.Key, out var h ) || h != s.Hash ).Select( s => s.Key ).ToList();
		var missing = expected.Keys.Except( sections.Select( s => s.Key ) ).ToList();
		if ( changed.Count > 0 ) failures.Add( $"{name}: {changed.Count} changed, first {string.Join( "; ", changed.Take( 5 ) )}" );
		if ( missing.Count > 0 ) failures.Add( $"{name}: {missing.Count} missing, first {string.Join( "; ", missing.Take( 5 ) )}" );
	}

	/// <summary>
	/// What every reader makes of a module: its rules for each shader it stands for, then per feature state (none, each
	/// value of each feature alone, each pair of non-zero values) the material inputs and the editor's description.
	/// </summary>
	public static string Describe( PassModule module )
	{
		var text = new StringBuilder();
		for ( int shader = 0; shader < 3; shader++ )
		{
			text.Append( "== rules " ).Append( shader ).Append( '\n' );
			foreach ( var rule in FeatureRules.Read( module, shader ).Rules ) text.Append( "  " ).Append( Canonical( rule.Text ) ).Append( '\n' );
		}

		var features = new List<(string Name, int Min, int Max)>();
		foreach ( Match m in FeatureDeclaration().Matches( MaterialInputs.Read( module, new Dictionary<string, int>() ).FeatureLines ) )
			features.Add( (m.Groups["name"].Value, int.Parse( m.Groups["min"].Value, CultureInfo.InvariantCulture ), int.Parse( m.Groups["max"].Value, CultureInfo.InvariantCulture )) );

		var states = new List<Dictionary<string, int>> { new() };
		var values = features.SelectMany( f => Enumerable.Range( f.Min, f.Max - f.Min + 1 ).Where( v => v != 0 ).Select( v => (f.Name, Value: v) ) ).ToList();
		foreach ( var v in values ) states.Add( new() { [v.Name] = v.Value } );
		for ( int i = 0; i < values.Count; i++ )
			for ( int j = i + 1; j < values.Count; j++ )
				if ( values[i].Name != values[j].Name ) states.Add( new() { [values[i].Name] = values[i].Value, [values[j].Name] = values[j].Value } );

		foreach ( var state in states )
		{
			text.Append( "== " ).Append( state.Count == 0 ? "(none)" : string.Join( " ", state.Select( s => $"{s.Key}={s.Value}" ) ) ).Append( '\n' );
			var inputs = MaterialInputs.Read( module, state );
			// What's made counts (the made textures and their inputs); a texture declared but not taken is never made or shown
			var made = inputs.Textures.SelectMany( t => t.Channels ).SelectMany( c => c.Inputs ).ToHashSet( StringComparer.Ordinal );
			foreach ( var input in inputs.Inputs.Values.Where( i => made.Contains( i.Name ) ).OrderBy( i => i.Name, StringComparer.Ordinal ) ) text.Append( "input " ).Append( input.Name ).Append( ' ' ).Append( input.Type ).Append( input.Srgb ? " srgb " : " linear " ).Append( input.MinBits ).Append( " \"" ).Append( input.Processor ).Append( "\" " ).Append( input.FileEnding ).Append( " \"" ).Append( input.Group ).Append( "\" " ).Append( Floats( input.Default ) ).Append( ' ' ).Append( input.DefaultFile ).Append( '\n' );
			foreach ( var t in inputs.Textures ) text.Append( "texture " ).Append( Texture( t ) ).Append( '\n' );
			foreach ( var t in inputs.ShownTextures ) text.Append( "shown " ).Append( t.Name ).Append( '\n' );
			foreach ( var p in inputs.Parameters.OrderBy( p => p.Name, StringComparer.Ordinal ) ) text.Append( "parameter " ).Append( p.Type ).Append( ' ' ).Append( p.Name ).Append( '\n' );
			foreach ( var (name, kind, value) in inputs.Attributes ) text.Append( inputs.ExpressionAttributes.Contains( name ) ? "expression " : "attribute " ).Append( kind ).Append( ' ' ).Append( name ).Append( ' ' ).Append( Floats( value ) ).Append( '\n' );
			text.Append( "representative " ).Append( inputs.RepresentativeTexture ).Append( '\n' );
			text.Append( "version " ).Append( inputs.Version ).Append( '\n' );
			foreach ( var v in inputs.VertexInputs ) text.Append( "vertex " ).Append( v.Name ).Append( ' ' ).Append( v.Semantic ).Append( ' ' ).Append( v.D3DSemanticName ).Append( v.D3DSemanticIndex ).Append( '\n' );
			text.Append( "-- editor\n" ).Append( Editor( MaterialEditorDescription.Describe( module, state ) ) );
		}

		return text.ToString();
	}

	/// <summary>
	/// The editor's description with its feature lines' whitespace outside quotes taken out: the editor parses them
	/// (<c>CVfx::CreateFromFeatureText</c>), so <c>0..2( 0 ="A" )</c> and <c>0..2( 0="A" )</c> are the same lines.
	/// </summary>
	static string Editor( string description ) => string.Join( "\n", description.Split( '\n' ).Select( l => l.StartsWith( "featurelines\t", StringComparison.Ordinal ) ? Canonical( l ) : l ) );

	/// <summary>A line without whitespace outside quotes.</summary>
	static string Canonical( string line )
	{
		var result = new StringBuilder( line.Length );
		var quoted = false;
		foreach ( var c in line )
		{
			if ( c == '"' ) quoted = !quoted;
			if ( quoted || !char.IsWhiteSpace( c ) || c == '\t' ) result.Append( c );
		}

		return result.ToString();
	}
	static string Texture( MaterialInputs.Texture t ) => $"{t.Name} {t.Type} {t.Format} {(t.SrgbRead ? "srgb" : "linear")} " + string.Join( " ", t.Channels.Select( c => $"{c.Channels}={c.Algorithm}({string.Join( ",", c.Inputs )}){(c.Srgb ? "srgb" : "")}" ) );

	static string Floats( float[] values ) => values is null ? "-" : string.Join( ",", values.Select( v => v.ToString( "R", CultureInfo.InvariantCulture ) ) );

	[GeneratedRegex( @"\bFeature\s*\(\s*(?<name>F_\w+)\s*,\s*(?<min>-?\d+)\s*\.\.\s*(?<max>-?\d+)" )]
	private static partial Regex FeatureDeclaration();
}
