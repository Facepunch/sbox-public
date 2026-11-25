using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Borealis.Backend;

/// <summary>
/// The values a <see cref="ShaderDrawer"/> draw binds by name, as the engine's render attributes hold them: constants
/// as floats (a matrix as 16, in the engine's memory order: M11, M12 ... M44), structured buffers, textures and
/// constant buffers by attribute name, and dynamic combos by combo name. Names compare ignoring case, as the engine's
/// attribute tokens do.
/// </summary>
public sealed class ShaderAttributes
{
	// Values by attribute id (AttributeIds): a slot holds a value while its stamp is the current generation, so clearing is
	// moving the generation on. The ids set this generation, in order, for enumerating
	object[] slots = [];
	int[] stamps = [];
	int generation = 1;
	readonly List<int> set = new();
	readonly Dictionary<int, int> combos = new();

	// Arrays CopyValuesFrom owns per id, reused by the next copy
	object[] copies = [];
	int revision;

	// Bumped on any change; external: a caller's own array is held and may change in place unseen
	int version;

	// How many of those changes were a combo's, which a draw's values don't read (ValuesVersion)
	int comboChanges;
	bool external;

	// Values and combos that show through where this set has none of its own (Under)
	ShaderAttributes under;

	/// <summary>Constants: a number, a vector or a matrix, as floats.</summary>
	public void Set( string name, params float[] value )
	{
		external = true;
		version++;
		Put( AttributeIds.Of( name ), value );
	}

	/// <summary>
	/// A single number: kept in a one-float array this set owns for the name and reuses, so setting numbers every draw
	/// allocates nothing. The array holds the latest value set; read it before setting the name again.
	/// </summary>
	public void Set( string name, float value ) => SetById( AttributeIds.Of( name ), value );

	internal void SetById( int id, float value )
	{
		var array = Owned( id, 1 );
		if ( array[0] == value ) return;
		array[0] = value;
		version++;
	}

	/// <summary>A two-component vector, kept as <see cref="Set(string, float)"/> keeps a number.</summary>
	public void Set( string name, float x, float y )
	{
		var array = Owned( AttributeIds.Of( name ), 2 );
		if ( array[0] == x && array[1] == y ) return;
		(array[0], array[1]) = (x, y);
		version++;
	}

	/// <summary>A three-component vector, kept as <see cref="Set(string, float)"/> keeps a number.</summary>
	public void Set( string name, float x, float y, float z )
	{
		var array = Owned( AttributeIds.Of( name ), 3 );
		if ( array[0] == x && array[1] == y && array[2] == z ) return;
		(array[0], array[1], array[2]) = (x, y, z);
		version++;
	}

	/// <summary>A four-component vector, kept as <see cref="Set(string, float)"/> keeps a number.</summary>
	public void Set( string name, float x, float y, float z, float w )
	{
		var array = Owned( AttributeIds.Of( name ), 4 );
		if ( array[0] == x && array[1] == y && array[2] == z && array[3] == w ) return;
		(array[0], array[1], array[2], array[3]) = (x, y, z, w);
		version++;
	}

	// Arrays of 1-4 floats this set owns, by component count and attribute id, reused for every set of the name
	readonly float[][][] owned = [[], [], [], []];

	bool Owns( int id, float[] array ) => owned[array.Length - 1] is var arrays && id < arrays.Length && ReferenceEquals( arrays[id], array );

	/// <summary>One to four floats under an <see cref="AttributeIds"/> id, kept as the vector setters keep them.</summary>
	internal void SetFloatsById( int id, ReadOnlySpan<float> values )
	{
		if ( values.Length is < 1 or > 4 ) throw new ArgumentOutOfRangeException( nameof( values ), "One to four floats" );
		var array = Owned( id, values.Length );
		if ( array.AsSpan().SequenceEqual( values ) ) return;
		values.CopyTo( array );
		version++;
	}

	float[] Owned( int id, int components )
	{
		ref var arrays = ref owned[components - 1];
		if ( id >= arrays.Length ) Array.Resize( ref arrays, Math.Max( Math.Max( arrays.Length * 2, 64 ), AttributeIds.Count ) );
		var array = arrays[id] ??= new float[components];
		Put( id, array );
		return array;
	}

	/// <summary>A structured or byte-address buffer.</summary>
	public void Set( string name, BufferHandle buffer ) => Put( AttributeIds.Of( name ), Box( bufferBoxes, buffer ) );

	/// <summary>A texture's default view. Sampled, it has to be in <see cref="ImageLayout.ShaderRead"/>.</summary>
	public void Set( string name, TextureHandle texture ) => Put( AttributeIds.Of( name ), Box( textureBoxes, texture ) );

	/// <summary>Another view of a texture: one mip, say. Its layout is the caller's to arrange.</summary>
	public void Set( string name, TextureViewHandle view ) => Put( AttributeIds.Of( name ), Box( viewBoxes, view ) );

	/// <summary>A constant buffer's bytes.</summary>
	public void SetData( string name, byte[] data )
	{
		external = true;
		version++;
		Put( AttributeIds.Of( name ), data );
	}

	/// <summary>A dynamic combo (<c>D_*</c>).</summary>
	public void SetCombo( string name, int value ) => SetComboById( AttributeIds.Of( name ), value );

	internal void SetComboById( int id, int value )
	{
		if ( combos.TryGetValue( id, out var current ) && current == value ) return;
		combos[id] = value;
		version++;
		comboChanges++;
	}

	/// <summary>A value of one of the kinds above under an <see cref="AttributeIds"/> id, for callers that keep the ids.</summary>
	internal void SetById( int id, object value )
	{
		version++;
		Put( id, value );
	}

	/// <inheritdoc cref="SetById(int, object)"/>
	internal void SetById( int id, TextureHandle texture ) => Put( id, Box( textureBoxes, texture ) );

	/// <inheritdoc cref="SetById(int, object)"/>
	internal void SetById( int id, TextureViewHandle view ) => Put( id, Box( viewBoxes, view ) );

	/// <inheritdoc cref="SetById(int, object)"/>
	internal void SetById( int id, BufferHandle buffer ) => Put( id, Box( bufferBoxes, buffer ) );

	// Handles stored as objects, boxed once and kept: a set that is filled every draw (the engine's lowered attributes)
	// boxes nothing in steady state. Forgotten wholesale if they pile up (handles of freed resources)
	readonly Dictionary<TextureHandle, object> textureBoxes = new();
	readonly Dictionary<TextureViewHandle, object> viewBoxes = new();
	readonly Dictionary<BufferHandle, object> bufferBoxes = new();

	static object Box<T>( Dictionary<T, object> boxes, T value ) where T : struct
	{
		if ( boxes.TryGetValue( value, out var boxed ) ) return boxed;
		if ( boxes.Count >= 4096 ) boxes.Clear();
		return boxes[value] = value;
	}

	/// <summary>The value set under a name, or null.</summary>
	public object Get( string name ) => Get( AttributeIds.Of( name ) );

	/// <summary>The value set under an <see cref="AttributeIds"/> id, or null.</summary>
	public object Get( int id ) => (uint)id < (uint)stamps.Length && stamps[id] == generation ? slots[id] : under?.Get( id );

	/// <summary>A combo's value, or null when it wasn't set.</summary>
	public int? GetCombo( string name ) => combos.Count == 0 && under is null ? null : GetCombo( AttributeIds.Of( name ) );

	internal int? GetCombo( int id ) => combos.Count != 0 && combos.TryGetValue( id, out var value ) ? value : under?.GetCombo( id );

	// Whether a value is set here, not under
	bool Own( int id ) => (uint)id < (uint)stamps.Length && stamps[id] == generation;

	/// <summary>
	/// A set whose values and combos show through where this one has none of the same name, until <see cref="Clear"/>: the
	/// engine's block attributes under each draw's, shared rather than copied. It must not change while this set is used,
	/// and has none under it itself.
	/// </summary>
	internal ShaderAttributes Under
	{
		get => under;
		set
		{
			if ( value?.under is not null ) throw new ArgumentException( "A set under another has none under it" );
			if ( ReferenceEquals( under, value ) ) return;
			under = value;
			version++;
			revision++;
		}
	}

	/// <summary>Every value set, by name, in the order first set: a foreach over it allocates nothing.</summary>
	public SetValues Values => new( this );

	/// <summary>The values of a <see cref="ShaderAttributes"/>, enumerated without allocating.</summary>
	public readonly struct SetValues( ShaderAttributes attributes ) : IEnumerable<KeyValuePair<string, object>>
	{
		/// <summary>An enumerator over the values, by name.</summary>
		public Enumerator GetEnumerator() => new( attributes );
		IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator() => GetEnumerator();
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

		/// <summary>Goes through the values in the order first set, then those of the set under it that it doesn't replace.</summary>
		public struct Enumerator : IEnumerator<KeyValuePair<string, object>>
		{
			readonly ShaderAttributes attributes;
			ShaderAttributes current;
			int index;

			/// <summary>Before the first value of <paramref name="attributes"/>.</summary>
			public Enumerator( ShaderAttributes attributes )
			{
				this.attributes = current = attributes;
				index = -1;
			}

			/// <summary>The value and its name.</summary>
			public readonly KeyValuePair<string, object> Current => new( AttributeIds.NameOf( current.set[index] ), current.slots[current.set[index]] );
			readonly object System.Collections.IEnumerator.Current => Current;

			/// <summary>On to the next value.</summary>
			public bool MoveNext()
			{
				while ( true )
				{
					if ( ++index < current.set.Count )
					{
						if ( ReferenceEquals( current, attributes ) || !attributes.Own( current.set[index] ) ) return true;
						continue;
					}

					if ( !ReferenceEquals( current, attributes ) || attributes.under is null ) return false;
					current = attributes.under;
					index = -1;
				}
			}

			/// <summary>Back to before the first value.</summary>
			public void Reset()
			{
				index = -1;
				current = attributes;
			}

			/// <summary>Nothing to release.</summary>
			public readonly void Dispose() { }
		}
	}

	void Put( int id, object value )
	{
		if ( id >= stamps.Length )
		{
			var size = Math.Max( Math.Max( stamps.Length * 2, 64 ), AttributeIds.Count );
			Array.Resize( ref slots, size );
			Array.Resize( ref stamps, size );
		}

		if ( stamps[id] != generation )
		{
			stamps[id] = generation;
			set.Add( id );
			revision++;
			version++;
		}
		else if ( !ReferenceEquals( slots[id], value ) )
		{
			revision++;
			version++;
		}

		slots[id] = value;
	}

	/// <summary>Moves on when a handle is added, replaced or cleared; numbers in owned arrays and combos don't move it.</summary>
	internal int Revision => under is null ? revision : HashCode.Combine( revision, under.revision, RuntimeHelpers.GetHashCode( under ) );

