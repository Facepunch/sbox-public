using Nav;
using Sandbox.Compression;
using System.Buffers;

namespace Sandbox.Navigation;

/// <summary>
/// Navigation Mesh - allowing AI to navigate a world
/// </summary>
[Expose]
public sealed partial class NavMesh : IDisposable
{
	// The library mesh queries read, on any thread. Written on the main thread only.
	internal Nav.NavMesh mesh;
	internal Nav.NavMesh meshAside => next?.Mesh;

	internal NavigationSimulation Simulation;

	internal long Generation;
	private bool disposed;

	// Making this only work from Scene.NavMesh for now. There's no real reason we can't let
	// then create these and manage them themselves. But for now, early days, I want to lock
	// it down to only required functionality.
	internal NavMesh()
	{
		UpdateAreaIds();
	}

	public void Dispose()
	{
		if ( disposed ) return;
		disposed = true;
		Clear();
	}

	internal void Reset()
	{
		Clear();
		IsLoaded = false;
		IsGenerating = false;
		IsDirty = false;
		boundsKnown = false;
		_bakedDataPath = null;
		bakeMissing = false;
		editorBake?.Cancel();
		editorBake = null;
	}

	void Clear()
	{
		Generation++;
		Simulation?.Invalidate();
		Simulation = null;
		IsEnabled = false;
		mesh = null;
		live = next = null;
		bakedTiles = null;
		queryPool.Clear();
		ReadLinkEnds();
	}

	/// <summary>
	/// Determines wether the navigation mesh is enabled and should be generated
	/// </summary>
	public bool IsEnabled
	{
		get; set
		{
			field = value;
			if ( field )
			{
				Init();
			}
		}
	} = false;

	/// <summary>
	/// The navigation mesh is generating
	/// </summary>
	[Hide]
	public bool IsGenerating { get; private set; } = false;

	/// <summary>
	/// The navigation mesh is dirty and needs a complete rebuild
	/// </summary>
	[Hide]
	public bool IsDirty { get; private set; } = false;

	// Initial load or generation completed
	internal bool IsLoaded = false;


	/// <summary>
	/// Should the generator include static bodies
	/// </summary>
	[Header( "Generation Input" )]
	public bool IncludeStaticBodies { get; set; } = true;

	/// <summary>
	/// Should the generator include keyframed bodies
	/// </summary>
	public bool IncludeKeyframedBodies { get; set; } = true;

	/// <summary>
	/// Don't include these bodies in the generation
	/// </summary>
	public TagSet ExcludedBodies { get; set; } = new();

	/// <summary>
	/// If any, we'll only include bodies with this tag
	/// </summary>
	public TagSet IncludedBodies { get; set; } = new();

	/// <summary>
	/// Skip tile generation during scene load. Tiles can then be generated on demand
	/// via <see cref="GenerateTile"/>, <see cref="RequestTileGeneration"/>, etc.
	/// </summary>
	public bool DeferGeneration { get; set; } = false;

	/// <summary>
	/// By Default , the navmesh will calculate bounds based on the world geometry, but if you want to override that, you can set custom bounds here.
	/// </summary>
	[Header( "Bounds" )]
	public bool CustomBounds { get; set; } = false;

	/// <summary>
	/// The bounds to generate the navmesh within.
	/// Won't take effect until regenerated or reloaded.
	/// </summary>
	[HideIf( nameof( CustomBounds ), false ), WideMode]
	public BBox Bounds { get; set; } = default;

	/// <summary>
	/// Constantly update the navigation mesh in the editor
	/// </summary>
	[Header( "Editor" )]
	public bool EditorAutoUpdate { get; set; } = false;

	/// <summary>
	/// Draw the navigation mesh in the editor
	/// </summary>
	public bool DrawMesh { get; set; }

	/// <summary>
	/// Height of the agent
	/// </summary>
	[Header( "Agent" )]
	public float AgentHeight { get; set; } = 64.0f;

	/// <summary>
	/// The radius of the agent. This will change how much gap is left on the edges of surfaces, so they don't clip into walls.
	/// </summary>
	public float AgentRadius { get; set; } = 16.0f;

