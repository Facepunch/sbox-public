using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

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

	/// <summary>Whether nothing is set: the managed values where there are any (on Borealis.Gpu there's no native set), else the native set's.</summary>
	internal bool IsEmpty => ManagedValues is { } values ? values.IsEmpty : !attributes.IsValid || attributes.IsEmpty();

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
	/// An attribute set for the device being recorded for: managed, so a recording (a post process's command list) can draw
	/// with it.
	/// </summary>
	internal static RenderAttributes ForRecording() => CreateManaged();
}

/// <summary>
/// Render attribute values by token: a backend's numeric inputs as float arrays (a matrix as 16, in memory order),
/// exact integral scalars for managed reads, textures with a mip, buffers, samplers, constant buffer bytes, and combos.
/// Setting a value again reuses its array (and a cleared value's), so a set filled every frame allocates nothing in
/// steady state: readers read the values they're handed before they're set again, and keep none.
/// </summary>
internal sealed class ManagedRenderAttributes
{
	// Exact type checks: a pattern over arrays goes through a cast helper per value, as an array of another element type may convert
	static bool IsFloats( object value ) => value is not null && value.GetType() == typeof( float[] );
	static bool IsBytes( object value ) => value is not null && value.GetType() == typeof( byte[] );

	/// <summary>A texture and the mip bound (-1 for all).</summary>
	public readonly record struct TextureValue( Texture Texture, int Mip );

	/// <summary>A value or combo and the <see cref="Version"/> it was last set at.</summary>
	public struct Entry<T>
	{
		public T Value;
		public int Stamp;
		// Only scalar integer attributes carry exact bits. Value is still the float array used by existing shader lowering.
		public uint? IntegerBits;
	}

	/// <summary>The values and combos, and what reuses their arrays.</summary>
	sealed class Store
	{
		public readonly Dictionary<uint, Entry<object>> Values = new();
		public readonly Dictionary<uint, Entry<int>> Combos = new();

		// Arrays that cleared or replaced values left, by token, reused when the token is set again. Not texture boxes: they're
		// shared per texture (WholeTextures, MipValue), and one kept here would hold its texture past the set's clear
		public readonly Dictionary<uint, object> Spare = new();

		// Constant buffer bytes set without a copy (SetShared): the caller's, never written or reused here
		public readonly HashSet<uint> Borrowed = new();

		public int Version, RemovedAt;

		/// <summary>
		/// Become a copy of <paramref name="other"/>: its numbers and copied bytes into arrays of this store's own (reused by token,
		/// so a set copied every frame allocates nothing once warm), what's shared (boxes, buffers, borrowed bytes) as it is.
		/// </summary>
		public void CopyFrom( Store other )
		{
			foreach ( var (k, entry) in Values ) Keep( k, entry.Value );
			Values.Clear();
			Borrowed.Clear();
			foreach ( var (k, entry) in other.Values )
			{
				var value = entry.Value;
				if ( IsFloats( value ) ) value = Copy( k, Unsafe.As<float[]>( value ) );
				else if ( IsBytes( value ) )
				{
					if ( !other.Borrowed.Contains( k ) ) value = Copy( k, Unsafe.As<byte[]>( value ) );
					else Borrowed.Add( k );
				}
				Values[k] = new Entry<object> { Value = value, Stamp = entry.Stamp, IntegerBits = entry.IntegerBits };
			}

			Combos.Clear();
			foreach ( var (k, entry) in other.Combos ) Combos[k] = entry;
			Version = other.Version;
			RemovedAt = other.RemovedAt;
		}

		public T[] Copy<T>( uint k, T[] source )
		{
			var array = Array<T>( k, source.Length );
			source.AsSpan().CopyTo( array );
			return array;
		}

		// An array of this length for k's value that only this store holds: its value's, else one a cleared value left, else new
		public T[] Array<T>( uint k, int length )
		{
			if ( Values.TryGetValue( k, out var current ) && current.Value is { } value && value.GetType() == typeof( T[] ) && Unsafe.As<T[]>( value ) is var array && array.Length == length && Owns( k, array ) ) return array;
			if ( Spare.Remove( k, out var old ) && old.GetType() == typeof( T[] ) && Unsafe.As<T[]>( old ) is var kept && kept.Length == length ) return kept;
			return length == 0 ? [] : new T[length];
		}