	/// <summary>Moves on any set, replace or clear; equal versions mean equal values only while <see cref="Stable"/>.</summary>
	internal int Version => under is null ? version : HashCode.Combine( version, under.version, RuntimeHelpers.GetHashCode( under ) );

	/// <summary><see cref="Version"/> without the combos' changes: equal while the values are, whatever the combos.</summary>
	internal int ValuesVersion => under is null ? version - comboChanges : HashCode.Combine( version - comboChanges, under.version - under.comboChanges, RuntimeHelpers.GetHashCode( under ) );

	/// <summary>False once a caller's own arrays are held: they may change in place.</summary>
	internal bool Stable => !external && (under?.Stable ?? true);

	/// <summary>Forget everything.</summary>
	public void Clear()
	{
		if ( set.Count > 0 || combos.Count > 0 || under is not null ) version++;
		if ( set.Count > 0 ) revision++;
		external = false;
		foreach ( var id in set ) slots[id] = null;
		set.Clear();
		combos.Clear();
		under = null;
		if ( ++generation == int.MaxValue )
		{
			Array.Clear( stamps );
			generation = 1;
		}
	}

	/// <summary>Same ids with the same values: numbers and bytes by content. Combos aren't compared.</summary>
	internal bool SameValues( ShaderAttributes other )
	{
		if ( EffectiveCount() != other.EffectiveCount() ) return false;
		foreach ( var id in set )
		{
			if ( !SameValue( slots[id], other.Get( id ) ) ) return false;
		}

		if ( under is not null )
		{
			foreach ( var id in under.set )
			{
				if ( !Own( id ) && !SameValue( under.slots[id], other.Get( id ) ) ) return false;
			}
		}

		return true;
	}

	int EffectiveCount()
	{
		var count = set.Count;
		if ( under is not null )
		{
			foreach ( var id in under.set )
			{
				if ( !Own( id ) ) count++;
			}
		}

		return count;
	}

	// Exact type checks: a pattern over arrays goes through a cast helper per value, as an array of another element type may convert
	static bool IsFloats( object value ) => value is not null && value.GetType() == typeof( float[] );
	static bool IsBytes( object value ) => value is not null && value.GetType() == typeof( byte[] );

	static bool SameValue( object mine, object theirs )
	{
		if ( IsFloats( mine ) ) return IsFloats( theirs ) && Unsafe.As<float[]>( mine ).AsSpan().SequenceEqual( Unsafe.As<float[]>( theirs ) );
		if ( IsBytes( mine ) ) return IsBytes( theirs ) && Unsafe.As<byte[]>( mine ).AsSpan().SequenceEqual( Unsafe.As<byte[]>( theirs ) );
		return ReferenceEquals( mine, theirs ) || Equals( mine, theirs );
	}

	static bool SameShape( object mine, object theirs )
	{
		if ( IsFloats( mine ) ) return IsFloats( theirs );
		if ( IsBytes( mine ) ) return IsBytes( theirs ) && Unsafe.As<byte[]>( mine ).Length == Unsafe.As<byte[]>( theirs ).Length;
		return ReferenceEquals( mine, theirs ) || Equals( mine, theirs );
	}

	static int ShapeHash( int id, object value ) => HashCode.Combine( id, IsFloats( value ) ? 1 : IsBytes( value ) ? Unsafe.As<byte[]>( value ).Length : value is null ? 0 : value.GetHashCode() );

	/// <summary>Same ids with the same handles; numbers and bytes need only the same ids, as constants are rewritten per draw. Combos aren't compared.</summary>
	internal bool SameStructure( ShaderAttributes other )
	{
		if ( EffectiveCount() != other.EffectiveCount() ) return false;
		foreach ( var id in set )
		{
			if ( !SameShape( slots[id], other.Get( id ) ) ) return false;
		}

		if ( under is not null )
		{
			foreach ( var id in under.set )
			{
				if ( !Own( id ) && !SameShape( under.slots[id], other.Get( id ) ) ) return false;
			}
		}

		return true;
	}

	/// <summary>A hash of what <see cref="SameStructure"/> compares, whatever the order the values were set in.</summary>
	internal int StructureHash()
	{
		var hash = 0;
		foreach ( var id in set ) hash += ShapeHash( id, slots[id] );
		if ( under is not null )
		{
			foreach ( var id in under.set )
			{
				if ( !Own( id ) ) hash += ShapeHash( id, under.slots[id] );
			}
		}

		return hash;
	}

	/// <summary>Copies <paramref name="other"/>'s values (not combos) into arrays this set owns, so a warm copy allocates nothing.</summary>
	internal void CopyValuesFrom( ShaderAttributes other )
	{
		Clear();
		if ( other.under is not null ) CopyOwnValuesFrom( other.under );
		CopyOwnValuesFrom( other );
	}

	void CopyOwnValuesFrom( ShaderAttributes other )
	{
		foreach ( var id in other.set )
		{
			if ( id >= copies.Length ) Array.Resize( ref copies, Math.Max( Math.Max( copies.Length * 2, 64 ), AttributeIds.Count ) );
			var value = other.slots[id];
			if ( IsFloats( value ) )
			{
				var floats = Unsafe.As<float[]>( value );
				var floatCopy = IsFloats( copies[id] ) ? Unsafe.As<float[]>( copies[id] ) : null;
				if ( floatCopy is null || floatCopy.Length != floats.Length ) copies[id] = floatCopy = new float[floats.Length];
				floats.CopyTo( floatCopy, 0 );
				value = floatCopy;
			}
			else if ( IsBytes( value ) )
			{
				var bytes = Unsafe.As<byte[]>( value );
				var byteCopy = IsBytes( copies[id] ) ? Unsafe.As<byte[]>( copies[id] ) : null;
				if ( byteCopy is null || byteCopy.Length != bytes.Length ) copies[id] = byteCopy = new byte[bytes.Length];
				bytes.CopyTo( byteCopy, 0 );
				value = byteCopy;
			}

			Put( id, value );
		}
	}

	/// <summary>
	/// Add <paramref name="other"/>'s values and combos, replacing any of the same name, sharing its values: for a set lowered
	/// once and copied under several draws' (the engine's block attributes), which must not change while the copies are used.
	/// </summary>
	internal void CopyFrom( ShaderAttributes other )
	{
		if ( other.under is not null ) CopyFrom( other.under );
		external |= other.external;
		foreach ( var id in other.set ) Put( id, other.slots[id] );
		foreach ( var (id, value) in other.combos ) SetComboById( id, value );
	}

	/// <summary>Add <paramref name="other"/>'s values and combos, replacing any of the same name.</summary>
	public void Merge( ShaderAttributes other )
	{
		if ( other.under is not null ) Merge( other.under );
		external |= other.external;

		// Arrays the other set reuses for its numbers are copied: setting them there again mustn't change this set
		foreach ( var id in other.set ) Put( id, other.slots[id] is float[] { Length: >= 1 and <= 4 } floats && other.Owns( id, floats ) ? (float[])floats.Clone() : other.slots[id] );
		foreach ( var (id, value) in other.combos ) SetComboById( id, value );
	}

	internal string ComboKey( ShaderFile shader )
	{
		var info = ShaderInfo.Of( shader );
		var key = new System.Text.StringBuilder();
		for ( int i = 0; i < info.ComboIds.Length; i++ )
		{
			if ( GetCombo( info.ComboIds[i] ) is int value && value != 0 ) key.Append( info.ComboNames[i] ).Append( '=' ).Append( value ).Append( ';' );
		}

		return key.ToString();
	}

	/// <summary>
	/// <see cref="ComboKey"/> without building it: the shader's dynamic combos' values in turn, a byte each, for a shader of
	/// at most eight whose values fit. False otherwise, and the caller keys by <see cref="ComboKey"/>.
	/// </summary>
	internal bool ComboBits( ShaderFile shader, out ulong bits ) => ComboBits( shader, out bits, out var more ) && more == 0 && ShaderInfo.Of( shader ).ComboIds.Length <= 8;

	/// <summary>As <see cref="ComboBits(ShaderFile, out ulong)"/> for up to sixteen combos: the first eight in <paramref name="bits"/>, the rest in <paramref name="more"/>.</summary>
	internal bool ComboBits( ShaderFile shader, out ulong bits, out ulong more )
	{
		bits = more = 0;
		var ids = ShaderInfo.Of( shader ).ComboIds;
		if ( ids.Length > 16 ) return false;
		if ( combos.Count == 0 && under is null ) return true;

		for ( int i = 0; i < ids.Length; i++ )
		{
			var value = GetCombo( ids[i] ) ?? 0;
			if ( (uint)value > byte.MaxValue ) return false;
			if ( i < 8 ) bits |= (ulong)value << (i * 8);
			else more |= (ulong)value << ((i - 8) * 8);
		}

		return true;
	}
}

/// <summary>What a drawer reads of a shader for every draw, worked out once: dynamic combos and specialization attribute ids.</summary>
internal sealed class ShaderInfo
{
	public ShaderFile Shader;
	public int[] ComboIds;
	public string[] ComboNames;
	public int[] SpecializationIds;

	static readonly ConditionalWeakTable<ShaderFile, ShaderInfo> table = new();
	static readonly ShaderInfo[] recent = new ShaderInfo[4];
	static int next;

	public static ShaderInfo Of( ShaderFile shader )
	{
		var ring = recent;
		for ( int i = 0; i < ring.Length; i++ )
		{
			var info = ring[i];
			if ( info is not null && ReferenceEquals( info.Shader, shader ) ) return info;
		}

		var made = table.GetValue( shader, static s => new ShaderInfo( s ) );
		ring[System.Threading.Interlocked.Increment( ref next ) & 3] = made;
		return made;
	}

	ShaderInfo( ShaderFile shader )
	{
		Shader = shader;
		ReadOnlySpan<ShaderProgramType> types = [ShaderProgramType.Vertex, ShaderProgramType.Pixel, ShaderProgramType.Compute];
		var names = new List<string>();
		foreach ( var type in types )
		{
			if ( shader[type] is { } program ) names.AddRange( program.DynamicCombos.Select( c => c.Name ) );
		}

		ComboNames = [.. names];
		ComboIds = [.. names.Select( AttributeIds.Of )];
		SpecializationIds = [.. types.ToArray()
			.SelectMany( t => shader[t]?.Variables ?? [] )
			.Where( v => v.Register == RegisterType.Specialization && v.Source == VariableSource.Attribute && !string.IsNullOrEmpty( v.Attribute ) )
			.Select( v => v.Attribute ).Distinct( StringComparer.Ordinal ).Select( AttributeIds.Of )];
	}
}

/// <summary>
/// One field of a vertex stream, matched to shader inputs by semantic and index as native's input layouts are.
/// </summary>
public readonly record struct VertexField( VertexSemantic Semantic, int Index, VertexFormat Format, int Offset );

