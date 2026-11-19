using System.Runtime.CompilerServices;
using System.Threading;

namespace Borealis.Gpu;

/// <summary>
/// Progress markers (<see cref="DeviceOptions.ProgressMarkers"/>): each list takes a slot of two words in host memory when it
/// begins, and every marker it records writes its number into both, one as the GPU's front end reaches it and one once the
/// work before it has finished. After the device is lost the words say how far the GPU got in each list.
/// </summary>
public sealed unsafe partial class Device
{
	// Lists' slots, reused in turn: more than the lists of the frames in flight, so a slot isn't reused while the GPU
	// may still write it
	const int MarkerSlots = 4096;

	bool bufferMarkers;
	BufferHandle markerBuffer;
	uint* markerWords;
	ulong markerVkBuffer;
	int nextMarkerSlot;
	readonly MarkerTrail[] markerTrails = new MarkerTrail[MarkerSlots];

	// Names a slot keeps of its markers: the first this many, in a window of one array made with the slots (lists of names
	// per slot grew as lists reached them, allocating in every frame until each slot had been through the longest lists)
	const int NamedMarkers = 128;
	string[] markerNames;

	/// <summary>A slot's list and how many markers it has written: marker n is named at <c>n - 1</c> of the slot's window.</summary>
	sealed class MarkerTrail
	{
		public string List;
		public int Count;
	}

	string MarkerName( int slot, uint n ) => n > NamedMarkers ? "(unnamed)" : markerNames[slot * NamedMarkers + (int)n - 1];

	/// <summary>Whether lists write progress markers.</summary>
	internal bool ProgressMarkers => bufferMarkers && markerWords != null;

	void CreateMarkers()
	{
		if ( !bufferMarkers || Vk.vkCmdWriteBufferMarkerAMD == null ) return;
		markerBuffer = CreateBuffer( new BufferInfo { Size = MarkerSlots * 8, Memory = MemoryLocation.Readback, Name = "Progress markers" } );
		markerWords = (uint*)Unsafe.AsPointer( ref Map( markerBuffer )[0] );
		markerVkBuffer = Get( markerBuffer ).Buffer;
		new Span<uint>( markerWords, MarkerSlots * 2 ).Clear();
		for ( int i = 0; i < MarkerSlots; i++ ) markerTrails[i] = new MarkerTrail();
		markerNames = new string[MarkerSlots * NamedMarkers];
	}

	/// <summary>The next slot for a list beginning, cleared.</summary>
	internal int TakeMarkerSlot( string list )
	{
		var slot = (int)((uint)Interlocked.Increment( ref nextMarkerSlot ) % MarkerSlots);
		Volatile.Write( ref markerWords[slot * 2], 0 );
		Volatile.Write( ref markerWords[slot * 2 + 1], 0 );
		var trail = markerTrails[slot];
		lock ( trail )
		{
			trail.List = list;
			trail.Count = 0;
		}

		return slot;
	}

	/// <summary>Record marker <paramref name="name"/> in <paramref name="list"/>'s slot.</summary>
	internal void WriteMarker( nint list, int slot, string name )
	{
		var trail = markerTrails[slot];
		uint number;
		lock ( trail )
		{
			number = (uint)++trail.Count;
			if ( number <= NamedMarkers ) markerNames[slot * NamedMarkers + (int)number - 1] = name;
		}

		// VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT as the front end reaches it, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT once the work before it is done
		Vk.vkCmdWriteBufferMarkerAMD( list, 0x1, markerVkBuffer, (ulong)slot * 8, number );
		Vk.vkCmdWriteBufferMarkerAMD( list, 0x2000, markerVkBuffer, (ulong)slot * 8 + 4, number );
	}

	/// <summary>Set a slot's words as a GPU that stopped in its list leaves them (tests).</summary>
	internal void SetMarkerProgress( int slot, uint reached, uint done )
	{
		Volatile.Write( ref markerWords[slot * 2], reached );
		Volatile.Write( ref markerWords[slot * 2 + 1], done );
	}

	/// <summary>Whether the GPU started a slot's list but didn't finish it: past its first marker, short of its last.</summary>
	internal bool MarkersPartway( int slot )
	{
		if ( !ProgressMarkers || slot < 0 ) return false;
		var done = Volatile.Read( ref markerWords[slot * 2 + 1] );
		var reached = Volatile.Read( ref markerWords[slot * 2] );
		lock ( markerTrails[slot] ) return reached > 0 && done < markerTrails[slot].Count;
	}

	/// <summary>How far the GPU got in a slot's list: "reached 12/40, done 11/40 (after 'Opaque Forward', in 'Static World Overlays')".</summary>
	internal string DescribeMarkers( int slot )
	{
		if ( !ProgressMarkers || slot < 0 ) return "";
		var reached = Volatile.Read( ref markerWords[slot * 2] );
		var done = Volatile.Read( ref markerWords[slot * 2 + 1] );
		var trail = markerTrails[slot];
		lock ( trail )
		{
			var count = trail.Count;
			if ( done == count && reached == count ) return $" [markers {count}/{count}]";
			string Name( uint n ) => n >= 1 && n <= count ? $"'{MarkerName( slot, n )}'" : "none";
			return $" [markers reached {reached}/{count}, done {done}/{count}: done to {Name( done )}, first not done {Name( done + 1 )}, reached {Name( reached )}, next {Name( reached + 1 )}]";
		}
	}
}
