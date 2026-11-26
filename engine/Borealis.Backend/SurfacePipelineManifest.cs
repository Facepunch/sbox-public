using Borealis.Gpu;
using Sandbox.ShaderCompiler;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PassObjects = Borealis.Backend.SurfaceProgram.PassObjects;

namespace Borealis.Backend;

/// <summary>
/// The surface pipelines a device made, kept beside its pipeline cache (<c>surface-pipelines.txt</c>), so the next run
/// makes them on workers as each surface program compiles instead of in its first frames (<see cref="SurfaceProgram.Prewarm"/>).
/// A pipeline is recorded the first time a draw makes it (a pipeline cache miss of its program, never per frame), by what
/// asked for it: the surface's file, the pass by name, the vertex layout, the targets, the specialization constants (each
/// by its slot's name, so another process's numbering doesn't matter), the render state and the pass flags. The file
/// outlives the driver's pipeline cache, which a driver or shader update empties. A line that doesn't read, names a pass,
/// constant or format that no longer exists, or fails to make is dropped; nothing about the file can fail a load.
/// </summary>
internal sealed class SurfacePipelineManifest
{
	const string Header = "borealis surface pipelines 1";

	/// <summary>Lines kept at most.</summary>
	public const int MaxEntries = 20000;

	/// <summary>Beside a pipeline cache file, or null without one.</summary>
	public static string PathBeside( string pipelineCachePath ) => string.IsNullOrEmpty( pipelineCachePath ) ? null : Path.Combine( Path.GetDirectoryName( pipelineCachePath ) ?? "", "surface-pipelines.txt" );

	/// <summary>What a pipeline was asked for with (<see cref="SurfaceProgram.Pipeline"/>'s arguments, unspecialised).</summary>
	public readonly record struct Request( PassObjects Pass, VertexLayout Layout, Format Color, Format Depth, int Samples, SpecializationConstant[] VertexConstants, SpecializationConstant[] PixelConstants, ulong ConstantsKey, SurfaceRenderState State, bool Ids, bool OpaqueFade, bool DepthEqual, bool Normals, OverlayStencil Stencil = OverlayStencil.None );

	readonly string path;
	readonly object gate = new();

	/// <summary>Every line kept, in order, and the lines read from the file not yet replayed, by surface file.</summary>
	readonly List<string> lines = new();
	readonly HashSet<string> known = new( StringComparer.Ordinal );
	readonly Dictionary<string, List<string>> pending = new( StringComparer.OrdinalIgnoreCase );
	bool dirty;

	/// <summary>Lines read from the file, replayed into programs, dropped, and recorded this run.</summary>
	public int Loaded, Replayed, Dropped, Recorded;

	public SurfacePipelineManifest( string path )
	{
		this.path = path;
		if ( path is null ) return;
		try
		{
			if ( !File.Exists( path ) ) return;
			using var reader = new StreamReader( path, Encoding.UTF8 );
			if ( reader.ReadLine() != Header ) return;
			while ( reader.ReadLine() is { } line )
			{
				var tab = line.IndexOf( '\t' );
				if ( tab <= 0 || lines.Count >= MaxEntries || !known.Add( line ) ) continue;
				lines.Add( line );
				var file = line[..tab];
				if ( !pending.TryGetValue( file, out var list ) ) pending[file] = list = new();
				list.Add( line );
				Loaded++;
			}
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or DecoderFallbackException )
		{
			// An unreadable file is an empty one
			lines.Clear();
			known.Clear();
			pending.Clear();
			Loaded = 0;
		}
	}

	/// <summary>Record a pipeline a draw made for <paramref name="file"/>'s program; a line already kept is not added again.</summary>
	public void Record( string file, in Request request )
	{
		if ( path is null || string.IsNullOrEmpty( file ) ) return;
		var line = Write( file, request );
		if ( line is null ) return;
		lock ( gate )
		{
			if ( lines.Count >= MaxEntries || !known.Add( line ) ) return;
			lines.Add( line );
			dirty = true;
			Recorded++;
		}
	}