		public void Put( uint k, object value, int stamp, uint? integerBits = null )
		{
			ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault( Values, k, out var existed );
			if ( existed && !ReferenceEquals( entry.Value, value ) ) Keep( k, entry.Value );
			entry.Value = value;
			entry.Stamp = stamp;
			entry.IntegerBits = integerBits;
		}

		// An array this store made (numbers', copied bytes') waits to be reused for its token
		public void Keep( uint k, object old )
		{
			if ( IsFloats( old ) || (IsBytes( old ) && !Borrowed.Contains( k )) ) Spare[k] = old;
		}

		bool Owns( uint k, object value ) => !IsBytes( value ) || !Borrowed.Contains( k );

		public void Own( uint k )
		{
			if ( Borrowed.Count > 0 ) Borrowed.Remove( k );
		}
	}

	// What the main thread writes, and what the render thread's frame reads while the main thread writes the next one's (Writing)
	Store current = new(), held;
	long splitEpoch = -1;

	/// <summary>
	/// The store a read on this thread reads: on the render thread, the set as its frame was handed over (the held copy once the
	/// main thread has written since: <see cref="Writing"/>); anywhere else, what's written now.
	/// </summary>
	Store Reading
	{
		get
		{
			var now = Volatile.Read( ref current );
			if ( !ThreadSafe.IsRenderThread ) return now;
			return Volatile.Read( ref splitEpoch ) == Rendering.RenderSync.Epoch ? held : now;
		}
	}

	/// <summary>
	/// Whether the set is written only while no frame reads it, between the render thread's frames (a game drawing's
	/// recording, written in the extract and read by the frame it hands over): its writes never need a copy for a frame.
	/// </summary>
	internal bool BetweenFrames { get; set; }

	/// <summary>
	/// What a backend lowered this set into, kept with it to use again while its values don't change (Borealis.Gpu's lowered
	/// sets for immediate draws and dispatches). Read and written only by the thread recording with the set.
	/// </summary>
	internal object Lowered;

	/// <summary>
	/// The store a write goes into. While a frame handed over may be drawing on the render thread (<see cref="Rendering.RenderSync.Overlapping"/>),
	/// the main thread's first write since the hand-over leaves the store as it was for that frame and goes on in a copy, so the
	/// frame reads every value as it was handed over, as it reads them on the main thread before the next update writes them. A
	/// set nobody writes isn't copied, nor anything in serial mode or on the render thread.
	/// </summary>
	Store Writing()
	{
		if ( !Rendering.RenderSync.Overlapping || ThreadSafe.IsRenderThread || BetweenFrames ) return current;
		var epoch = Rendering.RenderSync.Epoch;
		if ( splitEpoch == epoch ) return current;

		// The other store isn't read in this frame: the render thread reads the one current until the split, and held after
		var old = current;
		var fresh = held ?? new Store();
		fresh.CopyFrom( old );
		held = old;
		Volatile.Write( ref splitEpoch, epoch );
		Volatile.Write( ref current, fresh );
		Rendering.RenderSync.CountAttributeSplit();
		return fresh;
	}

	/// <summary>Every value by token: <c>float[]</c>, <see cref="TextureValue"/>, <see cref="GpuBuffer"/>, <c>SamplerState</c>, <c>byte[]</c> or a string.</summary>
	public Entries<object> Values => new( Reading.Values, int.MinValue );

	/// <summary>Every combo by token.</summary>
	public Entries<int> Combos => new( Reading.Combos, int.MinValue );

	/// <summary>The values set after <see cref="Version"/> was <paramref name="version"/>.</summary>
	public Entries<object> ValuesSince( int version ) => new( Reading.Values, version );

	/// <summary>The combos set after <see cref="Version"/> was <paramref name="version"/>.</summary>
	public Entries<int> CombosSince( int version ) => new( Reading.Combos, version );

	/// <summary>Changes whenever a value or combo is set or cleared, for a reader that converts them only when they change.</summary>
	public int Version => Reading.Version;

