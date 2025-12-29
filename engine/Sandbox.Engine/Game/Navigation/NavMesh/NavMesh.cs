using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using System.Runtime.CompilerServices;

namespace Sandbox.Navigation;

/// <summary>
/// Navigation Mesh - allowing AI to navigate a world
/// </summary>
[Expose]
public sealed partial class NavMesh : IDisposable
{
	internal DtNavMesh navmeshInternal;

	internal DtCrowd crowd;

	internal DtNavMeshQuery query;

	// Making this only work from Scene.NavMesh for now. There's no real reason we can't let
	// then create these and manage them themselves. But for now, early days, I want to lock
	// it down to only required functionality.
	internal NavMesh()
	{
		navmeshInternal = new DtNavMesh();
	}

	~NavMesh()
	{
		Dispose();
	}

	public void Dispose()
	{
		tileCache.Dispose();

		GC.SuppressFinalize( this );
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

	/// <summary>
	/// Should the generator include static bodies
	/// </summary>
	[Group( "Generation Input" )]
	public bool IncludeStaticBodies { get; set; } = true;

	/// <summary>
	/// Should the generator include keyframed bodies
	/// </summary>
	[Group( "Generation Input" )]
	public bool IncludeKeyframedBodies { get; set; } = true;

	/// <summary>
	/// Don't include these bodies in the generation
	/// </summary>
	[Group( "Generation Input" )]
	public TagSet ExcludedBodies { get; set; } = new();

	/// <summary>
	/// If any, we'll only include bodies with this tag
	/// </summary>
	[Group( "Generation Input" )]
	public TagSet IncludedBodies { get; set; } = new();

	/// <summary>
	/// Constantly update the navigation mesh in the editor
	/// </summary>
	[Group( "Editor" )]
	public bool EditorAutoUpdate { get; set; } = true;

	/// <summary>
	/// Draw the navigation mesh in the editor
	/// </summary>
	[Group( "Editor" )]
	public bool DrawMesh { get; set; }

	/// <summary>
	/// Height of the agent
	/// </summary>
	[Group( "Agent" )]
	public float AgentHeight { get; set; } = 64.0f;

	/// <summary>
	/// The radius of the agent. This will change how much gap is left on the edges of surfaces, so they don't clip into walls.
	/// </summary>
	[Group( "Agent" )]
	public float AgentRadius { get; set; } = 16.0f;

	/// <summary>
	/// The maximum height an agent can climb (step)
	/// </summary>
	[Group( "Agent" )]
	public float AgentStepSize { get; set; } = 18.0f;

	/// <summary>
	/// The maximum slope an agent can walk up (in degrees)
	/// </summary>
	[Group( "Agent" )]
	public float AgentMaxSlope { get; set; } = 40.0f;

	// Tiling props not exposed until we are sure we want to expose them

	/// <summary>
	/// The xz-plane cell size to use for fields. [Limit: > 0] [Units: wu] 
	/// </summary>
	private float CellSize = 4.0f;

	/// <summary>
	/// The y-axis cell size to use for fields. [Limit: > 0] [Units: wu]
	/// </summary>
	private float CellHeight = 4.0f;

	/// <summary>
	/// The width/height size of tile's on the xy-plane. [Limit: &gt;= 0] [Units: vx]
	/// </summary>
	private int TileSizeXYVoxels { get; set; } = 128;

	private float TileSizeXYWorldSpace { get => TileSizeXYVoxels * CellSize; }

	// We have DT_TILE_BITS(28) bits for tiles and DT_POLY_BITS(20) for poly's, so we can have 2^28 tiles and 2^20 polys
	internal Vector2Int TileCount { get; set; } = new Vector2Int( 256, 256 ); // Sqrt( 1<< 28 ) = 16384

	internal int MaxPolys = 1 << 20;

	internal Action OnInit;

	internal BBox WorldBounds;

	private float TileHeightWorldSpace { get; set; } = 1048576f;

	// The origin of the tile grid
	private Vector3 TileOrigin
	{
		get => -0.5f * TileSizeWorldSpace.WithZ( 0 ) * new Vector3( TileCount.x, TileCount.y ) - new Vector3( 0, 0, 0.5f * TileSizeWorldSpace.z );
	}

	private Vector3 TileSizeWorldSpace { get => new Vector3( TileSizeXYWorldSpace, TileSizeXYWorldSpace, TileHeightWorldSpace ); }

	/// <summary>
	/// Set the navgiation a dirty, so it will rebuild over the next few frames.
	/// If you need an immediate rebuild, call <see cref="Generate(PhysicsWorld)"/> instead.
	/// </summary>
	public void SetDirty()
	{
		IsDirty = true;
	}

	private bool _isInitialized = false;

	internal void Init()
	{
		ThreadSafe.AssertIsMainThread();

		if ( _isInitialized )
		{
			return;
		}

		var navMeshParams = new DtNavMeshParams
		{
			tileHeight = TileSizeXYWorldSpace,
			tileWidth = TileSizeXYWorldSpace,
			maxTiles = TileCount.x * TileCount.y,
			maxPolys = MaxPolys,
			orig = ToNav( TileOrigin ),
		};

		navmeshInternal.Init( navMeshParams, 6 );

		DtCrowdConfig crowdConfig = new DtCrowdConfig( AgentRadius, AgentHeight );
		crowdConfig.topologyOptimizationTimeThreshold = 1f;
		crowd = new DtCrowd( crowdConfig, navmeshInternal );

		DtObstacleAvoidanceParams obsParams = new DtObstacleAvoidanceParams();

		obsParams.VelocityBias = 0.4f;
		obsParams.DesiredVelocityWeight = 2.0f;
		obsParams.CurrentVelocityWeight = 0.75f;
		obsParams.SideBiasWeight = 0.75f;
		obsParams.TimeOfImpactWeight = 2.5f;
		obsParams.HorizonTime = 2.5f;
		obsParams.GridResolution = 33;
		obsParams.AdaptiveDivisions = 7;
		obsParams.AdaptiveRings = 2;
		obsParams.AdaptiveRefinementDepth = 5;

		obsParams.VelocityBias = 0.5f;
		obsParams.AdaptiveDivisions = 16;
		obsParams.AdaptiveRings = 4;
		obsParams.AdaptiveRefinementDepth = 16;
		crowd.SetObstacleAvoidanceParams( 0, obsParams );

		query = new DtNavMeshQuery( navmeshInternal );

		_isInitialized = true;

		OnInit?.Invoke();
	}

	internal void InvalidateAllTiles( PhysicsWorld world )
	{
		if ( IsGenerating ) return;

		WorldBounds = CalculateWorldBounds( world );
		// accountf or a border incase world shrinks
		WorldBounds = WorldBounds.Grow( TileSizeXYWorldSpace * 2 );
		Gizmo.Draw.LineBBox( WorldBounds );

		var minMaxBounds = CalculateMinMaxTileCoords( WorldBounds );

		// request full rebuild for every tile in bounds
		for ( int x = minMaxBounds.Left; x <= minMaxBounds.Right; x++ )
		{
			for ( int y = minMaxBounds.Top; y <= minMaxBounds.Bottom; y++ )
			{
				var tile = tileCache.GetOrAddTile( new Vector2Int( x, y ) );
				tile.RequestFullRebuild();
			}
		}

		IsDirty = false;
	}

	public async Task<bool> Generate( PhysicsWorld world )
	{
		if ( IsGenerating )
		{
			Log.Warning( "NavMesh is already generating" );
			return false;
		}

		try
		{
			IsGenerating = true;

			Init();

			WorldBounds = CalculateWorldBounds( world );

			await GenerateTiles( world, WorldBounds );
		}
		finally
		{
			IsGenerating = false;
			IsDirty = false;
		}

		return true;
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


			result = result == null ? body.GetBounds() : result?.AddBBox( body.GetBounds() );
		}

		if ( result != null )
		{
			result?.Grow( CellSize * 2.0f ); // Grow the bounds a bit to make sure we don't have any precission issues with the edges

			return (BBox)result;
		}

		return new BBox( Vector3.Zero, Vector3.Zero );
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

	internal int GetPolyCount( Vector2Int tilePosition )
	{
		var tile = navmeshInternal.GetTileAt( tilePosition.x, tilePosition.y, 0 );
		if ( tile == null || tile.data.header == null )
		{
			return default;
		}

		return tile == null ? 0 : tile.data.header.polyCount;
	}

	internal DtPoly GetPoly( Vector2Int tilePosition, int index )
	{
		var tile = navmeshInternal.GetTileAt( tilePosition.x, tilePosition.y, 0 );
		if ( tile == null || tile.data.header == null || index >= tile.data.header.polyCount )
		{
			return default;
		}

		return tile.data.polys[index];
	}

	internal int GetPolyVertCount( Vector2Int tilePosition, int index )
	{
		var tile = navmeshInternal.GetTileAt( tilePosition.x, tilePosition.y, 0 );
		if ( tile == null || tile.data.header == null || index >= tile.data.header.polyCount )
		{
			return default;
		}

		return tile.data.header.vertCount;
	}

	internal IEnumerable<Vector3> GetPolyVerts( Vector2Int tilePosition, int polyIndex )
	{
		var tile = navmeshInternal.GetTileAt( tilePosition.x, tilePosition.y, 0 );
		if ( tile == null || tile.data.header == null || polyIndex >= tile.data.header.polyCount )
		{
			return [];
		}
		var poly = tile.data.polys[polyIndex];

		return poly.verts.Select( vertexIndex => FromNav( tile.data.verts[vertexIndex] ) );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal static Vector3 FromNav( Vector3 v )
	{
		return new Vector3( v.x, v.z, v.y );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal static Vector3 ToNav( Vector3 v )
	{
		return new Vector3( v.x, v.z, v.y );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal static BBox ToNav( BBox b )
	{
		return new BBox( ToNav( b.Mins ), ToNav( b.Maxs ) );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal static Sphere ToNav( Sphere s )
	{
		return new Sphere( ToNav( s.Center ), s.Radius );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal static Capsule ToNav( Capsule c )
	{
		return new Capsule( ToNav( c.CenterA ), ToNav( c.CenterB ), c.Radius );
	}

	// Quaternion/Rotation conversion between world space and nav space.
	// Mapping chosen to match existing position (Vector3) axis swizzle (Y<->Z) plus handedness adjustments:
	// World (x, y, z, w) -> Nav ( -x, -z,  y, w )
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal static Rotation ToNav( Rotation r )
	{
		return new Rotation( -r.x, -r.z, -r.y, r.w );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal static Transform ToNav( in Transform t )
	{
		return new Transform(
			ToNav( t.Position ),
			ToNav( t.Rotation ),
			ToNav( t.Scale )
		);
	}

}
