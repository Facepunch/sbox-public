using System.Diagnostics;
using System.Threading.Tasks;

namespace Borealis.Features;

/// <summary>
/// Texture sizes for aggregates the GPU culls (<see cref="GpuDevice.CullsAggregatesGpuDriven"/>), whose fragments the CPU
/// doesn't test one by one: every <see cref="SizeEveryFrames"/> frames a worker sizes the main view's visible aggregates
/// as the CPU's own cull does (each the largest <see cref="TextureSizes.ForFragment"/> of its fragments in the frustum and
/// over the size cull) and the streaming device takes the answer once it's done (<see cref="ITextureSizeSource"/>). The
/// main thread only copies the view and the visible aggregates when it starts one, and never waits for it. Resident static
/// meshes the main view sees are sized there too, each at the LOD the CPU would pick for it.
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
	int GpuCulledSize( ViewPass pass, RenderWorld world, List<int> visible, ResidentList resident )
	{
		var device = RenderHost.Current?.Device;
		if ( WorkerSize( device ) != 0 ) return TextureSizes.All;

		// The frame's first main view, every few frames, unless the last sizing is still running
		if ( !pass.IsShadow && ReferenceEquals( pass.View, pass.Root ) && sizedGeneration != argGeneration && argGeneration % SizeEveryFrames == 0 )
		{
			sizedGeneration = argGeneration;
			var meshes = resident is not null && (resident.MeshGroups.Count > 0 || resident.Overlays.Count > 0) && Meshes is not null ? pass.Visible( Meshes ) : null;
			sizes.Start( world, pass, visible, meshes, resident?.Holds, pass.ResidentHolds is not null ? resident : null );
		}

		return 0;
	}

	/// <summary>The size a batch the worker sizes asks for itself: none while the worker sizes it, every mip when it doesn't.</summary>
	int WorkerSize( GpuDevice device )
	{
		if ( !SizeOnWorker || device is not { StreamsTextures: true } ) return TextureSizes.All;
		if ( sizes is null )
		{
			sizes = new AggregateSizes();
			device.AddTextureSizes( sizes );
		}

		return 0;
	}
}

/// <summary>See <see cref="AggregateRenderFeature"/>'s sizes. One sizing at a time; its answer is taken once.</summary>
internal sealed class AggregateSizes : ITextureSizeSource, System.Threading.IThreadPoolWorkItem
{
	readonly object gate = new();
	AggregateObject[] aggregates = [];
	Vector3[] centers = [], extents = [];
	int count;
	MeshObject[] meshes = [];
	Vector3[] meshCenters = [], meshExtents = [];
	int meshCount;

	// From these on, aggregates and meshes of a resident list the collect left out: the worker culls them as it would have
	int firstHeld, firstHeldMesh;
	float tanHalfFov, screenWidth;
	ViewFrustum frustum;
	SizeCull sizeCull;
	TextureSizes textureSizes;
	Vector3 camera;

	// Whether a sizing runs on the pool now: this queued as its own work item, so starting one allocates no task
	volatile bool running;