	/// <summary>
	/// The <see cref="Version"/> at which a value or combo last went (a clear, a null object): a reader that copies only what
	/// changed (<see cref="ValuesSince"/>) copies everything again when this is past its version.
	/// </summary>
	public int RemovedAt => Reading.RemovedAt;

	public void Set( StringToken k, params ReadOnlySpan<float> value )
	{
		var store = Writing();
		// The same numbers again change nothing, so a reader that keeps what it made from the set at a version finds it still good
		if ( store.Values.TryGetValue( k.Value, out var held ) && held.IntegerBits is null && IsFloats( held.Value ) && Unsafe.As<float[]>( held.Value ).AsSpan().SequenceEqual( value ) ) return;
		var array = store.Array<float>( k.Value, value.Length );
		value.CopyTo( array );
		store.Put( k.Value, array, ++store.Version );
		store.Own( k.Value );
	}

	public void Set( StringToken k, in Matrix value ) => Set( k, MemoryMarshal.Cast<Matrix, float>( new ReadOnlySpan<Matrix>( in value ) ) );

	public void SetInteger( StringToken k, int value ) => SetInteger( k, unchecked((uint)value), value );
	public void SetInteger( StringToken k, uint value ) => SetInteger( k, value, value );

	void SetInteger( StringToken k, uint bits, float projection )
	{
		var store = Writing();
		if ( store.Values.TryGetValue( k.Value, out var held ) && held.IntegerBits == bits && Unsafe.As<float[]>( held.Value )[0] == projection ) return;
		var array = store.Array<float>( k.Value, 1 );
		array[0] = projection;
		store.Put( k.Value, array, ++store.Version, bits );
		store.Own( k.Value );
	}

	/// <summary>A constant buffer's bytes, copied.</summary>
	public void SetBytes( StringToken k, ReadOnlySpan<byte> data )
	{
		var store = Writing();
		var array = store.Array<byte>( k.Value, data.Length );
		data.CopyTo( array );
		store.Put( k.Value, array, ++store.Version );
		store.Own( k.Value );
	}

	/// <summary>A constant buffer's bytes without a copy: the caller keeps them unchanged while the set is drawn with.</summary>
	public void SetShared( StringToken k, byte[] data )
	{
		var store = Writing();
		store.Put( k.Value, data, ++store.Version );
		store.Borrowed.Add( k.Value );
	}

	/// <summary>A texture and mip; null clears it. The same texture and mip as before keeps the value boxed then.</summary>
	public void SetTexture( StringToken k, Texture texture, int mip )
	{
		if ( texture is null )
		{
			SetObject( k, null );
			return;
		}

		var store = Writing();
		var value = new TextureValue( texture, mip );
		if ( store.Values.TryGetValue( k.Value, out var current ) && current.Value is TextureValue now && now == value ) return;
		var boxed = mip < 0 ? WholeTextures.GetValue( texture, static t => new TextureValue( t, -1 ) ) : MipValue( texture, mip );

		store.Put( k.Value, boxed, ++store.Version );
		store.Own( k.Value );
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
		var store = Writing();
		// The same object again (not an array or bytes, which their owner may have changed in place) changes nothing
		if ( value is not null and not Array && store.Values.TryGetValue( k.Value, out var held ) && ReferenceEquals( held.Value, value ) ) return;
		if ( value is null )
		{
			if ( store.Values.Remove( k.Value, out var old ) ) store.Keep( k.Value, old.Value );
			store.Own( k.Value );
			store.RemovedAt = ++store.Version;
			return;
		}

		store.Put( k.Value, value, ++store.Version );

		// Bytes from outside stay the caller's
		if ( IsBytes( value ) ) store.Borrowed.Add( k.Value );
		else store.Own( k.Value );
	}

	/// <summary>Whether nothing is set.</summary>
	public bool IsEmpty
	{
		get
		{
			var store = Reading;
			return store.Values.Count == 0 && store.Combos.Count == 0;
		}
	}

	/// <summary>How many values are set.</summary>
	public int ValueCount => Reading.Values.Count;