	/// <summary>
	/// The maximum height an agent can climb (step)
	/// </summary>
	public float AgentStepSize { get; set; } = 18.0f;

	/// <summary>
	/// The maximum slope an agent can walk up (in degrees)
	/// </summary>
	public float AgentMaxSlope { get; set; } = 40.0f;

	// Tiling props not exposed until we are sure we want to expose them
	private const float CellSize = 4.0f;
	private const float CellHeight = 4.0f;
	private const int TileSizeXYVoxels = 256;
	private const float TileSizeXYWorldSpace = TileSizeXYVoxels * CellSize;

	// How far off the mesh a path end, an agent or its target may be: sideways, and in agent heights up or down
	internal const float SnapDistance = 256f;
	internal const float SnapHeights = 1.51f;

	internal Action OnInit;

	/// <summary>
	/// Set the navgiation a dirty, so it will rebuild over the next few frames.
	/// If you need an immediate rebuild, call <see cref="Generate(PhysicsWorld)"/> instead.
	/// </summary>
	public void SetDirty()
	{
		IsDirty = true;
	}

	internal void Init()
	{
		ThreadSafe.AssertIsMainThread();
		ObjectDisposedException.ThrowIf( disposed, this );
		if ( live is null ) SetMesh( new Nav.NavMesh( CreateSettings(), cacheCodec ) );
	}

	private void SetMesh( Nav.NavMesh loaded )
	{
		live = new MeshWork( loaded );
		next = null;
		bakedTiles = null;
		Publish();
	}

	// The first mesh brings the crowd, and with it the agents that waited for one
	private void Publish()
	{
		mesh = live.Mesh;
		queryPool.Clear();
		if ( Simulation is not null )
		{
			Simulation.SetMesh( mesh );
			return;
		}

		Simulation = new NavigationSimulation( this, mesh );
		OnInit?.Invoke();
	}

	// Agent settings are fixed per library mesh, so changed ones are built into a second mesh aside
	private void Retarget()
	{
		var settings = CreateSettings();
		if ( live.Mesh.Settings.Equals( settings ) ) next = null;
		else if ( next is null || !next.Mesh.Settings.Equals( settings ) ) next = new MeshWork( new Nav.NavMesh( settings, cacheCodec ) );
		if ( next is not null ) next.Abandoned = false;
	}

	// Clamped to what the library can build with
	private NavSettings CreateSettings()
	{
		static float Clamp( float value, float min, float max ) => value >= min ? MathF.Min( value, max ) : min;

		return NavSettings.Create(
			Clamp( AgentHeight, CellHeight, 32767 * CellHeight ),
			Clamp( AgentRadius, 0, 127 * CellSize ),
			Clamp( AgentStepSize, 0, 32767 * CellHeight ),
			Clamp( AgentMaxSlope, 0, 90 ),
			CellSize, CellHeight, TileSizeXYVoxels );
	}

	// LZ4 throws without room for the worst case, where the library expects 0 for "did not fit"
	private static readonly NavCodec cacheCodec = new( static ( source, target ) =>
	{
		var buffer = ArrayPool<byte>.Shared.Rent( LZ4.MaxCompressedSize( source.Length ) );
		int written = LZ4.CompressBlock( source, buffer );
		bool fits = written <= target.Length;
		if ( fits ) buffer.AsSpan( 0, written ).CopyTo( target );
		ArrayPool<byte>.Shared.Return( buffer );
		return fits ? written : 0;
	}, LZ4.DecompressBlock );

	internal void InvalidateAllTiles( PhysicsWorld world )
	{
		if ( IsGenerating || live is null ) return;

		// Settings that change every update start a mesh aside per batch of tiles, not per update
		if ( next is not null && next.Builds.Count > 0 && !next.Mesh.Settings.Equals( CreateSettings() ) )
		{
			next.Abandoned = true;
			return;
		}

		Retarget();
		UpdateBounds( world );
		MarkDirty( CalculateMinMaxTileCoords( Bounds ) );

		IsDirty = false;
	}

