using System.Globalization;
using System.IO;
using System.Text;

namespace Borealis.Gpu;

/// <summary>
/// The pipeline parts a device linked pipelines from (<see cref="PipelineParts"/>), kept beside its pipeline cache
/// (<c>pipeline-parts.txt</c>), each by the values that feed it: a shader part by its SPIR-V's hash, entry, specialization
/// constants, binding layouts' slots (and wireframe and depth clamp for pre-rasterisation); vertex input by its buffers,
/// attributes and topology; output by its formats, blending, samples and alpha to coverage. The next run makes them on the
/// builder threads as soon as they can be made: vertex input and output parts when the device is made, shader parts when a
/// module with that SPIR-V is made; in the order the last run first linked them, so a first frame's parts come first.
/// Lines are kept in that order: this run's first links first, then the rest as they were. The file outlives the driver's cache, which a driver update empties. A part is recorded
/// the first time a pipeline links it in a run (never per frame); a line that doesn't read, or whose part fails to make,
/// is dropped, as is one no run has used in <see cref="MaxAge"/> runs; nothing about the file can fail a load.
/// </summary>
internal sealed class PipelinePartManifest
{
	const string Header = "borealis pipeline parts 1";

	/// <summary>Lines kept at most: the ones used longest ago go first.</summary>
	public const int MaxEntries = 20000;

	/// <summary>Runs a line is kept for without a pipeline linking its part.</summary>
	public const int MaxAge = 64;

	/// <summary>A line read back: which part, the shader it's made from (zero for none) and the line, to drop it.</summary>
	public readonly record struct Entry( PipelineParts Kind, UInt128 Shader, string Line, int Rank );

	readonly string path;
	readonly object gate = new();
	readonly int run;

	// Each line (without its run) and the run that last used it; lines read back not yet made, by shader hash
	readonly Dictionary<string, int> lines = new( StringComparer.Ordinal );
	readonly Dictionary<UInt128, List<Entry>> byShader = new();
	readonly List<Entry> unshaded = new();
	readonly HashSet<UInt128> earlierShaders = new();
	bool dirty;

	// Lines in the order this run first linked their parts
	readonly List<string> linked = new();

	/// <summary>Lines read from the file, queued to be made, dropped, and recorded this run.</summary>
	public int Loaded, Queued, Dropped, Recorded;

	public PipelinePartManifest( string path )
	{
		this.path = path;
		run = 1;
		try
		{
			if ( !File.Exists( path ) ) return;
			using var reader = new StreamReader( path, Encoding.UTF8 );
			var header = reader.ReadLine();
			if ( header is null || !header.StartsWith( Header + "\t", StringComparison.Ordinal ) || !int.TryParse( header.AsSpan( Header.Length + 1 ), NumberStyles.None, CultureInfo.InvariantCulture, out var last ) ) return;
			run = last + 1;
			while ( reader.ReadLine() is { } text )
			{
				var tab = text.IndexOf( '\t' );
				if ( tab <= 0 || !int.TryParse( text.AsSpan( 0, tab ), NumberStyles.None, CultureInfo.InvariantCulture, out var used ) || used > last ) continue;
				var line = text[(tab + 1)..];
				if ( run - used > MaxAge || lines.Count >= MaxEntries || lines.ContainsKey( line ) || Describe( line, lines.Count ) is not { } entry )
				{
					// Unread or stale: written back without it
					dirty = true;
					continue;
				}

				lines[line] = used;
				if ( entry.Shader == 0 ) unshaded.Add( entry );
				else
				{
					if ( !byShader.TryGetValue( entry.Shader, out var list ) ) byShader[entry.Shader] = list = new();
					list.Add( entry );
					earlierShaders.Add( entry.Shader );
				}

				Loaded++;
			}
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or DecoderFallbackException )
		{
			// An unreadable file is an empty one
			lines.Clear();
			byShader.Clear();
			unshaded.Clear();
			earlierShaders.Clear();
			Loaded = 0;
			run = 1;
		}
	}

	/// <summary>The lines for parts made without a shader (vertex input, output), once.</summary>
	public List<Entry> TakeUnshaded()
	{
		lock ( gate )
		{
			var taken = new List<Entry>( unshaded );
			unshaded.Clear();
			Queued += taken.Count;
			return taken;
		}
	}

	/// <summary>The lines for parts made from a shader with this SPIR-V hash, once a run.</summary>
	public List<Entry> Take( UInt128 shader )
	{
		lock ( gate )
		{
			if ( !byShader.Remove( shader, out var taken ) ) return null;
			Queued += taken.Count;
			return taken;
		}
	}

