using NativeEngine;
using System.Text.Json;

namespace Sandbox.Diagnostics;

/// <summary>
/// The GPU allocator's blocks and what's in each, as they were when captured. Memory comes from the driver a block at
/// a time and a block only goes back once it's empty, so a few small allocations can hold a whole one. This is where to
/// see which. Capturing walks the whole allocator, so it's done when asked for, never every frame.
/// </summary>
internal sealed class GpuMemoryMap
{
	internal enum Kind
	{
		Texture,
		RenderTarget,
		Buffer,
		Other,
	}

	internal readonly record struct Allocation( long Offset, long Size, Kind Kind, string Name );

	/// <summary>
	/// Neighbouring allocations of one kind, as one stretch to draw.
	/// </summary>
	internal readonly record struct Run( long Offset, long Size, Kind Kind );

	internal sealed class Block
	{
		public long Size;
		public long Used;
		public long LargestFree;

		/// <summary>
		/// Blocks in the shared pools can have their contents moved out to empty them. The small pools for things that
		/// can't move are left as they are.
		/// </summary>
		public bool Movable;

		/// <summary>
		/// In offset order.
		/// </summary>
		public readonly List<Allocation> Allocations = new();
		public readonly List<Run> Runs = new();

		public float Fill => Size > 0 ? (float)Used / Size : 0;

		/// <summary>
		/// What's at an offset: the allocation there, or nothing for free space.
		/// </summary>
		public Allocation? At( long offset )
		{
			int lo = 0, hi = Allocations.Count - 1;
			while ( lo <= hi )
			{
				var mid = (lo + hi) / 2;
				var a = Allocations[mid];
				if ( offset < a.Offset ) hi = mid - 1;
				else if ( offset >= a.Offset + a.Size ) lo = mid + 1;
				else return a;
			}

			return null;
		}

		public long Bytes( Kind kind )
		{
			long bytes = 0;
			foreach ( var a in Allocations )
				if ( a.Kind == kind ) bytes += a.Size;
			return bytes;
		}
	}

	/// <summary>
	/// Emptiest first: those are the ones holding memory for little.
	/// </summary>
	public readonly List<Block> Blocks = new();

	/// <summary>
	/// Allocations with memory of their own rather than a slice of a block, biggest first. Freed the moment they go.
	/// </summary>
	public readonly List<Allocation> Dedicated = new();

	public long BlockBytes;
	public long UsedBytes;
	public long DedicatedBytes;
	public long UnusedBytes => BlockBytes - UsedBytes;

	/// <summary>
	/// What the driver says this process uses and may use, which is what the system sees.
	/// </summary>
	public long DriverUsage;
	public long DriverBudget;

	public double CapturedAt;

	/// <summary>
	/// Null where there's no allocator to ask: a headless run, or a renderer that isn't Vulkan.
	/// </summary>
	public static GpuMemoryMap Capture()
	{
		if ( !Graphics.IsAvailable ) return null;

		var json = g_pRenderDevice.GetGpuMemoryMap();
		if ( string.IsNullOrEmpty( json ) ) return null;

		var map = new GpuMemoryMap { CapturedAt = RealTime.Now };
		g_pRenderDevice.GetVideoMemoryInfo( out var budget, out var usage, out _ );
		map.DriverBudget = (long)budget;
		map.DriverUsage = (long)usage;

		using var doc = JsonDocument.Parse( json );
		var root = doc.RootElement;

		if ( root.TryGetProperty( "DefaultPools", out var defaults ) )
		{
			foreach ( var type in defaults.EnumerateObject() )
				map.ReadPool( type.Value, movable: true );
		}

		if ( root.TryGetProperty( "CustomPools", out var customs ) )
		{
			foreach ( var type in customs.EnumerateObject() )
				foreach ( var pool in type.Value.EnumerateArray() )
					map.ReadPool( pool, movable: false );
		}

		map.Blocks.Sort( ( a, b ) => a.Fill.CompareTo( b.Fill ) );
		map.Dedicated.Sort( ( a, b ) => b.Size.CompareTo( a.Size ) );
		return map;
	}

	void ReadPool( JsonElement pool, bool movable )
	{
		if ( pool.TryGetProperty( "Blocks", out var blocks ) )
		{
			foreach ( var entry in blocks.EnumerateObject() )
			{
				var block = new Block { Size = Number( entry.Value, "TotalBytes" ), Movable = movable };
				block.Used = block.Size - Number( entry.Value, "UnusedBytes" );

				if ( entry.Value.TryGetProperty( "Suballocations", out var ranges ) )
				{
					foreach ( var range in ranges.EnumerateArray() )
					{
						var size = Number( range, "Size" );
						var type = Text( range, "Type" );
						if ( type == "FREE" )
						{
							block.LargestFree = Math.Max( block.LargestFree, size );
							continue;
						}

						block.Allocations.Add( new Allocation( Number( range, "Offset" ), size, KindOf( type, dedicated: false ), Text( range, "Name" ) ) );
					}
				}

				BuildRuns( block );
				BlockBytes += block.Size;
				UsedBytes += block.Used;
				Blocks.Add( block );
			}
		}

		if ( pool.TryGetProperty( "DedicatedAllocations", out var dedicated ) )
		{
			foreach ( var item in dedicated.EnumerateArray() )
			{
				var size = Number( item, "Size" );
				DedicatedBytes += size;
				Dedicated.Add( new Allocation( 0, size, KindOf( Text( item, "Type" ), dedicated: true ), Text( item, "Name" ) ) );
			}
		}
	}

	/// <summary>
	/// A block can hold thousands of allocations, far more than there are pixels to draw them in. Join neighbours of
	/// one kind, across gaps too small to see.
	/// </summary>
	static void BuildRuns( Block block )
	{
		var tiny = block.Size / 4096;
		foreach ( var a in block.Allocations )
		{
			if ( block.Runs.Count > 0 )
			{
				var last = block.Runs[^1];
				if ( last.Kind == a.Kind && a.Offset - (last.Offset + last.Size) <= tiny )
				{
					block.Runs[^1] = last with { Size = a.Offset + a.Size - last.Offset };
					continue;
				}
			}

			block.Runs.Add( new Run( a.Offset, a.Size, a.Kind ) );
		}
	}

	/// <summary>
	/// The allocator isn't told what an image is for. Render targets and depth buffers over 512 KB are the images
	/// the renderer gives memory of their own, so that's how they're told from textures; smaller ones count as textures.
	/// </summary>
	static Kind KindOf( string type, bool dedicated )
	{
		if ( type is null ) return Kind.Other;
		if ( type.StartsWith( "IMAGE" ) ) return dedicated ? Kind.RenderTarget : Kind.Texture;
		return type == "BUFFER" ? Kind.Buffer : Kind.Other;
	}

	static string Text( JsonElement element, string name ) =>
		element.TryGetProperty( name, out var value ) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	static long Number( JsonElement element, string name )
	{
		if ( !element.TryGetProperty( name, out var value ) ) return 0;
		if ( value.ValueKind == JsonValueKind.Number ) return value.TryGetInt64( out var n ) ? n : 0;
		return value.ValueKind == JsonValueKind.String && long.TryParse( value.GetString(), out var parsed ) ? parsed : 0;
	}

	public static string Describe( Kind kind ) => kind switch
	{
		Kind.Texture => "texture",
		Kind.RenderTarget => "render target",
		Kind.Buffer => "buffer",
		_ => "other",
	};
}