	/// <summary>
	/// The requests kept for <paramref name="file"/>, made for <paramref name="program"/> (just compiled), once: lines naming
	/// what the program no longer has are dropped.
	/// </summary>
	public List<Request> Take( string file, SurfaceProgram program )
	{
		List<string> taken;
		lock ( gate )
		{
			if ( file is null || !pending.Remove( file, out taken ) ) return null;
		}

		var requests = new List<Request>( taken.Count );
		foreach ( var line in taken )
		{
			if ( TryRead( line, program, out var request ) )
			{
				requests.Add( request );
				continue;
			}

			Drop( line );
		}

		lock ( gate ) Replayed += requests.Count;
		return requests;
	}

	/// <summary>Forget a line: it no longer reads, or its pipeline failed to make.</summary>
	public void Drop( in Request request, string file )
	{
		if ( Write( file, request ) is { } line ) Drop( line );
	}

	void Drop( string line )
	{
		lock ( gate )
		{
			if ( !known.Remove( line ) ) return;
			lines.Remove( line );
			dirty = true;
			Dropped++;
		}
	}

	/// <summary>Write the file if anything changed since it was read or last written; false when nothing was written.</summary>
	public bool Save()
	{
		if ( path is null ) return false;
		string[] snapshot;
		lock ( gate )
		{
			if ( !dirty ) return false;
			snapshot = [.. lines];
			dirty = false;
		}

		try
		{
			Directory.CreateDirectory( Path.GetDirectoryName( path ) ?? "." );
			var temporary = path + ".tmp";
			File.WriteAllLines( temporary, [Header, .. snapshot], new UTF8Encoding( false ) );
			File.Move( temporary, path, overwrite: true );
			return true;
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			lock ( gate ) dirty = true;
			return false;
		}
	}

	// One line: file, pass, layout, colour, depth, samples, constants key, vertex and pixel constants, render state, flags, optional overlay stencil
	static string Write( string file, in Request r )
	{
		if ( r.Pass?.Pass is not { } pass || r.Layout is null ) return null;
		var s = r.State;
		var fields = string.Join( ";", r.Layout.Fields.Select( f => $"{(int)f.Semantic},{f.Index},{(int)f.Format},{f.Offset}" ) );
		var line = string.Join( '\t',
			file, pass.Name, r.Layout.Stride.ToString( CultureInfo.InvariantCulture ), r.Layout.VertexCache ? "1" : "0", fields,
			((int)r.Color).ToString( CultureInfo.InvariantCulture ), ((int)r.Depth).ToString( CultureInfo.InvariantCulture ), r.Samples.ToString( CultureInfo.InvariantCulture ),
			r.ConstantsKey.ToString( CultureInfo.InvariantCulture ),
			Constants( r.VertexConstants, pass.Vertex ), Constants( r.PixelConstants, pass.Pixel ),
			((int)s.Blend).ToString( CultureInfo.InvariantCulture ), Flag( s.DepthWrite ), Flag( s.DepthTest ),
			s.DepthBias.ToString( "R", CultureInfo.InvariantCulture ), s.SlopeDepthBias.ToString( "R", CultureInfo.InvariantCulture ),
			Flag( s.TwoSided ), Flag( s.AlphaTest ), Flag( s.Translucent ),
			Flag( r.Ids ), Flag( r.OpaqueFade ), Flag( r.DepthEqual ), Flag( r.Normals ) );
		return r.Stencil == OverlayStencil.None ? line : line + "\t" + ((int)r.Stencil).ToString( CultureInfo.InvariantCulture );
	}

	static string Flag( bool value ) => value ? "1" : "0";

	// A stage's constants: each by its slot's name where the stage has one, else by id (the backend's own)
	static string Constants( SpecializationConstant[] constants, CompiledStage stage )
	{
		if ( constants is null ) return "-";
		return string.Join( ",", constants.Select( c =>
		{
			var slot = stage?.Specializations.FirstOrDefault( x => x.Id == c.Id );
			return slot is not null ? $"n{slot.Name}={c.Value}" : $"i{c.Id}={c.Value}";
		} ) );
	}