	/// <summary>Whether an earlier run recorded parts made from a shader with this SPIR-V hash.</summary>
	public bool Has( UInt128 shader )
	{
		lock ( gate ) return earlierShaders.Contains( shader );
	}

	/// <summary>A pipeline linked a part described by <paramref name="line"/> (<see cref="Describe"/>'s form): kept for the next run.</summary>
	public void Use( string line, UInt128 shader )
	{
		lock ( gate )
		{
			if ( lines.TryGetValue( line, out var used ) )
			{
				if ( used == run ) return;
				lines[line] = run;
				linked.Add( line );
				dirty = true;
				return;
			}

			if ( lines.Count >= MaxEntries ) return;
			lines[line] = run;
			linked.Add( line );
			dirty = true;
			Recorded++;
		}
	}

	/// <summary>Forget a line: its part failed to make.</summary>
	public void Drop( string line )
	{
		lock ( gate )
		{
			if ( !lines.Remove( line ) ) return;
			linked.Remove( line );
			dirty = true;
			Dropped++;
		}
	}

	/// <summary>Write the file if anything changed since it was read or last written; false when nothing was written.</summary>
	public bool Save()
	{
		List<string> snapshot;
		lock ( gate )
		{
			if ( !dirty ) return false;
			snapshot = new List<string>( lines.Count + 1 ) { $"{Header}\t{run.ToString( CultureInfo.InvariantCulture )}" };
			foreach ( var line in linked ) snapshot.Add( $"{run.ToString( CultureInfo.InvariantCulture )}\t{line}" );
			foreach ( var (line, used) in lines )
			{
				if ( used != run ) snapshot.Add( $"{used.ToString( CultureInfo.InvariantCulture )}\t{line}" );
			}
			dirty = false;
		}

		var temporary = $"{path}.{Environment.ProcessId}.tmp";
		try
		{
			Directory.CreateDirectory( Path.GetDirectoryName( path ) ?? "." );
			File.WriteAllLines( temporary, snapshot, new UTF8Encoding( false ) );
			File.Move( temporary, path, overwrite: true );
			return true;
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			try { File.Delete( temporary ); }
			catch ( Exception d ) when ( d is IOException or UnauthorizedAccessException ) { }
			lock ( gate ) dirty = true;
			return false;
		}
	}

	//
	// Lines: kind, then what the part reads, tab-separated; every number invariant
	//

	static string N( int value ) => value.ToString( CultureInfo.InvariantCulture );

	/// <summary>The line for one part of <paramref name="info"/>, its shader's SPIR-V hash given, its sets' slots given.</summary>
	public static string Write( PipelineParts kind, in GraphicsPipelineInfo info, UInt128 shader, BindingSlot[][] sets )
	{
		var b = new StringBuilder( 256 );
		switch ( kind )
		{
			case PipelineParts.PreRasterization:
				b.Append( 'P' ).Append( '\t' ).Append( shader.ToString( "x32", CultureInfo.InvariantCulture ) ).Append( '\t' ).Append( info.VertexEntry ?? "main" ).Append( '\t' );
				Constants( b, info.VertexConstants );
				b.Append( '\t' );
				Sets( b, sets );
				b.Append( '\t' ).Append( info.Wireframe ? '1' : '0' ).Append( '\t' ).Append( info.DepthClamp ? '1' : '0' );
				break;

			case PipelineParts.FragmentShader:
				if ( !info.FragmentShader.IsValid ) return null;
				b.Append( 'F' ).Append( '\t' ).Append( shader.ToString( "x32", CultureInfo.InvariantCulture ) ).Append( '\t' ).Append( info.FragmentEntry ?? "main" ).Append( '\t' );
				Constants( b, info.FragmentConstants );
				b.Append( '\t' );
				Sets( b, sets );
				break;

			case PipelineParts.VertexInput:
				b.Append( 'V' ).Append( '\t' ).Append( N( (int)info.Topology ) ).Append( '\t' );
				Join( b, info.VertexBuffers, ( b, v ) => b.Append( N( v.Stride ) ).Append( '.' ).Append( N( (int)v.StepRate ) ) );
				b.Append( '\t' );
				Join( b, info.Attributes, ( b, a ) => b.Append( N( a.Location ) ).Append( '.' ).Append( N( a.Buffer ) ).Append( '.' ).Append( N( (int)a.Format ) ).Append( '.' ).Append( N( a.Offset ) ) );
				break;

			case PipelineParts.FragmentOutput:
				b.Append( 'O' ).Append( '\t' );
				var colors = info.ColorFormats ?? [];
				for ( int i = 0; i < colors.Length; i++ )
				{
					if ( i > 0 ) b.Append( ';' );
					var blend = info.Blend is { Length: > 0 } ? info.Blend[Math.Min( i, info.Blend.Length - 1 )] : BlendState.Opaque;
					b.Append( N( (int)colors[i] ) ).Append( '.' ).Append( blend.Enable ? '1' : '0' )
						.Append( '.' ).Append( N( (int)blend.SourceColor ) ).Append( '.' ).Append( N( (int)blend.DestinationColor ) ).Append( '.' ).Append( N( (int)blend.ColorOp ) )
						.Append( '.' ).Append( N( (int)blend.SourceAlpha ) ).Append( '.' ).Append( N( (int)blend.DestinationAlpha ) ).Append( '.' ).Append( N( (int)blend.AlphaOp ) )
						.Append( '.' ).Append( N( blend.WriteMask ) );
				}

				b.Append( '\t' ).Append( N( (int)info.DepthFormat ) ).Append( '\t' ).Append( N( Math.Max( info.Samples, 1 ) ) ).Append( '\t' ).Append( info.AlphaToCoverage ? '1' : '0' );
				break;

			default:
				return null;
		}

		return b.ToString();
	}

