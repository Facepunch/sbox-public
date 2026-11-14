using System.Diagnostics;
using System.Threading.Tasks;

namespace Borealis.Features;

/// <summary>
/// Texture sizes for aggregates the GPU culls (<see cref="GpuDevice.CullsAggregatesGpuDriven"/>), whose fragments the CPU
/// doesn't test one by one: every <see cref="SizeEveryFrames"/> frames a worker sizes the main view's visible aggregates
/// as the CPU's own cull does (each the largest <see cref="TextureSizes.ForFragment"/> of its fragments in the frustum and
/// over the size cull) and the streaming device takes the answer once it's done (<see cref="ITextureSizeSource"/>). The
/// main thread only copies the view and the visible aggregates when it starts one, and never waits for it.
/// </summary>
internal sealed partial class AggregateRenderFeature
{
	/// <summary>Frames between the worker's sizings.</summary>
	internal static int SizeEveryFrames { get; set; } = 8;

	/// <summary>Size aggregates the GPU culls on a worker (on by default where the device streams); off, they ask for every mip.</summary>
	internal static bool SizeOnWorker { get; set; } = true;

	AggregateSizes sizes;
	int sizedGeneration = -1;

	/// <summary>The worker's sizings, for measuring: the last one's time and fragments, and the total.</summary>
	internal AggregateSizes Sizes => sizes;

	/// <summary>
	/// The size a GPU-culled aggregate's batch asks for: none of its own while the worker sizes it (the worker's answer
	/// stands), every mip otherwise. In the main view, now and then, start the worker on what it sees.
	/// </summary>
	int GpuCulledSize( ViewPass pass, RenderWorld world, List<int> visible )
	{
		var device = RenderHost.Current?.Device;
		if ( !SizeOnWorker || device is not { StreamsTextures: true } ) return TextureSizes.All;

		if ( sizes is null )
		{
			sizes = new AggregateSizes();
			device.AddTextureSizes( sizes );
		}

		// The frame's first main view, every few frames, unless the last sizing is still running
		if ( !pass.IsShadow && ReferenceEquals( pass.View, pass.Root ) && sizedGeneration != argGeneration && argGeneration % SizeEveryFrames == 0 )
		{
			sizedGeneration = argGeneration;
			sizes.Start( world, pass, visible );
		}

		return 0;
	}
}

/// <summary>See <see cref="AggregateRenderFeature"/>'s sizes. One sizing at a time; its answer is taken once.</summary>
internal sealed class AggregateSizes : ITextureSizeSource
{
	readonly object gate = new();
	AggregateObject[] aggregates = [];
	Vector3[] centers = [], extents = [];
	int count;
	ViewFrustum frustum;
	SizeCull sizeCull;
	TextureSizes textureSizes;
	Vector3 camera;
	Task running;

	// Written by the worker, then swapped with the answer the device takes
	List<(GpuMaterial Material, int Size)> working = new(), answer = new();
	bool fresh;

	/// <summary>The last sizing's worker time in milliseconds and the fragments it looked at.</summary>
	public double LastMs { get; private set; }
	public int LastFragments { get; private set; }
	public int LastAggregates { get; private set; }

	/// <summary>Sizings done, and their total worker time.</summary>
	public int Count { get; private set; }
	public double TotalMs { get; private set; }

	/// <summary>Main thread time spent starting sizings, in milliseconds.</summary>
	public double StartMs { get; private set; }

	/// <summary>
	/// Copy the view and its visible aggregates and size them on a worker; nothing while the last sizing runs. The main
	/// thread's part.
	/// </summary>
	public void Start( RenderWorld world, ViewPass pass, List<int> visible )
	{
		if ( running is { IsCompleted: false } ) return;
		var start = Stopwatch.GetTimestamp();

		var objects = world.Objects;
		var objectCenters = world.BoundsCenter;
		var objectExtents = world.BoundsExtents;
		if ( aggregates.Length < visible.Count )
		{
			aggregates = new AggregateObject[visible.Count];
			centers = new Vector3[visible.Count];
			extents = new Vector3[visible.Count];
		}

		count = 0;
		foreach ( var index in visible )
		{
			if ( objects[index] is not AggregateObject { Visible: true, GameLayers: true } aggregate ) continue;
			aggregates[count] = aggregate;
			centers[count] = objectCenters[index];
			extents[count] = objectExtents[index];
			count++;
		}

		frustum = pass.View.Frustum;
		sizeCull = new SizeCull( pass.Root );
		textureSizes = new TextureSizes( pass.View, pass.Root );
		camera = pass.View.Position;
		running = Task.Run( Size );
		StartMs += Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
	}

	// On a worker: each aggregate's size as the CPU's cull works it out, the largest per material
	void Size()
	{
		var start = Stopwatch.GetTimestamp();
		working.Clear();
		var fragmentsSeen = 0;
		for ( int a = 0; a < count; a++ )
		{
			var aggregate = aggregates[a];
			aggregates[a] = null;
			var draws = aggregate.Mesh.DrawsForLod( 0 );
			if ( draws.Length == 0 || draws[0].Material is not { } material ) continue;

			var fragments = aggregate.Fragments;
			var fragmentCenters = aggregate.Centers;
			var fragmentExtents = aggregate.Extents;
			var fallback = textureSizes.ForObject( centers[a], extents[a] ).Fallback;
			var size = 0.0f;
			for ( int i = 0; i < fragments.Length; i++ )
			{
				if ( !frustum.Intersects( fragmentCenters[i], fragmentExtents[i] ) || sizeCull.Culls( fragmentCenters[i], fragmentExtents[i] ) ) continue;
				size = MathF.Max( size, textureSizes.ForFragment( fragments[i].UvDensity, fragmentCenters[i], fragmentExtents[i], camera, fallback ) );
			}

			fragmentsSeen += fragments.Length;
			if ( size > 0 ) working.Add( (material, TextureSizes.Clamp( size )) );
		}

		var ms = Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
		lock ( gate )
		{
			(working, answer) = (answer, working);
			fresh = true;
			LastMs = ms;
			LastFragments = fragmentsSeen;
			LastAggregates = count;
			Count++;
			TotalMs += ms;
		}
	}

	public void Take( Action<GpuMaterial, int> ask )
	{
		lock ( gate )
		{
			if ( !fresh ) return;
			fresh = false;
			foreach ( var (material, size) in answer ) ask( material, size );
		}
	}
}