	static bool TryRead( string line, SurfaceProgram program, out Request request )
	{
		request = default;
		var p = line.Split( '\t' );
		if ( p.Length is not (23 or 24) ) return false;
		if ( program.Pass( p[1] ) is not { } pass ) return false;
		if ( !Int( p[2], out var stride ) || !Int( p[5], out var color ) || !Int( p[6], out var depth ) || !Int( p[7], out var samples ) ) return false;
		if ( !ulong.TryParse( p[8], NumberStyles.None, CultureInfo.InvariantCulture, out var constantsKey ) ) return false;
		if ( !Enum.IsDefined( (Format)color ) || !Enum.IsDefined( (Format)depth ) || samples is not (1 or 2 or 4 or 8) ) return false;
		var stencil = OverlayStencil.None;
		if ( p.Length == 24 )
		{
			if ( !Int( p[23], out var value ) || (OverlayStencil)value is not (OverlayStencil.Write or OverlayStencil.Test) || !((Format)depth).HasStencil() ) return false;
			stencil = (OverlayStencil)value;
		}

		var fields = new List<VertexLayout.Field>();
		foreach ( var text in p[4].Split( ';', StringSplitOptions.RemoveEmptyEntries ) )
		{
			var f = text.Split( ',' );
			if ( f.Length != 4 || !Int( f[0], out var semantic ) || !Int( f[1], out var index ) || !Int( f[2], out var format ) || !Int( f[3], out var offset ) ) return false;
			if ( !Enum.IsDefined( (Borealis.Source2.Shaders.VertexSemantic)semantic ) || !Enum.IsDefined( (VertexFormat)format ) ) return false;
			fields.Add( new VertexLayout.Field( (Borealis.Source2.Shaders.VertexSemantic)semantic, index, (VertexFormat)format, offset ) );
		}

		if ( !ReadConstants( p[9], pass.Pass.Vertex, out var vertexConstants ) || !ReadConstants( p[10], pass.Pass.Pixel, out var pixelConstants ) ) return false;
		if ( !Int( p[11], out var blend ) || !Enum.IsDefined( (SurfaceBlend)blend ) ) return false;
		if ( !float.TryParse( p[14], NumberStyles.Float, CultureInfo.InvariantCulture, out var bias ) || !float.TryParse( p[15], NumberStyles.Float, CultureInfo.InvariantCulture, out var slopeBias ) ) return false;
		bool[] flags = new bool[9];
		var at = new[] { 12, 13, 16, 17, 18, 19, 20, 21, 22 };
		for ( int i = 0; i < at.Length; i++ )
		{
			if ( p[at[i]] is not ("0" or "1") ) return false;
			flags[i] = p[at[i]] == "1";
		}

		var state = new SurfaceRenderState( (SurfaceBlend)blend, flags[0], flags[1], bias, slopeBias, flags[2], flags[3], flags[4] );
		request = new Request( pass, new VertexLayout( stride, [.. fields], p[3] == "1" ), (Format)color, (Format)depth, samples, vertexConstants, pixelConstants, constantsKey, state, flags[5], flags[6], flags[7], flags[8], stencil );
		return true;
	}

	static bool ReadConstants( string text, CompiledStage stage, out SpecializationConstant[] constants )
	{
		constants = null;
		if ( text == "-" ) return true;
		var parts = text.Split( ',', StringSplitOptions.RemoveEmptyEntries );
		constants = new SpecializationConstant[parts.Length];
		for ( int i = 0; i < parts.Length; i++ )
		{
			var part = parts[i];
			var equals = part.IndexOf( '=' );
			if ( part.Length < 3 || equals < 2 || !uint.TryParse( part.AsSpan( equals + 1 ), NumberStyles.None, CultureInfo.InvariantCulture, out var value ) ) return false;
			var name = part[1..equals];
			if ( part[0] == 'n' )
			{
				// Renumbered by another process's compile: found by name
				var slot = stage?.Specializations.FirstOrDefault( x => x.Name == name );
				if ( slot is null ) return false;
				constants[i] = new SpecializationConstant( (uint)slot.Id, value );
			}
			else if ( part[0] == 'i' && uint.TryParse( name, NumberStyles.None, CultureInfo.InvariantCulture, out var id ) ) constants[i] = new SpecializationConstant( id, value );
			else return false;
		}

		return true;
	}

	static bool Int( string text, out int value ) => int.TryParse( text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value );
}