	static void Constants( StringBuilder b, SpecializationConstant[] constants )
	{
		if ( constants is not { Length: > 0 } )
		{
			b.Append( '-' );
			return;
		}

		Join( b, constants, ( b, c ) => b.Append( c.Id.ToString( CultureInfo.InvariantCulture ) ).Append( '=' ).Append( c.Value.ToString( CultureInfo.InvariantCulture ) ) );
	}

	static void Sets( StringBuilder b, BindingSlot[][] sets )
	{
		for ( int s = 0; s < sets.Length; s++ )
		{
			if ( s > 0 ) b.Append( '|' );
			Join( b, sets[s], ( b, slot ) => b.Append( N( slot.Binding ) ).Append( '.' ).Append( N( (int)slot.Type ) ).Append( '.' ).Append( N( (int)slot.Stages ) ).Append( '.' ).Append( N( slot.Count ) ) );
		}
	}

	static void Join<T>( StringBuilder b, T[] items, Action<StringBuilder, T> write )
	{
		for ( int i = 0; i < (items?.Length ?? 0); i++ )
		{
			if ( i > 0 ) b.Append( ';' );
			write( b, items[i] );
		}
	}

	/// <summary>What a line names, or null when it doesn't read.</summary>
	static Entry? Describe( string line, int rank )
	{
		if ( Read( line, out var kind, out var shader, out _, out _ ) ) return new Entry( kind, shader, line, rank );
		return null;
	}

	/// <summary>
	/// Read a line back into the parts of a pipeline info it describes (shader handles left unset) and its sets' slots;
	/// false when it doesn't read.
	/// </summary>
	public static bool Read( string line, out PipelineParts kind, out UInt128 shader, out GraphicsPipelineInfo info, out BindingSlot[][] sets )
	{
		kind = PipelineParts.None;
		shader = 0;
		info = default;
		sets = null;
		var p = line.Split( '\t' );
		switch ( p[0] )
		{
			case "P" when p.Length == 7:
				if ( !Hash( p[1], out shader ) || p[2].Length == 0 || !ReadConstants( p[3], out var vertexConstants ) || !ReadSets( p[4], out sets ) || !Flag( p[5], out var wireframe ) || !Flag( p[6], out var clamp ) ) return false;
				kind = PipelineParts.PreRasterization;
				info.VertexEntry = p[2];
				info.VertexConstants = vertexConstants;
				info.Wireframe = wireframe;
				info.DepthClamp = clamp;
				return true;

			case "F" when p.Length == 5:
				if ( !Hash( p[1], out shader ) || p[2].Length == 0 || !ReadConstants( p[3], out var fragmentConstants ) || !ReadSets( p[4], out sets ) ) return false;
				kind = PipelineParts.FragmentShader;
				info.FragmentEntry = p[2];
				info.FragmentConstants = fragmentConstants;
				return true;

			case "V" when p.Length == 4:
				{
					if ( !Int( p[1], out var topology ) || !Enum.IsDefined( (PrimitiveTopology)topology ) ) return false;
					if ( !List( p[2], 2, out var buffers ) || !List( p[3], 4, out var attributes ) ) return false;
					var vertexBuffers = new VertexBufferLayout[buffers.Count];
					for ( int i = 0; i < buffers.Count; i++ )
					{
						if ( !Enum.IsDefined( (VertexStepRate)buffers[i][1] ) || buffers[i][0] < 0 ) return false;
						vertexBuffers[i] = new VertexBufferLayout( buffers[i][0], (VertexStepRate)buffers[i][1] );
					}

					var vertexAttributes = new VertexAttribute[attributes.Count];
					for ( int i = 0; i < attributes.Count; i++ )
					{
						var a = attributes[i];
						if ( !Enum.IsDefined( (VertexFormat)a[2] ) || a[0] < 0 || a[1] < 0 || a[1] >= vertexBuffers.Length || a[3] < 0 ) return false;
						vertexAttributes[i] = new VertexAttribute( a[0], a[1], (VertexFormat)a[2], a[3] );
					}

					kind = PipelineParts.VertexInput;
					info.Topology = (PrimitiveTopology)topology;
					info.VertexBuffers = vertexBuffers;
					info.Attributes = vertexAttributes;
					return true;
				}

			case "O" when p.Length == 5:
				{
					if ( !List( p[1], 9, out var targets ) || !Int( p[2], out var depth ) || !Int( p[3], out var samples ) || !Flag( p[4], out var coverage ) ) return false;
					if ( !Enum.IsDefined( (Format)depth ) || samples is not (1 or 2 or 4 or 8) ) return false;
					var colors = new Format[targets.Count];
					var blends = new BlendState[targets.Count];
					for ( int i = 0; i < targets.Count; i++ )
					{
						var t = targets[i];
						if ( !Enum.IsDefined( (Format)t[0] ) || t[1] is not (0 or 1) ) return false;
						for ( int f = 2; f < 8; f++ )
						{
							if ( f is 4 or 7 ? !Enum.IsDefined( (BlendOp)t[f] ) : !Enum.IsDefined( (BlendFactor)t[f] ) ) return false;
						}

						colors[i] = (Format)t[0];
						blends[i] = new BlendState
						{
							Enable = t[1] == 1,
							SourceColor = (BlendFactor)t[2],
							DestinationColor = (BlendFactor)t[3],
							ColorOp = (BlendOp)t[4],
							SourceAlpha = (BlendFactor)t[5],
							DestinationAlpha = (BlendFactor)t[6],
							AlphaOp = (BlendOp)t[7],
							WriteMask = t[8],
						};
					}

					kind = PipelineParts.FragmentOutput;
					info.ColorFormats = colors;
					info.Blend = blends;
					info.DepthFormat = (Format)depth;
					info.Samples = samples;
					info.AlphaToCoverage = coverage;
					return true;
				}

			default:
				return false;
		}
	}