/// <summary>
/// The layout of a vertex stream a <see cref="ShaderDrawer"/> draw reads. Inputs it has no field for read the default
/// stream. Pipelines are cached per layout object, so keep one instance per layout.
/// </summary>
public sealed class VertexStreamLayout( int stride, params VertexField[] fields )
{
	/// <summary>Bytes per vertex.</summary>
	public int Stride { get; } = stride;

	/// <summary>Its fields.</summary>
	public IReadOnlyList<VertexField> Fields { get; } = fields;

	/// <summary>
	/// The engine's <c>Vertex</c> (76 bytes): position, colour (bytes), normal, two uvs and a tangent. What
	/// <c>Graphics.DrawQuad</c> draws.
	/// </summary>
	public static VertexStreamLayout EngineVertex { get; } = new( 76,
		new( VertexSemantic.Position, 0, VertexFormat.Float3, 0 ),
		new( VertexSemantic.Color, 0, VertexFormat.UByte4Norm, 12 ),
		new( VertexSemantic.Normal, 0, VertexFormat.Float3, 16 ),
		new( VertexSemantic.TexCoord, 0, VertexFormat.Float4, 28 ),
		new( VertexSemantic.TexCoord, 1, VertexFormat.Float4, 44 ),
		new( VertexSemantic.Tangent, 0, VertexFormat.Float4, 60 ) );

	internal VertexField? Find( in VertexInput input ) => Find( input.Semantic, input.SemanticIndex );

	internal VertexField? Find( VertexSemantic semantic, int index )
	{
		foreach ( var field in Fields )
		{
			if ( field.Semantic == semantic && field.Index == index ) return field;
		}

		return null;
	}
}

/// <summary>
/// What a <see cref="ShaderDrawer"/> draw reads and how many: vertices of <see cref="Layout"/> from a buffer (or none, every
/// input reading the default stream), indices from a buffer or none, and the counts either here or in an indirect
/// arguments buffer, as the engine's <c>Graphics.Draw</c>, <c>DrawIndexed</c> and their indirect forms take them.
/// </summary>
public struct GeometryDraw
{
	/// <summary>The vertex buffer, read from <see cref="VertexOffset"/> bytes; none reads the default stream.</summary>
	public BufferHandle Vertices;

	/// <summary>Bytes into <see cref="Vertices"/> its vertex 0 is.</summary>
	public long VertexOffset;

	/// <summary>The vertices' layout; needed with <see cref="Vertices"/>.</summary>
	public VertexStreamLayout Layout;

	/// <summary>The index buffer, read from <see cref="IndexOffset"/> bytes; none draws vertices in order.</summary>
	public BufferHandle Indices;

	/// <summary>Bytes into <see cref="Indices"/> its index 0 is.</summary>
	public long IndexOffset;

	/// <summary>16- or 32-bit indices.</summary>
	public IndexFormat IndexFormat;

	/// <summary>What the vertices make.</summary>
	public PrimitiveTopology Topology;

	/// <summary>Vertices (or indices) per instance, from <see cref="First"/>.</summary>
	public int Count;

	/// <summary>The first vertex, or index.</summary>
	public int First;

	/// <summary>Added to each index.</summary>
	public int BaseVertex;

	/// <summary>Instances; 0 draws one.</summary>
	public int Instances;

	/// <summary>
	/// Arguments the counts are read from instead (four 32-bit values a draw, five indexed), <see cref="Stride"/> bytes
	/// apart from <see cref="ArgumentsOffset"/>, <see cref="DrawCount"/> of them.
	/// </summary>
	public BufferHandle Arguments;

	/// <summary>Bytes into <see cref="Arguments"/> the first draw's are.</summary>
	public long ArgumentsOffset;

	/// <summary>Draws read from <see cref="Arguments"/>.</summary>
	public int DrawCount;

	/// <summary>Bytes between draws' arguments; 0 packs them.</summary>
	public int Stride;
}

/// <summary>
/// Draws and dispatches with one of s&amp;box's compiled shaders and no material, binding every resource the shader
/// reflects by the name the engine sets it under, the way the engine's <c>Graphics</c> draws bind render attributes:
/// what a <c>Graphics</c> or <c>CommandList</c> draw (UI, post processing, debug drawing) lowers to on Borealis.Gpu.
/// Draws read vertices from a stream, or none (every input reads the default stream).
/// </summary>
/// <remarks>
/// It binds through the same material objects as Borealis's scene (<see cref="BackendContext"/>): a shader drawn a way is
/// an object holding its pipeline and persistent sets, with every named resource from the draw's attributes and its
/// material constants written each draw into the frame's pass constants. Made over a <see cref="BackendDevice"/>, it
/// shares that device's objects. <see cref="MaterialObjects"/> off binds by reflection every draw instead, which is the
/// reference the tests compare against; a draw also falls back to it when the frame's pass constants are full.
/// </remarks>
public sealed partial class ShaderDrawer : IDisposable
{
	readonly Device gpu;

	// The device it was made over, whose surfaces it draws (DrawSurface); null for one made over a library alone
	readonly BackendDevice device;
	readonly ShaderLibrary shaders;
	readonly ShaderDefaults defaults;
	readonly Dictionary<(ShaderFile Shader, string Mode, string Combos, ulong Specialization), ShaderVariant> variants = new();

	// Variants by their combos' values packed (ShaderAttributes.ComboBits) and the material's features array, built with no
	// strings: what most draws find theirs by
	readonly Dictionary<(ShaderFile Shader, string Mode, int[] Features, ulong Combos, ulong More, ulong Specialization), ShaderVariant> packedVariants = new( new PackedKeyComparer() );

	// Compared without hashing the mode name: a variant is looked up every draw
	sealed class PackedKeyComparer : IEqualityComparer<(ShaderFile Shader, string Mode, int[] Features, ulong Combos, ulong More, ulong Specialization)>
	{
		public bool Equals( (ShaderFile Shader, string Mode, int[] Features, ulong Combos, ulong More, ulong Specialization) a, (ShaderFile Shader, string Mode, int[] Features, ulong Combos, ulong More, ulong Specialization) b )
			=> ReferenceEquals( a.Shader, b.Shader ) && ReferenceEquals( a.Features, b.Features ) && a.Combos == b.Combos && a.More == b.More && a.Specialization == b.Specialization && string.Equals( a.Mode, b.Mode );

		public int GetHashCode( (ShaderFile Shader, string Mode, int[] Features, ulong Combos, ulong More, ulong Specialization) key )
			=> HashCode.Combine( RuntimeHelpers.GetHashCode( key.Shader ), RuntimeHelpers.GetHashCode( key.Features ), key.Combos, key.More, key.Specialization );
	}
	// A record struct, not a tuple: a tuple of eight or more keeps the rest in a tuple of its own, which its hash and equality
	// box until the JIT has optimised them
	readonly record struct PipelineKey( ShaderVariant Variant, Format Color, Format Depth, int Samples, VertexStreamLayout Layout, PrimitiveTopology Topology, bool Ids, ToolsPass Tools, bool Redrawn );
	readonly Dictionary<PipelineKey, PipelineHandle> pipelines = new();

	/// <summary>
	/// The tools pass colour draws take (<see cref="ToolsViews"/>): a custom object in a debug view's colour layer draws with
	/// its own vertex stage and the engine's tools view pixel stage (<see cref="ToolsViewStage"/>).
	/// </summary>
	internal ToolsPass ToolsPass { get; set; }
	readonly Dictionary<ShaderVariant, PipelineHandle> computePipelines = new();
	MaterialObjectCache cache;
	readonly bool ownsCache;
	readonly AttributeBindings bindings;

	/// <summary>
	/// A drawer over a shader library and the placeholders bound for anything a draw doesn't set, with material objects
	/// of its own.
	/// </summary>
	public ShaderDrawer( Device gpu, ShaderLibrary shaders, ShaderDefaults defaults )
	{
		this.gpu = gpu;
		this.shaders = shaders;
		this.defaults = defaults;
		ownsCache = true;
		bindings = new AttributeBindings( this );
	}

	/// <summary>
	/// A drawer sharing <paramref name="device"/>'s shaders, placeholders and material objects: one material binding
	/// service for the engine's drawing and Borealis's scene on that device.
	/// </summary>
	public ShaderDrawer( BackendDevice device )
	{
		this.device = device;
		gpu = device.Gpu;
		shaders = device.Shaders;
		defaults = device.Defaults;
		cache = device.MaterialCache;
		bindings = new AttributeBindings( this );
	}

	/// <summary>The library shaders come from.</summary>
	public ShaderLibrary Shaders => shaders;

	/// <summary>
	/// Bind through material objects (the default). Off binds by reflection every draw: a transient set per draw, written
	/// from scratch.
	/// </summary>
	public bool MaterialObjects { get; set; } = true;

	MaterialObjectCache Cache => cache ??= new MaterialObjectCache( gpu, defaults );

	/// <summary>Material objects made, descriptor sets written through them, and sets held now, for tests and benchmarks.</summary>
	public (int Objects, int SetsWritten, int SetsHeld) MaterialObjectStats => cache is null ? default : (cache.Count, cache.SetsWritten, cache.SetCount);

	/// <summary>
	/// The instance id stream draws read <c>TEXCOORD13</c> (<c>InstanceTransformUv</c>) from, per instance, from an offset in
	/// bytes: a frame's, where a custom object draws inside it, whose shaders index <c>g_TransformBuffer</c> with it as
	/// native's <c>CSceneSystem::BindTransformSlot</c> has them do. None (the default) reads the default stream.
	/// </summary>
	public (BufferHandle Buffer, long Offset) InstanceIds { get; set; }

	/// <summary>
	/// The material draws of its shader take their features (<c>F_*</c>, which pick the variant), constants and textures from,
	/// as native binds a material's mode: what a <c>Graphics</c> draw with a material that has a compiled file reads. Null (the
	/// default) draws the shader with its defaults. Draws of another shader ignore it.
	/// </summary>
	internal BackendMaterial Material { get; set; }

	BackendMaterial MaterialOf( ShaderFile shader ) => Material is { } material && ReferenceEquals( material.Shader, shader ) ? material : null;

	// A material's texture bindings by layout, pass slot and set, for one version of it: its textures change only with the version
	sealed class MaterialTextures
	{
		public BackendMaterial Material;
		public int Version = -1;
		public readonly MaterialLayout[] Layouts = new MaterialLayout[4];
		public readonly Binding?[][][] Sets = new Binding?[4][][];
		public int NextLayout;
		public readonly Dictionary<MaterialLayout, Binding?[][]> ByLayout = new( ReferenceEqualityComparer.Instance );
	}

	readonly ConditionalWeakTable<BackendMaterial, MaterialTextures> materialTextures = new();

	readonly MaterialTextures[] recentTextures = new MaterialTextures[4];
	int recentTexturesNext;

