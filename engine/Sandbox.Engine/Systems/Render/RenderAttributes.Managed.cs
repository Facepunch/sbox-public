using System.Runtime.InteropServices;

namespace Sandbox;

public partial class RenderAttributes
{
	/// <summary>
	/// The values of an attribute set kept in managed code, for a backend that isn't Source 2's
	/// (<see cref="RenderBackend.BeginBlock"/>). Null for a native set, which is every set on Source 2.
	/// </summary>
	internal ManagedRenderAttributes Managed { get; private set; }

	/// <summary>
	/// Whether this set keeps its values in managed code rather than in a native <c>CRenderAttributes</c>.
	/// </summary>
	internal bool IsManaged => Managed is not null;

	/// <summary>
	/// A managed copy of a native set's values, kept when the process draws on a backend that lowers attributes itself
	/// (<see cref="KeepManagedCopy"/>): a scene object's under <c>-borealisgpu</c>, which a custom object merges into the
	/// attributes it draws with and a model's draws read as its object attributes. Null otherwise, and always for a set that is <see cref="Managed"/>.
	/// </summary>
	internal ManagedRenderAttributes Shadow { get; private set; }

	/// <summary>
	/// Keep a managed copy of what's set on this native set from now on (<see cref="Shadow"/>), so it merges into managed sets.
	/// </summary>
	internal void KeepManagedCopy()
	{
		if ( Managed is null ) Shadow ??= new ManagedRenderAttributes();
	}

	/// <summary>
	/// <see cref="Shadow"/> as an attribute set of its own, to draw with on a backend that lowers attributes itself (a dynamic
	/// object's on Borealis.Gpu). Null without a managed copy.
	/// </summary>
	internal RenderAttributes ShadowSet => Shadow is null ? null : shadowSet ??= new RenderAttributes( Shadow );

	/// <summary>The values a backend that lowers attributes itself reads: a managed set's own, or a native set's copy. Null for a native set without one.</summary>
	internal ManagedRenderAttributes ManagedValues => Managed ?? Shadow;

	/// <summary>This set if it's managed, else <see cref="ShadowSet"/>: the set to draw with on a backend that lowers attributes itself.</summary>
	internal RenderAttributes ManagedSet => Managed is not null ? this : ShadowSet;

	RenderAttributes shadowSet;

	RenderAttributes( ManagedRenderAttributes managed )
	{
		Managed = managed;
	}

	/// <summary>
	/// An attribute set with no native side: what a render block on a backend other than Source 2's holds.
	/// </summary>
	internal static RenderAttributes CreateManaged() => new( new ManagedRenderAttributes() );

	/// <summary>
	/// A constant buffer's bytes as <see cref="SetData{T}(in StringToken, T[])"/> sets them, without the copy on a managed set:
	/// the caller keeps <paramref name="data"/> unchanged while the set is drawn with (a Borealis.Gpu frame's view constants,
	/// which live as long as its block). A native set takes a copy as usual.
	/// </summary>
	internal void SetDataShared( in StringToken k, byte[] data )
	{
		if ( Managed is not null ) Managed.SetShared( k, data );
		else SetData( k, data );
	}

	/// <summary>
	/// An attribute set for the device being recorded for (<see cref="Graphics.SetRecordingBackend"/>, else the
	/// process's): native on Source 2's, as <c>new RenderAttributes()</c> makes, managed on another, so a recording for
	/// that device (a post process's command list under <c>-borealisgpu</c>) can draw with it.
	/// </summary>
	internal static RenderAttributes ForRecording() => Graphics.Backend is Source2RenderBackend ? new RenderAttributes() : CreateManaged();
}

/// <summary>
/// Render attribute values by token, as a backend that lowers them itself reads them: numbers and vectors as floats
/// (a matrix as 16, in memory order: M11, M12 ... M44), textures with a mip, buffers, samplers, constant buffer bytes,
/// and combos. Setting a value again reuses its array (and a cleared value's), so a set filled every frame allocates
/// nothing in steady state: readers read the values they're handed before they're set again, and keep none.
/// </summary>
internal sealed class ManagedRenderAttributes
{
	/// <summary>A texture and the mip bound (-1 for all).</summary>
	public readonly record struct TextureValue( Texture Texture, int Mip );

	/// <summary>A value or combo and the <see cref="Version"/> it was last set at.</summary>
	public struct Entry<T>
	{
		public T Value;
		public int Stamp;
	}

	readonly Dictionary<uint, Entry<object>> values = new();
	readonly Dictionary<uint, Entry<int>> combos = new();

	// Arrays and texture boxes that cleared or replaced values left, by token, reused when the token is set again; boxes
	// (which hold their texture) only until the next clear
	readonly Dictionary<uint, object> spare = new();