	static bool Int( string text, out int value ) => int.TryParse( text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value );

	static bool Flag( string text, out bool value )
	{
		value = text == "1";
		return text is "0" or "1";
	}

	static bool Hash( string text, out UInt128 value ) => UInt128.TryParse( text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value ) && text.Length == 32 && value != 0;

	static bool ReadConstants( string text, out SpecializationConstant[] constants )
	{
		constants = null;
		if ( text == "-" ) return true;
		var parts = text.Split( ';' );
		constants = new SpecializationConstant[parts.Length];
		for ( int i = 0; i < parts.Length; i++ )
		{
			var equals = parts[i].IndexOf( '=' );
			if ( equals <= 0 || !uint.TryParse( parts[i].AsSpan( 0, equals ), NumberStyles.None, CultureInfo.InvariantCulture, out var id ) || !uint.TryParse( parts[i].AsSpan( equals + 1 ), NumberStyles.None, CultureInfo.InvariantCulture, out var value ) ) return false;
			constants[i] = new SpecializationConstant( id, value );
		}

		return true;
	}

	static bool ReadSets( string text, out BindingSlot[][] sets )
	{
		var parts = text.Split( '|' );
		sets = new BindingSlot[parts.Length][];
		if ( parts.Length > ShaderAbi.BindlessSet ) return false;
		for ( int s = 0; s < parts.Length; s++ )
		{
			if ( !List( parts[s], 4, out var slots ) ) return false;
			sets[s] = new BindingSlot[slots.Count];
			for ( int i = 0; i < slots.Count; i++ )
			{
				var v = slots[i];
				if ( v[0] < 0 || !Enum.IsDefined( (BindingType)v[1] ) || v[3] < 0 ) return false;
				sets[s][i] = new BindingSlot( v[0], (BindingType)v[1], (ShaderStages)v[2], v[3] );
			}
		}

		return true;
	}

	/// <summary>A ';'-separated list of '.'-separated groups of <paramref name="size"/> integers; empty text is an empty list.</summary>
	static bool List( string text, int size, out List<int[]> items )
	{
		items = new List<int[]>();
		if ( text.Length == 0 ) return true;
		foreach ( var item in text.Split( ';' ) )
		{
			var fields = item.Split( '.' );
			if ( fields.Length != size ) return false;
			var values = new int[size];
			for ( int i = 0; i < size; i++ )
			{
				if ( !Int( fields[i], out values[i] ) ) return false;
			}

			items.Add( values );
		}

		return true;
	}
}