	// The material's texture for a variable when ready and the expected shape; kept per pass slot of the layout
	Binding MaterialTexture( BackendMaterial material, in ShaderResource resource, MaterialLayout layout, int set, int slot )
	{
		if ( layout is null ) return FindMaterialTexture( material, resource );

		MaterialTextures textures = null;
		foreach ( var recent in recentTextures )
		{
			if ( recent is not null && ReferenceEquals( recent.Material, material ) ) { textures = recent; break; }
		}

		if ( textures is null )
		{
			textures = materialTextures.GetValue( material, static m => new MaterialTextures { Material = m } );
			recentTextures[recentTexturesNext] = textures;
			recentTexturesNext = (recentTexturesNext + 1) & 3;
		}

		var version = material.Version;
		if ( textures.Version != version )
		{
			textures.ByLayout.Clear();
			Array.Clear( textures.Layouts );
			textures.Version = version;
		}

		Binding?[][] sets = null;
		for ( int i = 0; i < textures.Layouts.Length; i++ )
		{
			if ( ReferenceEquals( textures.Layouts[i], layout ) ) { sets = textures.Sets[i]; break; }
		}

		if ( sets is null )
		{
			if ( !textures.ByLayout.TryGetValue( layout, out sets ) ) textures.ByLayout[layout] = sets = new Binding?[ShaderAbi.BindlessSet][];
			var next = textures.NextLayout;
			(textures.Layouts[next], textures.Sets[next]) = (layout, sets);
			textures.NextLayout = (next + 1) & 3;
		}

		var held = sets[set] ??= new Binding?[layout.PassSlots[set].Length];
		if ( held[slot] is { } found ) return found;

		var binding = FindMaterialTexture( material, resource );
		held[slot] = binding;
		return binding;
	}

	Binding FindMaterialTexture( BackendMaterial material, in ShaderResource resource )
	{
		var expected = resource.Dimension switch
		{
			SpirvImageDimension.Cube => TextureDimension.TextureCube,
			SpirvImageDimension.CubeArray => TextureDimension.TextureCubeArray,
			SpirvImageDimension.Texture3D => TextureDimension.Texture3D,
			SpirvImageDimension.Texture2DArray => TextureDimension.Texture2DArray,
			_ => TextureDimension.Texture2D,
		};

		if ( !material.Textures.TryGetValue( resource.Variable.Name, out var texture ) || texture.Dimension != expected || (texture.Samples > 1) != (resource.Dimension == SpirvImageDimension.Texture2DMS) ) return defaults.For( resource );
		return Binding.Sampled( resource.Binding, texture.Handle, resource.Variable.RenderStateId == 1 && gpu.InfoOf( texture.Handle ).Format.HasSrgb() );
	}

	// Material objects key their pipelines by layout object: a layout with instance ids is another one, so it has a key of its own
	readonly Dictionary<VertexStreamLayout, object> idsLayouts = new();
	readonly object noLayoutIds = new();

	object LayoutKey( VertexStreamLayout layout, bool ids )
	{
		if ( !ids ) return layout;
		if ( layout is null ) return noLayoutIds;
		if ( !idsLayouts.TryGetValue( layout, out var key ) ) idsLayouts[layout] = key = new object();
		return key;
	}

	static bool IsInstanceId( in VertexInput input ) => input.Semantic == VertexSemantic.TexCoord && input.SemanticIndex == 13;

	// Whether a draw of the variant reads the lent instance ids. One that expands primitives reads the first id for every
	// primitive (its instances are primitives): one object's draw, as native binds one transform slot for it
	bool ReadsInstanceIds( ShaderVariant variant )
	{
		if ( !InstanceIds.Buffer.IsValid ) return false;

		// By index: every draw asks, and enumerating the list through its interface boxed an enumerator each time
		var inputs = variant.VertexInputs;
		for ( int i = 0; i < inputs.Count; i++ )
		{
			if ( IsInstanceId( inputs[i] ) ) return true;
		}

		return false;
	}

	/// <summary>Draws that fell back to binding by reflection because the frame's pass constants were full.</summary>
	public int Fallbacks { get; private set; }

	/// <summary>Free the material objects this drawer made for itself (not those of a device it shares).</summary>
	/// <summary>
	/// Destroy the pipelines this drawer made and kept (a material object's are its cache's), once the GPU is past what's
	/// been submitted with them; and its material objects, if it has its own.
	/// </summary>
	public void Dispose()
	{
		foreach ( var pipeline in pipelines.Values ) gpu.Destroy( pipeline );
		pipelines.Clear();
		foreach ( var pipeline in computePipelines.Values ) gpu.Destroy( pipeline );
		computePipelines.Clear();
		computeObjects.Clear();
		if ( ownsCache ) cache?.Dispose();
		cache = null;
	}

	// Last lookups' variants: a draw asks twice (scope depth, then binds)
	struct VariantMemo
	{
		public ShaderFile Shader;
		public string Mode;
		public ShaderAttributes Attributes;
		public int Version;
		public BackendMaterial Material;
		public int MaterialVersion;
		public ShaderVariant Found;
	}

	const int VariantMemos = 256;
	readonly VariantMemo[] variantMemo = new VariantMemo[VariantMemos];

	/// <summary>
	/// The shader's variant for its first mode (or <paramref name="mode"/>) and the attributes' dynamic combos.
	/// </summary>
	public ShaderVariant Variant( ShaderFile shader, ShaderAttributes attributes, string mode = null )
	{
		mode ??= shader.Modes[0].Name;
		var material = MaterialOf( shader );
		var materialVersion = material?.Version ?? 0;
		var version = attributes.Version;
		var stable = attributes.Stable;

		// By where the lookup lands: a scene draws hundreds of materials, each asking again for every pass
		var slot = (RuntimeHelpers.GetHashCode( (object)material ?? shader ) + RuntimeHelpers.GetHashCode( attributes ) * 31 + version) & (VariantMemos - 1);
		if ( stable )
		{
			ref readonly var memo = ref variantMemo[slot];
			if ( ReferenceEquals( memo.Attributes, attributes ) && memo.Version == version && ReferenceEquals( memo.Shader, shader ) && ReferenceEquals( memo.Material, material ) && memo.MaterialVersion == materialVersion && (ReferenceEquals( memo.Mode, mode ) || memo.Mode == mode) ) return memo.Found;
		}

		pendingVariant = false;
		var found = FindVariant( shader, attributes, mode, material?.Features ?? [] );

		// Not while its module compiles: it changes when that's done
		if ( stable && found is not null && !pendingVariant )
		{
			variantMemo[slot] = new VariantMemo { Shader = shader, Mode = mode, Attributes = attributes, Version = version, Material = material, MaterialVersion = materialVersion, Found = found };
		}

		return found;
	}

	ShaderVariant FindVariant( ShaderFile shader, ShaderAttributes attributes, string mode, int[] features )
	{
		var specialization = SpecializationKey( shader, attributes );
		if ( attributes.ComboBits( shader, out var bits, out var more ) )
		{
			var packed = (shader, mode, features, bits, more, specialization);
			if ( packedVariants.TryGetValue( packed, out var found ) ) return found;
			found = VariantByName( shader, attributes, mode, features, specialization );
			if ( found is not null && !pendingVariant ) packedVariants[packed] = found;
			return found;
		}

		return VariantByName( shader, attributes, mode, features, specialization );
	}

	/// <summary>The values of the shader's specialization attributes in <paramref name="attributes"/>, as one key.</summary>
	ulong SpecializationKey( ShaderFile shader, ShaderAttributes attributes )
	{
		var ids = ShaderInfo.Of( shader ).SpecializationIds;

		ulong key = 0;
		foreach ( var id in ids ) key = key * 1000003 + (uint)(attributes.Get( id ) is float[] { Length: > 0 } f ? (int)f[0] : 0) + 1;
		return key;
	}

	// A material's feature values as a variant key's part, made once per array (materials keep theirs)
	readonly ConditionalWeakTable<int[], string> featureKeys = new();

	string FeaturesKey( int[] features ) => featureKeys.GetValue( features, static f => string.Join( ',', f ) );

	// Whether the last VariantByName's variant was one whose module is still compiling
	bool pendingVariant;

	ShaderVariant VariantByName( ShaderFile shader, ShaderAttributes attributes, string mode, int[] features, ulong specialization )
	{
		pendingVariant = false;
		var combos = attributes.ComboKey( shader );
		if ( features.Length > 0 ) combos = string.Concat( combos, "|", FeaturesKey( features ) );
		var key = (shader, mode, combos, specialization);
		if ( variants.TryGetValue( key, out var variant ) ) return variant;

		// Not kept while its module compiles: its programs draw until then
		variant = shaders.Variant( shader, mode, features, attributes.GetCombo, name => attributes.Get( name ) is float[] { Length: > 0 } f ? (int)f[0] : null, false, out var pending );
		pendingVariant = pending;
		if ( !pending ) variants[key] = variant;
		return variant;
	}

	/// <summary>
	/// Draw <paramref name="instances"/> instances of <paramref name="indexCount"/> indices with
	/// <paramref name="shader"/> inside the list's open rendering scope, into a colour target of
	/// <paramref name="color"/>. <paramref name="viewport"/> is what the shader reads as <c>Source( Viewport )</c>
	/// (x, y, width, height); it's also set as the viewport and scissor.
	/// </summary>
	public bool DrawIndexedInstanced( CommandList list, ShaderFile shader, ShaderAttributes attributes, Format color, int samples, BufferHandle indices, int indexCount, int instances, System.Numerics.Vector4 viewport, string mode = null )
		=> DrawIndexedInstanced( list, shader, attributes, color, Format.Unknown, samples, indices, indexCount, instances, viewport, mode );

	/// <summary>
	/// As the overload without <paramref name="depth"/>, in a scope with a depth target of that format
	/// (<see cref="Format.Unknown"/> for none), which the shader's depth state tests and writes.
	/// </summary>
	public bool DrawIndexedInstanced( CommandList list, ShaderFile shader, ShaderAttributes attributes, Format color, Format depth, int samples, BufferHandle indices, int indexCount, int instances, System.Numerics.Vector4 viewport, string mode = null )
	{
		var variant = Variant( shader, attributes, mode );
		if ( variant?.Vertex?.Bytecode is null ) return false;

		var ids = BindForDraw( list, variant, attributes, color, depth, samples, null, PrimitiveTopology.TriangleList, viewport );
		list.SetVertexBuffer( 0, defaults.VertexDefaults );
		if ( ids ) list.SetVertexBuffer( 1, InstanceIds.Buffer, InstanceIds.Offset );
		list.SetIndexBuffer( indices, IndexFormat.UInt32 );
		list.DrawIndexed( indexCount, instances );
		return true;
	}