	// Constant buffer bytes set without a copy (SetShared): the caller's, never written or reused here
	readonly HashSet<uint> borrowed = new();

	/// <summary>Every value by token: <c>float[]</c>, <see cref="TextureValue"/>, <see cref="GpuBuffer"/>, <c>SamplerState</c>, <c>byte[]</c> or a string.</summary>
	public Entries<object> Values => new( values, int.MinValue );

	/// <summary>Every combo by token.</summary>
	public Entries<int> Combos => new( combos, int.MinValue );

	/// <summary>The values set after <see cref="Version"/> was <paramref name="version"/>.</summary>
	public Entries<object> ValuesSince( int version ) => new( values, version );

	/// <summary>The combos set after <see cref="Version"/> was <paramref name="version"/>.</summary>
	public Entries<int> CombosSince( int version ) => new( combos, version );

	/// <summary>Changes whenever a value or combo is set or cleared, for a reader that converts them only when they change.</summary>
	public int Version { get; private set; }

	/// <summary>
	/// The <see cref="Version"/> at which a value or combo last went (a clear, a null object): a reader that copies only what
	/// changed (<see cref="ValuesSince"/>) copies everything again when this is past its version.
	/// </summary>
	public int RemovedAt { get; private set; }

	public void Set( StringToken k, params ReadOnlySpan<float> value )
	{
		var array = Array<float>( k.Value, value.Length );
		value.CopyTo( array );
		Put( k.Value, array, ++Version );
		Own( k.Value );
	}

	public void Set( StringToken k, in Matrix value ) => Set( k, MemoryMarshal.Cast<Matrix, float>( new ReadOnlySpan<Matrix>( in value ) ) );

	/// <summary>A constant buffer's bytes, copied.</summary>
	public void SetBytes( StringToken k, ReadOnlySpan<byte> data )
	{
		var array = Array<byte>( k.Value, data.Length );
		data.CopyTo( array );
		Put( k.Value, array, ++Version );
		Own( k.Value );
	}

	/// <summary>A constant buffer's bytes without a copy: the caller keeps them unchanged while the set is drawn with.</summary>
	public void SetShared( StringToken k, byte[] data )
	{
		Put( k.Value, data, ++Version );
		borrowed.Add( k.Value );
	}

	/// <summary>A texture and mip; null clears it. The same texture and mip as before keeps the value boxed then.</summary>
	public void SetTexture( StringToken k, Texture texture, int mip )
	{
		if ( texture is null )
		{
			SetObject( k, null );
			return;
		}

		var value = new TextureValue( texture, mip );
		object boxed;
		if ( values.TryGetValue( k.Value, out var current ) && current.Value is TextureValue now && now == value ) boxed = current.Value;
		else if ( spare.Remove( k.Value, out var old ) && old is TextureValue left && left == value ) boxed = old;
		else if ( mip < 0 ) boxed = WholeTextures.GetValue( texture, static t => new TextureValue( t, -1 ) );
		else boxed = MipValue( texture, mip );

		Put( k.Value, boxed, ++Version );
		Own( k.Value );
	}

	// Each texture's whole-texture value boxed once and shared by every set (boxes aren't changed), so a set that is new or was
	// cleared (a pooled command list's) boxes nothing for it
	static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Texture, object> WholeTextures = new();

	// And each of its mips' values, for passes that bind a mip of textures they swap between every frame (history targets)
	static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Texture, object[]> MipTextures = new();

	static object MipValue( Texture texture, int mip )
	{
		if ( mip >= 32 ) return new TextureValue( texture, mip );

		var mips = MipTextures.GetValue( texture, static _ => new object[32] );
		return System.Threading.Volatile.Read( ref mips[mip] ) ?? System.Threading.Interlocked.CompareExchange( ref mips[mip], new TextureValue( texture, mip ), null ) ?? mips[mip];
	}

	public void SetObject( StringToken k, object value )
	{
		if ( value is null )
		{
			if ( values.Remove( k.Value, out var old ) ) Keep( k.Value, old.Value );
			Own( k.Value );
			RemovedAt = ++Version;
			return;
		}

		Put( k.Value, value, ++Version );

		// Bytes from outside stay the caller's
		if ( value is byte[] ) borrowed.Add( k.Value );
		else Own( k.Value );
	}

	/// <summary>Whether nothing is set.</summary>
	public bool IsEmpty => values.Count == 0 && combos.Count == 0;

	/// <summary>How many values are set.</summary>
	public int ValueCount => values.Count;