	/// <summary>
	/// Whether <paramref name="other"/> holds the same values and combos, as <c>CRenderAttributes::AreEqual</c> compares two
	/// sets: numbers and bytes by value, textures, buffers and samplers by what they are.
	/// </summary>
	public bool SameValues( ManagedRenderAttributes other )
	{
		if ( ReferenceEquals( this, other ) ) return true;
		if ( other is null ) return false;
		var mine = Reading;
		var theirs = other.Reading;
		if ( mine.Values.Count != theirs.Values.Count || mine.Combos.Count != theirs.Combos.Count ) return false;

		foreach ( var (k, entry) in mine.Combos )
		{
			if ( !theirs.Combos.TryGetValue( k, out var their ) || their.Value != entry.Value ) return false;
		}

		foreach ( var (k, entry) in mine.Values )
		{
			if ( !theirs.Values.TryGetValue( k, out var their ) ) return false;
			if ( entry.IntegerBits != their.IntegerBits ) return false;
			var same = IsFloats( entry.Value ) ? IsFloats( their.Value ) && Unsafe.As<float[]>( entry.Value ).AsSpan().SequenceEqual( Unsafe.As<float[]>( their.Value ) )
				: IsBytes( entry.Value ) ? IsBytes( their.Value ) && Unsafe.As<byte[]>( entry.Value ).AsSpan().SequenceEqual( Unsafe.As<byte[]>( their.Value ) )
				: Equals( entry.Value, their.Value );
			if ( !same ) return false;
		}

		return true;
	}

	public void SetCombo( StringToken k, int value )
	{
		var store = Writing();
		if ( store.Combos.TryGetValue( k.Value, out var held ) && held.Value == value ) return;
		store.Combos[k.Value] = new Entry<int> { Value = value, Stamp = ++store.Version };
	}

	public object Get( StringToken k ) => Reading.Values.TryGetValue( k.Value, out var entry ) ? entry.Value : null;
	public int? GetCombo( StringToken k ) => Reading.Combos.TryGetValue( k.Value, out var entry ) ? entry.Value : null;

	public float[] GetFloats( StringToken k ) => Get( k ) as float[];

	public int GetInt( StringToken k, int fallback )
	{
		if ( !Reading.Values.TryGetValue( k.Value, out var entry ) ) return fallback;
		if ( entry.IntegerBits is { } bits ) return unchecked((int)bits);
		return entry.Value is float[] { Length: > 0 } f ? (int)f[0] : fallback;
	}

	public uint GetUInt( StringToken k, uint fallback )
	{
		if ( !Reading.Values.TryGetValue( k.Value, out var entry ) ) return fallback;
		if ( entry.IntegerBits is { } bits ) return bits;
		return entry.Value is float[] { Length: > 0 } f ? (uint)f[0] : fallback;
	}

	public void Clear()
	{
		var store = Writing();
		if ( store.Values.Count == 0 && store.Combos.Count == 0 ) return;
		foreach ( var (k, entry) in store.Values ) store.Keep( k, entry.Value );
		store.Values.Clear();
		store.Combos.Clear();
		store.Borrowed.Clear();
		store.RemovedAt = ++store.Version;
	}

	/// <summary>Copy every value and combo into <paramref name="other"/>, replacing those of the same token.</summary>
	public void MergeTo( ManagedRenderAttributes other )
	{
		if ( ReferenceEquals( other, this ) ) return;

		var from = Reading;
		var to = other.Writing();
		var stamp = ++to.Version;
		foreach ( var (k, entry) in from.Values )
		{
			// Arrays go into the other set's own, so setting them again here doesn't change them there
			var value = entry.Value;
			if ( IsFloats( value ) )
			{
				to.Put( k, to.Copy( k, Unsafe.As<float[]>( value ) ), stamp, entry.IntegerBits );
				to.Own( k );
			}
			else if ( IsBytes( value ) && from.Borrowed.Contains( k ) )
			{
				to.Put( k, value, stamp );
				to.Borrowed.Add( k );
			}
			else if ( IsBytes( value ) )
			{
				to.Put( k, to.Copy( k, Unsafe.As<byte[]>( value ) ), stamp );
				to.Own( k );
			}
			else
			{
				to.Put( k, value, stamp );
				to.Own( k );
			}
		}

		foreach ( var (k, entry) in from.Combos ) to.Combos[k] = new Entry<int> { Value = entry.Value, Stamp = stamp };
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