	/// <summary>
	/// Draw <paramref name="vertexCount"/> vertices of <paramref name="layout"/> from <paramref name="vertices"/> at
	/// <paramref name="offset"/>, as <paramref name="topology"/>, inside the list's open rendering scope. The rest is as
	/// <see cref="DrawIndexedInstanced(CommandList, ShaderFile, ShaderAttributes, Format, int, BufferHandle, int, int, System.Numerics.Vector4, string)"/>.
	/// </summary>
	public bool Draw( CommandList list, ShaderFile shader, ShaderAttributes attributes, Format color, int samples, BufferHandle vertices, long offset, VertexStreamLayout layout, int vertexCount, PrimitiveTopology topology, System.Numerics.Vector4 viewport, string mode = null )
		=> Draw( list, shader, attributes, color, Format.Unknown, samples, vertices, offset, layout, vertexCount, topology, viewport, mode );

	/// <summary>
	/// As the overload without <paramref name="depth"/>, in a scope with a depth target of that format
	/// (<see cref="Format.Unknown"/> for none), which the shader's depth state tests and writes.
	/// </summary>
	public bool Draw( CommandList list, ShaderFile shader, ShaderAttributes attributes, Format color, Format depth, int samples, BufferHandle vertices, long offset, VertexStreamLayout layout, int vertexCount, PrimitiveTopology topology, System.Numerics.Vector4 viewport, string mode = null )
	{
		var variant = Variant( shader, attributes, mode );
		if ( variant?.Vertex?.Bytecode is null ) return false;

		// A vertex stage that expands primitives (where the .shader had a geometry stage) runs its vertices once per
		// primitive, an instance each, reading the primitive's vertices as per-instance streams 0, 2 and 3
		var expansion = variant.Expansion;
		var instances = 1;
		if ( expansion.Expands )
		{
			instances = expansion.Primitives( topology, vertexCount );
			if ( instances < 0 || layout is null ) return false;
			if ( instances == 0 ) return true;
		}

		var ids = BindForDraw( list, variant, attributes, color, depth, samples, layout, topology, viewport );
		list.SetVertexBuffer( 0, vertices, offset );
		list.SetVertexBuffer( 1, defaults.VertexDefaults );
		if ( !expansion.Expands )
		{
			if ( ids ) list.SetVertexBuffer( 2, InstanceIds.Buffer, InstanceIds.Offset );
			list.Draw( vertexCount );
			return true;
		}

		// A strip's later corners read streams of their own a vertex on; a list's read stream 0 (Pipeline)
		for ( int corner = expansion.Step( topology ); corner < expansion.Corners; corner++ ) list.SetVertexBuffer( 1 + corner, vertices, offset + corner * layout.Stride );
		if ( ids ) list.SetVertexBuffer( 1 + expansion.Corners, InstanceIds.Buffer, InstanceIds.Offset );
		list.Draw( expansion.Vertices, instances );
		return true;
	}

	/// <summary>
	/// Draw <paramref name="instances"/> instances of <paramref name="indexCount"/> 32-bit indices from
	/// <paramref name="firstIndex"/> in <paramref name="indices"/>, reading vertices of <paramref name="layout"/> from
	/// <paramref name="vertices"/> offset by <paramref name="baseVertex"/>, as triangles: a model's draw call, as
	/// <c>Graphics.DrawModelInstanced</c> draws it. False for a variant that expands primitives. The rest is as
	/// <see cref="Draw(CommandList, ShaderFile, ShaderAttributes, Format, Format, int, BufferHandle, long, VertexStreamLayout, int, PrimitiveTopology, System.Numerics.Vector4, string)"/>.
	/// </summary>
	public bool DrawIndexed( CommandList list, ShaderFile shader, ShaderAttributes attributes, Format color, Format depth, int samples, BufferHandle vertices, VertexStreamLayout layout, BufferHandle indices, int firstIndex, int indexCount, int baseVertex, int instances, System.Numerics.Vector4 viewport, string mode = null )
	{
		var variant = Variant( shader, attributes, mode );
		if ( variant?.Vertex?.Bytecode is null || variant.Expansion.Expands ) return false;

		var ids = BindForDraw( list, variant, attributes, color, depth, samples, layout, PrimitiveTopology.TriangleList, viewport );
		list.SetVertexBuffer( 0, vertices );
		list.SetVertexBuffer( 1, defaults.VertexDefaults );
		if ( ids ) list.SetVertexBuffer( 2, InstanceIds.Buffer, InstanceIds.Offset );
		list.SetIndexBuffer( indices, IndexFormat.UInt32 );
		list.DrawIndexed( indexCount, instances, firstIndex, baseVertex );
		return true;
	}

	/// <summary>
	/// Draw <paramref name="draw"/>'s geometry inside the list's open rendering scope. A variant that expands primitives
	/// draws only vertices from a buffer, unindexed, one instance, with the counts here: false for the rest. The rest is as
	/// <see cref="Draw(CommandList, ShaderFile, ShaderAttributes, Format, Format, int, BufferHandle, long, VertexStreamLayout, int, PrimitiveTopology, System.Numerics.Vector4, string)"/>.
	/// </summary>
	public bool Draw( CommandList list, ShaderFile shader, ShaderAttributes attributes, Format color, Format depth, int samples, in GeometryDraw draw, System.Numerics.Vector4 viewport, string mode = null )
	{
		var variant = Variant( shader, attributes, mode );
		if ( variant?.Vertex?.Bytecode is null ) return false;

		var layout = draw.Vertices.IsValid ? draw.Layout : null;
		var indirect = draw.Arguments.IsValid;
		var indexed = draw.Indices.IsValid;
		if ( variant.Expansion.Expands )
		{
			if ( layout is null || indexed || indirect || draw.Instances > 1 ) return false;
			return Draw( list, shader, attributes, color, depth, samples, draw.Vertices, draw.VertexOffset + (long)draw.First * layout.Stride, layout, draw.Count, draw.Topology, viewport, mode );
		}

		var ids = BindForDraw( list, variant, attributes, color, depth, samples, layout, draw.Topology, viewport );
		if ( layout is null )
		{
			list.SetVertexBuffer( 0, defaults.VertexDefaults );
			if ( ids ) list.SetVertexBuffer( 1, InstanceIds.Buffer, InstanceIds.Offset );
		}
		else
		{
			list.SetVertexBuffer( 0, draw.Vertices, draw.VertexOffset );
			list.SetVertexBuffer( 1, defaults.VertexDefaults );
			if ( ids ) list.SetVertexBuffer( 2, InstanceIds.Buffer, InstanceIds.Offset );
		}

		if ( indexed ) list.SetIndexBuffer( draw.Indices, draw.IndexFormat, draw.IndexOffset );

		var instances = Math.Max( draw.Instances, 1 );
		if ( indirect && indexed ) list.DrawIndexedIndirect( draw.Arguments, draw.ArgumentsOffset, draw.DrawCount, draw.Stride > 0 ? draw.Stride : 20 );
		else if ( indirect ) list.DrawIndirect( draw.Arguments, draw.ArgumentsOffset, draw.DrawCount, draw.Stride > 0 ? draw.Stride : 16 );
		else if ( indexed ) list.DrawIndexed( draw.Count, instances, draw.First, draw.BaseVertex );
		else list.Draw( draw.Count, instances, draw.First );
		return true;
	}

	struct ObjectMemo
	{
		public ShaderVariant Variant;
		public VertexStreamLayout Layout;
		public bool Ids, Redrawn;
		public Format Color, Depth;
		public int Samples;
		public PrimitiveTopology Topology;
		public ToolsPass Tools;
		public MaterialObject Found;
	}

	const int ObjectMemos = 128;
	readonly ObjectMemo[] objectMemo = new ObjectMemo[ObjectMemos];

	static int ObjectSlot( ShaderVariant variant, VertexStreamLayout layout, Format color, Format depth, int samples ) =>
		(RuntimeHelpers.GetHashCode( variant ) + RuntimeHelpers.GetHashCode( layout ) * 31 + (int)color * 7 + (int)depth * 13 + samples) & (ObjectMemos - 1);

	MaterialObject RememberedObject( ShaderVariant variant, VertexStreamLayout layout, bool ids, Format color, Format depth, int samples, PrimitiveTopology topology, ToolsPass tools, bool redrawn )
	{
		ref readonly var memo = ref objectMemo[ObjectSlot( variant, layout, color, depth, samples )];
		if ( ReferenceEquals( memo.Variant, variant ) && ReferenceEquals( memo.Layout, layout ) && memo.Ids == ids && memo.Color == color && memo.Depth == depth && memo.Samples == samples && memo.Topology == topology && memo.Tools == tools && memo.Redrawn == redrawn ) return memo.Found;
		return null;
	}

	// Bind a draw's pipeline and resources; true when it reads the lent instance ids, which the caller binds after its streams
	bool BindForDraw( CommandList list, ShaderVariant variant, ShaderAttributes attributes, Format color, Format depth, int samples, VertexStreamLayout layout, PrimitiveTopology topology, System.Numerics.Vector4 viewport )
	{
		if ( BackendDevice.DrawMarkers ) list.Marker( BackendDevice.DrawMarkerName( variant ) );
		var ids = ReadsInstanceIds( variant );
		var redrawn = RedrawnEveryFrame.Active;
		var tools = color == Format.Unknown ? ToolsPass.None : ToolsPass;
		var materialObject = MaterialObjects ? RememberedObject( variant, layout, ids, color, depth, samples, topology, tools, redrawn ) : null;
		if ( materialObject is not null ? HasPassSpace( materialObject.Layout ) : Objects( variant ) )
		{
			if ( materialObject is null )
			{
				var key = new MaterialObjectCache.Key( null, variant, LayoutKey( layout, ids ), color, depth, samples, false, false, topology, Tools: tools, Redrawn: redrawn );
				materialObject = cache.Get( key, immediate: true, (Drawer: this, Variant: variant, Color: color, Depth: depth, Samples: samples, Layout: layout, Topology: topology, Ids: ids, Redrawn: redrawn),
					static ( s, materialLayout ) => s.Drawer.Pipeline( s.Variant, s.Color, s.Depth, s.Samples, s.Layout, s.Topology, materialLayout.Sets, s.Ids, s.Redrawn ) );
				objectMemo[ObjectSlot( variant, layout, color, depth, samples )] = new ObjectMemo { Variant = variant, Layout = layout, Ids = ids, Redrawn = redrawn, Color = color, Depth = depth, Samples = samples, Topology = topology, Tools = tools, Found = materialObject };
			}

			SetViewport( list, viewport );
			BindObject( list, materialObject, attributes, viewport );
		}
		else
		{
			list.SetPipeline( Pipeline( variant, color, depth, samples, layout, topology, null, ids, redrawn ) );
			SetViewport( list, viewport );
			Bind( list, variant, attributes, viewport );
		}

		// The stencil reference is dynamic state: a shader that tests or writes stencil (gizmos, tool overlays) sets its own
		if ( variant.State.StencilTest && depth.HasStencil() ) list.SetStencilReference( (byte)variant.State.StencilReference );

		// So is the depth bias drawing into depth alone (a shadow map's)
		if ( color == Format.Unknown && depth != Format.Unknown ) list.SetDepthBias( DepthBias.Constant, DepthBias.Slope );
		return ids;
	}

