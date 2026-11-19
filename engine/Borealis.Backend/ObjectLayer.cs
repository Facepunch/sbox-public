using System.Threading;

namespace Borealis.Backend;

/// <summary>
/// Parameters set as an object's attributes (<see cref="BackendContext.SetObjectAttributes"/>), as material objects use
/// them: the object's own, or its lightmap's or light probe's, which its draws read over the pass's. The names it sets
/// go in <see cref="MaterialObjectCache.LayerNames"/>, so layouts know which of their pass bindings an object can change.
/// Its constant buffers are copied once a frame into the pass buffer, so draws of one material with different objects
/// bind the same sets at other dynamic offsets instead of writing sets.
/// </summary>
internal sealed class ObjectLayer
{
	/// <summary>The parameters' <see cref="BackendParameters.Version"/> this was brought up to date at.</summary>
	public int Version = int.MinValue;

	/// <summary>The combos it sets, as a key: interned, so equal keys are the same string; empty for none.</summary>
	public string Combos = "";

	readonly Dictionary<(uint Name, int Size, bool Dynamic), (ulong Frame, int Version, long Offset)> constants = new();

	/// <summary>
	/// Where constant buffer <paramref name="name"/> is this frame, copied at <paramref name="size"/>: in this frame's
	/// pass region when <paramref name="dynamic"/> (bound at that dynamic offset), else in the material buffer, kept by
	/// content.
	/// </summary>
	public long Constants( MaterialObjectCache cache, uint name, int size, byte[] data, bool dynamic, int version )
	{
		var frame = cache.Gpu.FrameNumber;
		lock ( constants )
		{
			if ( constants.TryGetValue( (name, size, dynamic), out var found ) && found.Frame == frame && found.Version == version ) return found.Offset;

			long offset;
			if ( dynamic )
			{
				offset = cache.AllocatePassConstants( size, out var target );
				target.Clear();
				data.AsSpan( 0, Math.Min( data.Length, size ) ).CopyTo( target );
			}
			else
			{
				var padded = new byte[size];
				data.AsSpan( 0, Math.Min( data.Length, size ) ).CopyTo( padded );
				offset = cache.KeepPassConstants( padded, size );
			}

			constants[(name, size, dynamic)] = (frame, version, offset);
			return offset;
		}
	}

	/// <summary>The layer for <paramref name="parameters"/>, brought up to date with them; its names go in <paramref name="cache"/>'s.</summary>
	public static ObjectLayer Of( BackendParameters parameters, MaterialObjectCache cache )
	{
		var layer = Volatile.Read( ref parameters.Layer );
		if ( layer is null )
		{
			Interlocked.CompareExchange( ref parameters.Layer, new ObjectLayer(), null );
			layer = parameters.Layer;
		}

		var version = parameters.Version;
		if ( Volatile.Read( ref layer.Version ) == version ) return layer;

		lock ( layer )
		{
			if ( layer.Version == version ) return layer;

			// Parameters set every frame bring their layers up to date every frame: kept scratch, and the key made again only
			// when it differs
			var names = scratchNames ??= new();
			var combos = scratchCombos ??= new();
			names.Clear();
			combos.Clear();
			parameters.Names( names, combos );
			cache.AddLayerNames( names );
			layer.Combos = ComboKey( combos, layer.Combos );
			Volatile.Write( ref layer.Version, version );
		}

		return layer;
	}

	[ThreadStatic] static HashSet<uint> scratchNames;
	[ThreadStatic] static Dictionary<uint, int> scratchCombos;
	[ThreadStatic] static List<KeyValuePair<uint, int>> scratchSorted;
	[ThreadStatic] static System.Text.StringBuilder scratchKey;

	/// <summary>The combos as a key, by name (<see cref="Combos"/>): <paramref name="current"/> when it's the same.</summary>
	static string ComboKey( Dictionary<uint, int> combos, string current )
	{
		if ( combos.Count == 0 ) return "";

		var sorted = scratchSorted ??= new();
		sorted.Clear();
		foreach ( var combo in combos ) sorted.Add( combo );
		sorted.Sort( static ( a, b ) => a.Key.CompareTo( b.Key ) );

		var key = scratchKey ??= new();
		key.Clear();
		foreach ( var (combo, value) in sorted ) key.Append( combo ).Append( '=' ).Append( value ).Append( ';' );
		return key.Equals( current.AsSpan() ) ? current : string.Intern( key.ToString() );
	}

	// The last key joined with a lighting layer's (BackendContext.SetObjectAttributes), so drawing the same pair again joins nothing
	(string Lighting, string Joined) joined;

	/// <summary>This layer's combos then <paramref name="lighting"/>'s, as one interned key.</summary>
	public string JoinedWith( string lighting )
	{
		lock ( this )
		{
			if ( !ReferenceEquals( joined.Lighting, lighting ) || joined.Joined is null ) joined = (lighting, string.Intern( Combos + "|" + lighting ));
			return joined.Joined;
		}
	}
}