	void System.Threading.IThreadPoolWorkItem.Execute()
	{
		try
		{
			Size();
		}
		catch ( Exception )
		{
			// As a task's unobserved failure: this sizing gives no answer, the next starts again
		}
		finally
		{
			running = false;
		}
	}

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
	/// thread's part. With <paramref name="held"/>, the view's resident list whose aggregates and meshes the collect left
	/// out: all of them, which the worker culls itself.
	/// </summary>
	public void Start( RenderWorld world, ViewPass pass, List<int> visible, List<int> meshesVisible = null, bool[] holds = null, AggregateRenderFeature.ResidentList held = null )
	{
		if ( running ) return;
		var start = Stopwatch.GetTimestamp();

		var objects = world.Objects;
		var objectCenters = world.BoundsCenter;
		var objectExtents = world.BoundsExtents;
		var wanted = visible.Count + (held?.Batches.Count ?? 0);
		if ( aggregates.Length < wanted )
		{
			aggregates = new AggregateObject[wanted];
			centers = new Vector3[wanted];
			extents = new Vector3[wanted];
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

		firstHeld = count;
		if ( held is not null )
		{
			foreach ( var batch in held.Batches )
			{
				var index = batch.Aggregate.Index;
				if ( index < 0 || index >= objects.Length ) continue;
				aggregates[count] = batch.Aggregate;
				centers[count] = objectCenters[index];
				extents[count] = objectExtents[index];
				count++;
			}
		}

		// The resident meshes it sees, which no view prepares
		meshCount = 0;
		if ( meshesVisible is not null && holds is not null )
		{
			if ( meshes.Length < meshesVisible.Count )
			{
				meshes = new MeshObject[meshesVisible.Count];
				meshCenters = new Vector3[meshesVisible.Count];
				meshExtents = new Vector3[meshesVisible.Count];
			}

			foreach ( var index in meshesVisible )
			{
				if ( index >= holds.Length || !holds[index] || objects[index] is not MeshObject mesh ) continue;
				meshes[meshCount] = mesh;
				meshCenters[meshCount] = objectCenters[index];
				meshExtents[meshCount] = objectExtents[index];
				meshCount++;
			}
		}

		firstHeldMesh = meshCount;
		if ( held is not null )
		{
			var heldMeshes = 0;
			foreach ( var group in held.MeshGroups ) heldMeshes += group.Members.Length;
			foreach ( var order in held.Overlays )
			{
				foreach ( var group in order.Groups ) heldMeshes += group.Members.Length;
			}

			if ( meshes.Length < meshCount + heldMeshes )
			{
				Array.Resize( ref meshes, meshCount + heldMeshes );
				Array.Resize( ref meshCenters, meshCount + heldMeshes );
				Array.Resize( ref meshExtents, meshCount + heldMeshes );
			}

			foreach ( var group in held.MeshGroups ) AddHeld( group, objects, objectCenters, objectExtents );
			foreach ( var order in held.Overlays )
			{
				foreach ( var group in order.Groups ) AddHeld( group, objects, objectCenters, objectExtents );
			}
		}

		tanHalfFov = MathF.Tan( pass.Root.FieldOfView.DegreeToRadian() * 0.5f );
		screenWidth = pass.Root.Viewport.Width;
		frustum = pass.View.Frustum;
		sizeCull = new SizeCull( pass.Root );
		textureSizes = new TextureSizes( pass.View, pass.Root );
		camera = pass.View.Position;
		running = true;
		System.Threading.ThreadPool.UnsafeQueueUserWorkItem( this, preferLocal: false );
		StartMs += Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
	}

	void AddHeld( AggregateRenderFeature.MeshGroup group, ReadOnlySpan<RenderObject> objects, ReadOnlySpan<Vector3> objectCenters, ReadOnlySpan<Vector3> objectExtents )
	{
		foreach ( var mesh in group.Members )
		{
			var index = mesh.Index;
			if ( index < 0 || index >= objects.Length ) continue;
			meshes[meshCount] = mesh;
			meshCenters[meshCount] = objectCenters[index];
			meshExtents[meshCount] = objectExtents[index];
			meshCount++;
		}
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
			if ( a >= firstHeld && (!frustum.Intersects( centers[a], extents[a] ) || sizeCull.Culls( centers[a], extents[a] )) ) continue;
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

		// Resident meshes: each draw of the LOD the CPU would pick, sized as the mesh feature sizes a run's draws
		for ( int m = 0; m < meshCount; m++ )
		{
			var mesh = meshes[m];
			meshes[m] = null;
			if ( m >= firstHeldMesh && (!frustum.Intersects( meshCenters[m], meshExtents[m] ) || sizeCull.Culls( meshCenters[m], meshExtents[m] )) ) continue;
			if ( mesh.Mesh is not { } model ) continue;

			var lod = MeshRenderFeature.ChooseLod( mesh, meshCenters[m], camera, tanHalfFov, screenWidth );
			var (scale, fallback) = textureSizes.ForObject( meshCenters[m], meshExtents[m] );
			foreach ( ref readonly var draw in model.DrawsForLod( lod ) )
			{
				var material = mesh.MaterialOverride ?? draw.Material;
				var size = TextureSizes.ForDraw( draw.UvDensity, scale, fallback );
				if ( material is not null && size > 0 ) working.Add( (material, size) );
			}
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