	/// <summary>Whether this draw binds through material objects: they're on, and the frame's pass constants have room for it.</summary>
	bool Objects( ShaderVariant variant ) => MaterialObjects && HasPassSpace( Cache.LayoutOf( variant, immediate: true ) );

	bool HasPassSpace( MaterialLayout layout )
	{
		if ( Cache.PassSpace >= layout.ImmediateBytes ) return true;
		Fallbacks++;
		return false;
	}

	void BindObject( CommandList list, MaterialObject materialObject, ShaderAttributes attributes, System.Numerics.Vector4 viewport )
	{
		if ( bindings.Slot < 0 ) bindings.Slot = cache.NewSlot();
		bindings.Attributes = attributes;
		bindings.Viewport = viewport;
		var bound = default( PipelineHandle );
		var values = AttributeStateOf( list, attributes, viewport );
		bindings.AttributesEpoch = values.Id;
		bindings.Structure = values.Structure >= 0 ? values.Structure : values.Structure = StructureOf( attributes );
		var material = Material;
		materialObject.BindImmediate( list, ref bound, material, material?.Version ?? 0, bindings );
		bindings.Attributes = null;
	}

	// Values draws bound in, per list, frame and viewport. Id: values constants were written from; Structure: handles the sets bind.
	sealed class AttributeState
	{
		public ShaderAttributes Source;
		public int Version;
		public long Id;
		public long Structure = -1;
		public readonly ShaderAttributes Values = new();

		public void Forget()
		{
			Source = null;
			Structure = -1;
			Values.Clear();
		}
	}

	// A pass's draws cycle through a few sets of values (an MDI draw per material group and pass); more kept than they cycle through
	const int AttributeStatesKept = 8, StructuresKept = 1024;
	readonly AttributeState[] attributeStates = new AttributeState[AttributeStatesKept];
	int attributeStateNext;

	// Handle sets by content (SameStructure), each with a stable id; hashed since attribute sets churn every frame.
	sealed class StructureKey
	{
		public long Id;
		public StructureKey Next;
		public readonly ShaderAttributes Reference = new();
	}

	readonly Dictionary<int, StructureKey> structures = new();

	long StructureOf( ShaderAttributes attributes )
	{
		var hash = attributes.StructureHash();
		structures.TryGetValue( hash, out var first );
		for ( var known = first; known is not null; known = known.Next )
		{
			if ( attributes.SameStructure( known.Reference ) ) return known.Id;
		}

		// Past the limit: forget them all; ids stay unique, so what was resolved under them is only resolved again
		if ( structures.Count >= StructuresKept )
		{
			structures.Clear();
			first = null;
		}

		var made = new StructureKey { Next = first, Id = cache.NextEpoch() };
		made.Reference.CopyValuesFrom( attributes );
		structures[hash] = made;
		return made.Id;
	}

	// What the attribute states were made for: they hold only while a draw has the same list, device frame and viewport
	CommandList passList;
	long passFrame;
	System.Numerics.Vector4 passViewport;

	// The state a draw binds in; another list, frame, values or viewport never reuses an old one.
	AttributeState AttributeStateOf( CommandList list, ShaderAttributes attributes, System.Numerics.Vector4 viewport )
	{
		var frame = cache.Frame;
		if ( !ReferenceEquals( list, passList ) || frame != passFrame || viewport != passViewport )
		{
			(passList, passFrame, passViewport) = (list, frame, viewport);
			foreach ( var state in attributeStates ) state?.Forget();
		}

		var version = attributes.ValuesVersion;
		if ( attributes.Stable )
		{
			foreach ( var state in attributeStates )
			{
				if ( state is not null && ReferenceEquals( state.Source, attributes ) && state.Version == version ) return state;
			}
		}

		// Another set, or the same one set again, with the same values
		foreach ( var state in attributeStates )
		{
			if ( state?.Source is null || !attributes.SameValues( state.Values ) ) continue;
			state.Source = attributes;
			state.Version = version;
			return state;
		}

		var made = attributeStates[attributeStateNext] ??= new AttributeState();
		attributeStateNext = (attributeStateNext + 1) % AttributeStatesKept;
		made.Forget();
		made.Source = attributes;
		made.Version = version;
		made.Values.CopyValuesFrom( attributes );
		made.Id = cache.NextEpoch();
		return made;
	}

	/// <summary>Forget the last draws' lists, materials and attributes so their owners can be collected.</summary>
	internal void ReleasePass()
	{
		passList = null;
		foreach ( var state in attributeStates ) state?.Forget();
		structures.Clear();
		Array.Clear( variantMemo );
		Array.Clear( objectMemo );
		Array.Clear( stageConstants );
	}

	/// <summary>
	/// Run a compute shader over <paramref name="x"/> by <paramref name="y"/> by <paramref name="z"/> threads (divided
	/// by its thread group size, as native does), outside any rendering scope. Storage textures bind by attribute too.
	/// </summary>
	public bool Dispatch( CommandList list, ShaderFile shader, ShaderAttributes attributes, int x, int y, int z, string mode = null )
	{
		return Dispatch( list, Variant( shader, attributes, mode ), attributes, x, y, z );
	}

	/// <summary>
	/// <see cref="Dispatch(CommandList, ShaderFile, ShaderAttributes, int, int, int, string)"/> with the variant already found
	/// (<see cref="Variant"/>), for a caller that needed it first.
	/// </summary>
	internal bool Dispatch( CommandList list, ShaderVariant variant, ShaderAttributes attributes, int x, int y, int z )
	{
		if ( variant?.Compute?.Bytecode is null ) return false;

		BindForDispatch( list, variant, attributes );
		var (gx, gy, gz) = variant.Compute.Bytecode.ThreadGroupSize;
		gx = Math.Max( gx, 1 );
		gy = Math.Max( gy, 1 );
		gz = Math.Max( gz, 1 );
		list.Dispatch( (x + gx - 1) / gx, (y + gy - 1) / gy, (z + gz - 1) / gz );
		return true;
	}

	/// <summary>
	/// Run a compute shader over the thread groups (not threads) that <paramref name="arguments"/> holds at
	/// <paramref name="offset"/> bytes - three 32-bit counts, written by an earlier pass - as native's indirect dispatch does.
	/// The rest is as <see cref="Dispatch(CommandList, ShaderFile, ShaderAttributes, int, int, int, string)"/>.
	/// </summary>
	public bool DispatchIndirect( CommandList list, ShaderFile shader, ShaderAttributes attributes, BufferHandle arguments, long offset, string mode = null )
	{
		return DispatchIndirect( list, Variant( shader, attributes, mode ), attributes, arguments, offset );
	}

	/// <summary>
	/// <see cref="DispatchIndirect(CommandList, ShaderFile, ShaderAttributes, BufferHandle, long, string)"/> with the variant
	/// already found (<see cref="Variant"/>).
	/// </summary>
	internal bool DispatchIndirect( CommandList list, ShaderVariant variant, ShaderAttributes attributes, BufferHandle arguments, long offset )
	{
		if ( variant?.Compute?.Bytecode is null ) return false;

		BindForDispatch( list, variant, attributes );
		list.DispatchIndirect( arguments, offset );
		return true;
	}

	void BindForDispatch( CommandList list, ShaderVariant variant, ShaderAttributes attributes )
	{
		if ( MaterialObjects )
		{
			// Found in the cache once a variant: an object with no material is never made again
			if ( !computeObjects.TryGetValue( variant, out var materialObject ) )
			{
				var key = new MaterialObjectCache.Key( null, variant, null, Format.Unknown, Format.Unknown, 0, false, false );
				computeObjects[variant] = materialObject = Cache.Get( key, immediate: true, (Drawer: this, Variant: variant), static ( s, layout ) => s.Drawer.ComputePipeline( s.Variant, layout.Sets ) );
			}

			// As Objects: while the frame's pass constants have room for it
			if ( cache.PassSpace >= materialObject.Layout.ImmediateBytes )
			{
				BindObject( list, materialObject, attributes, default );
				return;
			}

			Fallbacks++;
		}

		list.SetPipeline( ComputePipeline( variant, null ) );
		Bind( list, variant, attributes, default );
	}

	// Each compute variant's material object (BindForDispatch)
	readonly Dictionary<ShaderVariant, MaterialObject> computeObjects = new();

	/// <summary>
	/// The compute pipeline a dispatch of <paramref name="variant"/> binds by reflection with (made once), for a caller that
	/// fills the variant's transient sets itself.
	/// </summary>
	internal PipelineHandle ReflectedComputePipeline( ShaderVariant variant ) => ComputePipeline( variant, null );

	/// <summary>A compute pipeline for the reflection path (cached here) or for a material object's sets (the object keeps it).</summary>
	PipelineHandle ComputePipeline( ShaderVariant variant, BindingLayoutHandle[] sets )
	{
		if ( sets is null && computePipelines.TryGetValue( variant, out var cached ) ) return cached;

		var pipeline = gpu.CreateComputePipeline( new ComputePipelineInfo
		{
			Shader = variant.Compute.Module,
			Entry = variant.Compute.Bytecode.EntryPoint( ShaderProgramType.Compute ),
			Constants = variant.Compute.Constants,
			Sets = sets ?? variant.Sets,
			Name = variant.ToString(),
		} );

		if ( sets is null ) computePipelines[variant] = pipeline;
		return pipeline;
	}

	/// <summary>
	/// The depth range draws map to, 0 to 1 by default: a frame's own where drawing lands inside it (a custom object in one of
	/// Borealis's layers, whose view may keep part of the range for its 3D skybox).
	/// </summary>
	public (float Min, float Max) DepthRange { get; set; } = (0, 1);

	/// <summary>
	/// The rasterizer depth bias of draws into depth alone, none by default: a shadow map's, as native's shadow layers override
	/// their draws' (<c>RsDepthBiasStateOverride_t</c>). Draws with colour have none.
	/// </summary>
	public (int Constant, float Slope) DepthBias { get; set; }

	void SetViewport( CommandList list, System.Numerics.Vector4 viewport )
	{
		list.SetViewport( viewport.X, viewport.Y, viewport.Z, viewport.W, DepthRange.Min, DepthRange.Max );
		list.SetScissor( (int)viewport.X, (int)viewport.Y, (int)MathF.Ceiling( viewport.Z ), (int)MathF.Ceiling( viewport.W ) );
	}