	// Scene load: the bake if there is one, then every tile of the bounds the bake does not cover
	internal async Task<bool> Load( PhysicsWorld world )
	{
		if ( IsGenerating )
		{
			Log.Warning( "NavMesh is already generating" );
			return false;
		}

		long generation = Generation;
		try
		{
			IsEnabled = true;
			IsGenerating = true;

			await LoadFromBake();
			if ( generation != Generation || disposed ) return false;
			bakeMissing = !string.IsNullOrEmpty( _bakedDataPath ) && bakedTiles is null;

			Retarget();
			UpdateBounds( world );

			// Tiles the bake covers are finished, the ones it left empty on purpose included
			var waiting = new List<(int X, int Y)>();
			if ( !DeferGeneration ) MarkDirty( CalculateMinMaxTileCoords( Bounds ), waiting, bakedTiles, world );
			await BuildTiles( world, waiting );
		}
		finally
		{
			if ( generation == Generation && !disposed )
			{
				IsGenerating = false;
				IsDirty = false;
				IsLoaded = true;
			}
		}

		return true;
	}

	/// <summary>
	/// Builds every tile in the bounds from the geometry of the world. False when a tile failed to build
	/// or the navmesh is already generating.
	/// </summary>
	public async Task<bool> Generate( PhysicsWorld world )
	{
		if ( IsGenerating )
		{
			Log.Warning( "NavMesh is already generating" );
			return false;
		}

		long generation = Generation;
		int failed = failedBuilds;
		try
		{
			IsEnabled = true;
			IsGenerating = true;

			Retarget();
			bakedTiles = null;
			UpdateBounds( world );

			var waiting = new List<(int X, int Y)>();
			MarkDirty( CalculateMinMaxTileCoords( Bounds ), waiting, world: world );
			await BuildTiles( world, waiting );
		}
		finally
		{
			if ( generation == Generation && !disposed )
			{
				IsGenerating = false;
				IsDirty = false;
				IsLoaded = true;
			}
		}

		return failedBuilds == failed;
	}

	// Calls that take a box clamp to Bounds once those are custom or have been computed
	private bool boundsKnown;

	void UpdateBounds( PhysicsWorld world )
	{
		if ( !CustomBounds ) Bounds = CalculateWorldBounds( world );
		boundsKnown = true;
	}

	private BBox CalculateWorldBounds( PhysicsWorld world )
	{
		// Iterate over all bodies and create world bounds
		BBox? result = null;
		foreach ( var body in world.Bodies )
		{
			if ( !IsBodyRelevantForNavmesh( body ) )
			{
				continue;
			}

			result = result == null ? body.GetBounds() : result.Value.AddBBox( body.GetBounds() );
		}

		// Grow the bounds a bit to make sure we don't have any precission issues with the edges
		return result?.Grow( CellSize * 2.0f ) ?? new BBox( Vector3.Zero, Vector3.Zero );
	}

	internal bool IsBodyRelevantForNavmesh( PhysicsBody body )
	{
		var navmeshBodyType = body.NavmeshBodyTypeOverride ?? body.BodyType;
		if ( body.ShapeCount == 0 ) return false;
		if ( navmeshBodyType == PhysicsBodyType.Dynamic ) return false; // never include dynamic bodies
		if ( navmeshBodyType == PhysicsBodyType.Static && !IncludeStaticBodies ) return false;
		if ( navmeshBodyType == PhysicsBodyType.Keyframed && !IncludeKeyframedBodies ) return false;

		// Excluded by tags
		if ( ExcludedBodies is not null && !ExcludedBodies.IsEmpty && body.Shapes.Any( shape => shape.Tags.HasAny( ExcludedBodies ) ) )
			return false;

		// Inlcuded by tags
		if ( IncludedBodies is not null && !IncludedBodies.IsEmpty && !body.Shapes.Any( shape => shape.Tags.HasAny( IncludedBodies ) ) )
			return false;

		return true;
	}

	/// <summary>
	/// Implement this interface to receive navmesh editor events.
	/// </summary>
	public interface IEventListener
	{
		/// <summary>
		/// Called when an area definition has changed or loaded/created.
		/// </summary>
		void OnAreaDefinitionChanged() { }
	}
}