	/// <summary>
	/// Whether <paramref name="other"/> holds the same values and combos, as <c>CRenderAttributes::AreEqual</c> compares two
	/// sets: numbers and bytes by value, textures, buffers and samplers by what they are.
	/// </summary>
	public bool SameValues( ManagedRenderAttributes other )
	{
		if ( ReferenceEquals( this, other ) ) return true;
		if ( other is null || values.Count != other.values.Count || combos.Count != other.combos.Count ) return false;

		foreach ( var (k, entry) in combos )
		{
			if ( !other.combos.TryGetValue( k, out var theirs ) || theirs.Value != entry.Value ) return false;
		}

		foreach ( var (k, entry) in values )
		{
			if ( !other.values.TryGetValue( k, out var theirs ) ) return false;
			var same = (entry.Value, theirs.Value) switch
			{
				(float[] a, float[] b ) => a.AsSpan().SequenceEqual( b ),
				(byte[] a, byte[] b ) => a.AsSpan().SequenceEqual( b ),
				var (a, b) => Equals( a, b ),
			};
			if ( !same ) return false;
		}

		return true;
	}

	public void SetCombo( StringToken k, int value )
	{
		combos[k.Value] = new Entry<int> { Value = value, Stamp = ++Version };
	}

	public object Get( StringToken k ) => values.TryGetValue( k.Value, out var entry ) ? entry.Value : null;
	public int? GetCombo( StringToken k ) => combos.TryGetValue( k.Value, out var entry ) ? entry.Value : null;

	public float[] GetFloats( StringToken k ) => Get( k ) as float[];

	public void Clear()
	{
		// Boxes left at the last clear and not taken since go, so they don't keep their textures
		foreach ( var (k, old) in spare )
		{
			if ( old is TextureValue ) spare.Remove( k );
		}

		foreach ( var (k, entry) in values ) Keep( k, entry.Value );
		values.Clear();
		combos.Clear();
		borrowed.Clear();
		RemovedAt = ++Version;
	}

	/// <summary>Copy every value and combo into <paramref name="other"/>, replacing those of the same token.</summary>
	public void MergeTo( ManagedRenderAttributes other )
	{
		if ( ReferenceEquals( other, this ) ) return;

		var stamp = ++other.Version;
		foreach ( var (k, entry) in values )
		{
			// Arrays go into the other set's own, so setting them again here doesn't change them there
			switch ( entry.Value )
			{
				case float[] floats:
					other.Put( k, other.Copy( k, floats ), stamp );
					other.Own( k );
					break;
				case byte[] bytes when borrowed.Contains( k ):
					other.Put( k, bytes, stamp );
					other.borrowed.Add( k );
					break;
				case byte[] bytes:
					other.Put( k, other.Copy( k, bytes ), stamp );
					other.Own( k );
					break;
				default:
					other.Put( k, entry.Value, stamp );
					other.Own( k );
					break;
			}
		}

		foreach ( var (k, entry) in combos ) other.combos[k] = new Entry<int> { Value = entry.Value, Stamp = stamp };
	}

	T[] Copy<T>( uint k, T[] source )
	{
		var array = Array<T>( k, source.Length );
		source.AsSpan().CopyTo( array );
		return array;
	}

	// An array of this length for k's value that only this set holds: its value's, else one a cleared value left, else new
	T[] Array<T>( uint k, int length )
	{
		if ( values.TryGetValue( k, out var current ) && current.Value is T[] array && array.Length == length && Owns( k, array ) ) return array;
		if ( spare.Remove( k, out var old ) && old is T[] kept && kept.Length == length ) return kept;
		return length == 0 ? [] : new T[length];
	}

	void Put( uint k, object value, int stamp )
	{
		ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault( values, k, out var existed );
		if ( existed && !ReferenceEquals( entry.Value, value ) ) Keep( k, entry.Value );
		entry.Value = value;
		entry.Stamp = stamp;
	}

	// A value this set made (numbers' and copied bytes' arrays, texture boxes) waits to be reused for its token
	void Keep( uint k, object old )
	{
		if ( old is float[] or TextureValue || (old is byte[] && !borrowed.Contains( k )) ) spare[k] = old;
	}

	bool Owns( uint k, object value ) => value is not byte[] || !borrowed.Contains( k );

	void Own( uint k )
	{
		if ( borrowed.Count > 0 ) borrowed.Remove( k );
	}

	/// <summary>Values or combos by token, those set after a version, enumerated without allocating.</summary>
	public readonly struct Entries<T>( Dictionary<uint, Entry<T>> map, int since )
	{
		public Enumerator GetEnumerator() => new( map.GetEnumerator(), since );

		public struct Enumerator( Dictionary<uint, Entry<T>>.Enumerator inner, int since )
		{
			Dictionary<uint, Entry<T>>.Enumerator inner = inner;

			public KeyValuePair<uint, T> Current => new( inner.Current.Key, inner.Current.Value.Value );

			public bool MoveNext()
			{
				while ( inner.MoveNext() )
				{
					if ( inner.Current.Value.Stamp > since ) return true;
				}

				return false;
			}
		}
	}
}