	/// <summary>
	/// A graphics pipeline for the reflection path (cached here) or for a material object's sets (the object keeps it). For
	/// draws <paramref name="redrawn"/> every frame (<see cref="RedrawnEveryFrame"/>) one not made yet is made on the device's
	/// workers and its draws skipped meanwhile; those are kept apart from the ones other draws wait for.
	/// </summary>
	PipelineHandle Pipeline( ShaderVariant variant, Format color, Format depth, int samples, VertexStreamLayout layout, PrimitiveTopology topology, BindingLayoutHandle[] sets, bool ids = false, bool redrawn = false )
	{
		var tools = color == Format.Unknown ? ToolsPass.None : ToolsPass;
		var key = new PipelineKey( variant, color, depth, samples, layout, topology, ids, tools, redrawn );
		if ( sets is null && pipelines.TryGetValue( key, out var pipeline ) ) return pipeline;

		// Inputs the stream lacks (or every input, with no stream) read the default stream. No colour format draws into
		// depth alone. With instance ids, TEXCOORD13 reads them from a stream after the default one, per instance
		GraphicsPipelineInfo info;
		Format[] colors = color == Format.Unknown ? [] : [color];

		// Into depth alone, the depth bias is dynamic (DepthBias)
		var depthBias = color == Format.Unknown && depth != Format.Unknown;
		var idsStream = new VertexBufferLayout( sizeof( uint ), VertexStepRate.Instance );
		if ( layout is null )
		{
			var attributes = ShaderDefaults.MapVertices( variant.VertexInputs, input => ids && IsInstanceId( input ) ? new VertexAttribute( 0, 1, VertexFormat.UInt, 0 ) : null, defaultBuffer: 0 );
			info = variant.PipelineInfo( ids ? [new VertexBufferLayout( 0 ), idsStream] : [new VertexBufferLayout( 0 )], attributes, colors, depth, samples, depthBias, null );
		}
		else if ( variant.Expansion is { Expands: true } expansion )
		{
			// One instance per primitive, each stream stepping a primitive's worth of vertices per instance; an input reads its
			// vertex's field at its semantic index less 4 a corner. A corner inside the step (a list's) reads stream 0 past the
			// first vertex, so a device that checks vertex reads by whole steps finds the last primitive in a buffer of just its
			// vertices; one past it (a strip's) reads its own stream, 2 or 3, bound a vertex on (Draw). With instance ids,
			// TEXCOORD13 reads the first from the stream after those, which doesn't step
			var vertices = expansion.Step( topology );
			var step = new VertexBufferLayout( layout.Stride * vertices, VertexStepRate.Instance );
			var idStream = 1 + expansion.Corners;
			var attributes = ShaderDefaults.MapVertices( variant.VertexInputs, input =>
			{
				if ( ids && IsInstanceId( input ) ) return new VertexAttribute( 0, idStream, VertexFormat.UInt, 0 );
				var corner = expansion.Corner( input );
				if ( layout.Find( input.Semantic, expansion.SemanticIndex( input ) ) is not { } field || !SameKind( field, input ) ) return null;
				return corner < vertices ? new VertexAttribute( 0, 0, field.Format, field.Offset + corner * layout.Stride ) : new VertexAttribute( 0, 1 + corner, field.Format, field.Offset );
			}, defaultBuffer: 1 );
			VertexBufferLayout[] streams = expansion.Corners switch { 3 => [step, new( 0 ), step, step], 2 => [step, new( 0 ), step], _ => [step, new( 0 )] };
			if ( ids ) streams = [.. streams, new VertexBufferLayout( 0, VertexStepRate.Instance )];
			info = variant.PipelineInfo( streams, attributes, colors, depth, samples, depthBias, null );
			topology = expansion.Topology;
		}
		else
		{
			var attributes = ShaderDefaults.MapVertices( variant.VertexInputs, input =>
				ids && IsInstanceId( input ) ? new VertexAttribute( 0, 2, VertexFormat.UInt, 0 )
				: layout.Find( input ) is { } field && SameKind( field, input ) ? new VertexAttribute( 0, 0, Signed( field.Format, input ), field.Offset ) : null, defaultBuffer: 1 );
			info = variant.PipelineInfo( ids ? [new VertexBufferLayout( layout.Stride ), new VertexBufferLayout( 0 ), idsStream] : [new VertexBufferLayout( layout.Stride ), new VertexBufferLayout( 0 )], attributes, colors, depth, samples, depthBias, null );
		}

		info.Topology = topology;
		if ( sets is not null ) info.Sets = sets;
		if ( tools != ToolsPass.None ) ToolsViewStage.Apply( shaders, ref info, tools, ToolsViews.ShaderColor( variant.Shader?.Name ) );
		info.SkipUntilReady = redrawn;
		using ( Zones.PipelineCreate.Start() ) pipeline = gpu.CreateGraphicsPipeline( info );
		if ( sets is null ) pipelines[key] = pipeline;
		return pipeline;
	}

	// A field the shader reads as the other kind (integers as floats, or the other way round) reads the default stream, as a
	// model's draws do (BackendDevice.Pipeline)
	static bool SameKind( in VertexField field, in VertexInput input )
		=> field.Format.IsInteger() == (input.Kind != SpirvScalar.Float);

	// A 32-bit integer field takes the signedness the shader reads it with: the bits are the same
	static VertexFormat Signed( VertexFormat format, in VertexInput input ) => format switch
	{
		VertexFormat.UInt when input.Kind == SpirvScalar.Int => VertexFormat.Int,
		VertexFormat.Int when input.Kind == SpirvScalar.UInt => VertexFormat.UInt,
		_ => format,
	};

	/// <summary>
	/// The binding a reflected resource gets from a draw's attribute value, or for a material variable the material's
	/// texture (<see cref="Material"/>) or its placeholder, kept per pass slot of the layout.
	/// </summary>
	Binding Resource( in ShaderResource resource, object value, MaterialLayout layout = null, int set = 0, int slot = 0 )
	{
		switch ( resource.Kind )
		{
			case ShaderResourceKind.Texture:
				if ( resource.Variable?.Source == VariableSource.Material && Material is { } material ) return MaterialTexture( material, resource, layout, set, slot );
				if ( value is TextureHandle texture ) return Binding.Sampled( resource.Binding, texture, resource.Variable.RenderStateId == 1 && gpu.InfoOf( texture ).Format.HasSrgb() );
				if ( value is TextureViewHandle view ) return Binding.Sampled( resource.Binding, view );
				break;

			case ShaderResourceKind.Buffer when value is BufferHandle buffer:
				return Binding.Storage( resource.Binding, buffer );

			case ShaderResourceKind.StorageTexture:
				if ( value is TextureHandle storage ) return Binding.StorageTexture( resource.Binding, storage );
				if ( value is TextureViewHandle storageView ) return Binding.StorageTexture( resource.Binding, storageView );
				break;

			case ShaderResourceKind.Counter when value is BufferHandle counter:
				return Binding.Storage( resource.Binding, counter );
		}

		return layout is null ? defaults.For( resource ) : layout.Placeholder( set, slot );
	}

	/// <summary>
	/// The attribute value a resource binds: the one set under its name, or for a buffer's hidden counter, under the counter's
	/// name (<see cref="ShaderResource.CounterName"/>).
	/// </summary>
	object Value( in ShaderResource resource, ShaderAttributes attributes )
	{
		if ( resource.Variable is not { } variable ) return null;
		if ( resource.Kind != ShaderResourceKind.Counter ) return attributes.Get( variable.BindingId );
		var name = resource.Name;
		if ( !counterNames.TryGetValue( name, out var counter ) ) counterNames[name] = counter = ShaderResource.CounterName( name );
		return attributes.Get( counter );
	}

	readonly Dictionary<string, string> counterNames = new();

	void Bind( CommandList list, ShaderVariant variant, ShaderAttributes attributes, System.Numerics.Vector4 viewport )
	{
		Span<Binding> bindings = stackalloc Binding[64];
		for ( int set = 0; set < variant.Sets.Length; set++ )
		{
			if ( !variant.Sets[set].IsValid ) continue;

			var count = 0;
			foreach ( var resource in variant.ResourceSpan )
			{
				if ( resource.Set != set ) continue;
				if ( count == bindings.Length ) throw new InvalidOperationException( $"{variant} binds more than {bindings.Length} resources in set {set}" );

				var stage = set == ShaderAbi.FragmentSet ? variant.Pixel : variant.Compute ?? variant.Vertex;
				var value = Value( resource, attributes );
				bindings[count++] = resource.Kind switch
				{
					ShaderResourceKind.MaterialConstants => Constants( list, stage, resource.Binding, attributes, viewport ),
					ShaderResourceKind.ConstantBuffer when value is byte[] data => ConstantBuffer( list, resource.Binding, data, stage, resource.Name ),
					_ => Resource( resource, value ),
				};
			}

			list.SetBindings( set, variant.Sets[set], bindings[..count] );
		}
	}

	Binding Constants( CommandList list, ShaderStage stage, int binding, ShaderAttributes attributes, System.Numerics.Vector4 viewport )
	{
		var size = stage.MaterialBuffer.Size;
		var allocation = list.AllocateUpload( size, gpu.Capabilities.UniformAlignment );
		WriteConstants( allocation.Data[..size], stage, attributes, viewport );
		return Binding.Uniform( binding, allocation.Buffer, allocation.Offset, size );
	}

	/// <summary>
	/// A stage's material constants for a draw: the viewport, texture sizes and attributes over the material's values and the
	/// shader's defaults. What doesn't change from draw to draw (the material's values, features and defaults) is written once
	/// per stage and material version (<see cref="ConstantPlan"/>) and copied; only the draw's own variables are written.
	/// </summary>
	internal void WriteConstants( Span<byte> data, ShaderStage stage, ShaderAttributes attributes, System.Numerics.Vector4 viewport )
	{
		var material = Material;
		var plan = PlanFor( stage, material );
		var stable = attributes.Stable;

		// The same attributes, viewport and plan as the last write: its bytes again
		if ( stable && plan.Image is { } image && image.Length == data.Length && ReferenceEquals( plan.ImageAttributes, attributes ) && plan.ImageAttributesVersion == attributes.ValuesVersion
			&& plan.ImageVersion == plan.Version && ReferenceEquals( plan.ImageFeatures, plan.Features ) && plan.ImageViewport == viewport )
		{
			image.CopyTo( data );
			return;
		}

		// Written here and copied out once: data is mapped upload memory, which is write-combined and very slow to read back
		if ( plan.Image is null || plan.Image.Length != data.Length ) plan.Image = new byte[data.Length];
		var written = plan.Image.AsSpan();
		plan.ImageAttributes = null;

		plan.Base.AsSpan( 0, Math.Min( plan.Base.Length, written.Length ) ).CopyTo( written );
		if ( written.Length > plan.Base.Length ) written[plan.Base.Length..].Clear();

		var features = material?.Features ?? default;
		var cacheable = stable;
		foreach ( var index in plan.PerDraw )
		{
			var variable = stage.MaterialConstants[index].Variable;
			float[] given;
			switch ( variable.Source )
			{
				case VariableSource.Viewport:
					given = scratch4;
					(given[0], given[1], given[2], given[3]) = (viewport.X, viewport.Y, viewport.Z, viewport.W);
					break;
				case VariableSource.InvViewportSize:
					given = scratch2;
					(given[0], given[1]) = (1 / viewport.Z, 1 / viewport.W);
					break;
				case VariableSource.Attribute:
					given = string.IsNullOrEmpty( variable.Attribute ) ? null : attributes.Get( variable.BindingId ) as float[];
					break;
				default:
					// A texture's size can change with no change to the attributes
					cacheable = false;
					given = TextureSize( stage, variable, attributes );
					break;
			}

			stage.WriteMaterialConstant( written, index, given, features );
		}

		written.CopyTo( data );
		if ( !cacheable ) return;
		(plan.ImageAttributes, plan.ImageAttributesVersion, plan.ImageVersion, plan.ImageFeatures, plan.ImageViewport) = (attributes, attributes.ValuesVersion, plan.Version, plan.Features, viewport);
	}

	readonly float[] scratch4 = new float[4], scratch2 = new float[2], scratch1 = new float[1];

	/// <summary>
	/// A stage's material constants with a material, apart from the draw's own variables (<see cref="PerDraw"/>, left at their
	/// defaults in <see cref="Base"/>), for one version of the material.
	/// </summary>
	sealed class ConstantPlan
	{
		public byte[] Base;
		public int[] PerDraw;
		public int Version;
		public int[] Features;
		public ShaderStage Stage;
		public BackendMaterial Material;

		// What the last write made, and from what, where the draw's variables came from stable attributes
		public byte[] Image;
		public ShaderAttributes ImageAttributes;
		public int ImageAttributesVersion;
		public int ImageVersion;
		public int[] ImageFeatures;
		public System.Numerics.Vector4 ImageViewport;
	}

	static bool DrawnPerDraw( VariableSource source ) => source is VariableSource.Viewport or VariableSource.InvViewportSize or VariableSource.Attribute
		or VariableSource.TextureDimensions or VariableSource.InvTextureDimensions or VariableSource.TextureDimensionsLog2;

	// Plans by stage and material, kept as long as the drawer is (and dropped when there are many)
	readonly Dictionary<(ShaderStage Stage, BackendMaterial Material), ConstantPlan> planCache = new( new PlanKeyComparer() );

	sealed class PlanKeyComparer : IEqualityComparer<(ShaderStage Stage, BackendMaterial Material)>
	{
		public bool Equals( (ShaderStage Stage, BackendMaterial Material) a, (ShaderStage Stage, BackendMaterial Material) b ) => ReferenceEquals( a.Stage, b.Stage ) && ReferenceEquals( a.Material, b.Material );
		public int GetHashCode( (ShaderStage Stage, BackendMaterial Material) key ) => RuntimeHelpers.GetHashCode( key.Stage ) * 31 + RuntimeHelpers.GetHashCode( key.Material );
	}

	readonly ConstantPlan[] recentPlans = new ConstantPlan[4];
	int recentPlansNext;

	ConstantPlan PlanFor( ShaderStage stage, BackendMaterial material )
	{
		var version = material?.Version ?? 0;
		var features = material?.Features;
		foreach ( var recent in recentPlans )
		{
			if ( recent is not null && ReferenceEquals( recent.Stage, stage ) && ReferenceEquals( recent.Material, material ) && recent.Version == version && ReferenceEquals( recent.Features, features ) ) return recent;
		}

		if ( !planCache.TryGetValue( (stage, material), out var plan ) || plan.Version != version || !ReferenceEquals( plan.Features, features ) )
		{
			plan = FindPlan( stage, material );
			// Generous: a map's foliage alone can be a thousand stage and material pairs, and a cleared cache rebuilds them all
			if ( planCache.Count >= 8192 ) planCache.Clear();
			planCache[(stage, material)] = plan;
		}

		(plan.Stage, plan.Material) = (stage, material);
		recentPlans[recentPlansNext] = plan;
		recentPlansNext = (recentPlansNext + 1) & 3;
		return plan;
	}

	// Plans by stage, for no material and for each material (kept as long as the material is)
	readonly Dictionary<ShaderStage, ConstantPlan> plainPlans = new();
	readonly System.Runtime.CompilerServices.ConditionalWeakTable<BackendMaterial, Dictionary<ShaderStage, ConstantPlan>> materialPlans = new();

	ConstantPlan FindPlan( ShaderStage stage, BackendMaterial material )
	{
		var plans = material is null ? plainPlans : materialPlans.GetOrCreateValue( material );
		var version = material?.Version ?? 0;
		var features = material?.Features;
		if ( plans.TryGetValue( stage, out var plan ) && plan.Version == version && ReferenceEquals( plan.Features, features ) ) return plan;

		// A material whose values change every frame writes its plan again in place: no new arrays
		if ( plan is null )
		{
			plan = new ConstantPlan { Base = new byte[stage.MaterialBuffer.Size] };
			var perDraw = new List<int>();
			for ( int i = 0; i < stage.MaterialConstants.Count; i++ )
			{
				if ( DrawnPerDraw( stage.MaterialConstants[i].Variable.Source ) ) perDraw.Add( i );
			}

			plan.PerDraw = [.. perDraw];
		}

		plan.Version = version;
		plan.Features = features;
		plan.Base.AsSpan().Clear();
		for ( int i = 0; i < stage.MaterialConstants.Count; i++ )
		{
			var variable = stage.MaterialConstants[i].Variable;
			float[] given = null;
			if ( variable.Source is VariableSource.Material or VariableSource.MaterialAndExpression ) given = material?.Value( variable.Name );
			else if ( variable.Source == VariableSource.ShaderVersion && material is not null )
			{
				given = scratch1;
				given[0] = material.ShaderVersion;
			}

			stage.WriteMaterialConstant( plan.Base, i, given, features ?? default );
		}
		plans[stage] = plan;
		return plan;
	}

	/// <summary>
	/// The size of the texture a <c>Source( TextureDim )</c>-style variable names with <c>SourceArg</c>: (width, height,
	/// depth, mips), its reciprocal, or its log2. Only x and y are read by the shaders drawn so far.
	/// </summary>
	float[] TextureSize( ShaderStage stage, ShaderVariable variable, ShaderAttributes attributes )
	{
		var table = stage.Program.Variables;
		if ( variable.SourceIndex < 0 || variable.SourceIndex >= table.Length ) return null;

		// The draw's attribute, or the material's texture: a texture the material sets (The Festering's dither matrix)
		var argument = table[variable.SourceIndex];
		var texture = attributes.Get( argument.BindingId ) as TextureHandle?;
		if ( texture is null && Material?.Textures is { } textures && textures.TryGetValue( argument.Name, out var materialTexture ) ) texture = materialTexture?.Handle;
		if ( texture is not { IsValid: true } handle ) return null;

		var info = gpu.InfoOf( handle );
		float w = info.Width, h = info.Height, d = Math.Max( info.Depth, 1 ), m = info.Mips;

		// Into the drawer's scratch, read before the next constant is written
		var size = scratch4;
		(size[0], size[1], size[2], size[3]) = variable.Source switch
		{
			VariableSource.InvTextureDimensions => (1 / w, 1 / h, 1 / d, 1 / m),
			VariableSource.TextureDimensionsLog2 => (MathF.Log2( w ), MathF.Log2( h ), MathF.Log2( d ), m),
			_ => (w, h, d, m),
		};
		return size;
	}

	Binding ConstantBuffer( CommandList list, int binding, byte[] data, ShaderStage stage, string name )
	{
		HandFilledConstants.CheckSize( stage, name, data.Length );
		var size = (data.Length + 15) & ~15;
		var allocation = list.AllocateUpload( size, gpu.Capabilities.UniformAlignment );
		allocation.Data.Clear();
		data.CopyTo( allocation.Data );
		return Binding.Uniform( binding, allocation.Buffer, allocation.Offset, size );
	}

	/// <summary>
	/// A draw's attributes as a material object's pass bindings: every named resource by name, and the material
	/// constants and constant buffers written into the frame's pass constants (or kept by content past the device's
	/// dynamic limit).
	/// </summary>
	sealed class AttributeBindings( ShaderDrawer drawer ) : PassBindings
	{
		public ShaderAttributes Attributes;
		public System.Numerics.Vector4 Viewport;

		public override float[] Floats( string attribute ) => Attributes.Get( attribute ) as float[];

		public override Binding Resolve( in ShaderResource resource, ShaderStage stage, bool dynamic, out uint dynamicOffset ) => Resolve( null, 0, 0, resource, stage, dynamic, out dynamicOffset );

		public override Binding Resolve( MaterialLayout layout, int set, int slot, in ShaderResource resource, ShaderStage stage, bool dynamic, out uint dynamicOffset )
		{
			dynamicOffset = 0;
			var value = drawer.Value( resource, Attributes );
			switch ( resource.Kind )
			{
				case ShaderResourceKind.MaterialConstants:
					{
						var size = stage.MaterialBuffer.Size;
						Span<byte> target = dynamic ? Space( size, out dynamicOffset ) : new byte[size];
						drawer.WriteConstants( target, stage, Attributes, Viewport );
						return Constants( resource, target, size, dynamic );
					}

				case ShaderResourceKind.ConstantBuffer when value is byte[] data:
					{
						HandFilledConstants.CheckSize( stage, resource.Name, data.Length );
						var size = (data.Length + 15) & ~15;
						Span<byte> target = dynamic ? Space( size, out dynamicOffset ) : new byte[size];
						target.Clear();
						data.CopyTo( target );
						return Constants( resource, target, size, dynamic );
					}

				case ShaderResourceKind.ConstantBuffer:
					{
						var placeholder = layout is null ? drawer.defaults.For( resource ) : layout.Placeholder( set, slot );
						if ( dynamic ) placeholder.Type = BindingType.UniformBufferDynamic;
						return placeholder;
					}

				default:
					return drawer.Resource( resource, value, layout, set, slot );
			}
		}

		Span<byte> Space( int size, out uint offset )
		{
			var start = drawer.cache.TryAllocatePassConstants( size, out var data );
			if ( start < 0 ) throw new InvalidOperationException( "Pass constant region is full" );
			offset = (uint)start;
			return data;
		}

		Binding Constants( in ShaderResource resource, ReadOnlySpan<byte> data, int size, bool dynamic )
		{
			var cache = drawer.cache;
			if ( dynamic ) return new Binding { Slot = resource.Binding, Type = BindingType.UniformBufferDynamic, Buffer = cache.PassBuffer, Size = size };
			return Binding.Uniform( resource.Binding, cache.MaterialBuffer, cache.KeepPassConstants( data, size ), size );
		}
	}
}
