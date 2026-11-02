namespace Sandbox;

public sealed partial class WaterBody
{
	/// <summary>
	/// Distance below the surface to the volume's bottom, in local inches.
	/// Changing depth preserves the water level and finite surface size.
	/// </summary>
	[Property, Group( "Surface" ), Order( -298 ), Range( 1, 16384 ), Step( 1 ), HideIf( nameof( UsesMeshDepth ), true )]
	public float Depth
	{
		get;
		set => field = float.IsFinite( value ) ? MathF.Max( 1, value ) : 1;
	} = 512;

	/// <summary>
	/// Width and length of an ocean's native swimming and physics region, in world inches.
	/// This region stays centred on the water body; mathematical water queries remain unbounded horizontally.
	/// </summary>
	const float OceanSimulationSize = 131072;

	internal float WorldDepth => MathF.Max( 0.1f, MathF.Abs( Depth * WorldScale.z ) );

	GameObject _volumeObject;
	BoxCollider _volume;
	IDisposable _boundsUndoScope;
	Transform? _boundsDragFrame;
	static readonly List<WaterBody> _volumeQueries = [];

	/// <summary>
	/// Convert a world position to surface-relative coordinates measured in world inches.
	/// Positive Z lies above the water; X and Y follow the body's rotation without its scale.
	/// </summary>
	public Vector3 ToSurface( Vector3 worldPosition ) => WorldRotation.Inverse * (worldPosition - SurfaceOrigin);

	/// <summary>
	/// Convert surface-relative coordinates in world inches to a world position.
	/// </summary>
	public Vector3 FromSurface( Vector3 position ) => SurfaceOrigin + WorldRotation * position;

	/// <summary>
	/// Whether a world point lies inside the enabled fluid volume, including its surface and bottom.
	/// </summary>
	public bool Contains( Vector3 worldPosition )
	{
		if ( !Active || WorldScale.x == 0 || WorldScale.y == 0 || WorldScale.z == 0 )
			return false;

		var point = ToSurface( worldPosition );
		return InFootprint( worldPosition ) && point.z <= SampleHeight( point ) && point.z >= -WorldDepth;
	}

	/// <summary>
	/// Find an active fluid volume containing a world point. Finite volumes take precedence
	/// over infinite water; overlapping volumes of the same kind use scene order.
	/// </summary>
	public static WaterBody Find( Scene scene, Vector3 position )
	{
		WaterBody result = null;
		_volumeQueries.Clear();
		scene.GetAll( _volumeQueries );
		foreach ( var water in _volumeQueries )
		{
			if ( !water.Active || !water.Contains( position ) )
				continue;

			result ??= water;
			if ( !water.Ocean )
			{
				result = water;
				break;
			}
		}
		_volumeQueries.Clear();
		return result;
	}

	/// <summary>
	/// Resolve only the native trigger owned by a WaterBody, leaving legacy water colliders unchanged.
	/// </summary>
	internal static WaterBody FromCollider( Collider collider )
	{
		if ( !collider.IsValid() || !collider.GameObject.Parent.IsValid() )
			return null;
		var water = collider.GameObject.Parent.GetComponent<WaterBody>();
		return water.IsValid() && water.Active && water._volume == collider ? water : null;
	}

	/// <summary>
	/// Sample immersion along a character's foot-to-head segment, excluding the discovery envelope
	/// above the waves and respecting the volume's bottom. Endpoint wave heights interpolate along the segment.
	/// </summary>
	internal float SubmergedFraction( Vector3 foot, Vector3 head )
	{
		var start = ToSurface( foot );
		var end = ToSurface( head );
		float from = 0, to = 1;
		if ( !Clip( SampleHeight( start ) - start.z, SampleHeight( end ) - end.z )
			|| !Clip( start.z + WorldDepth, end.z + WorldDepth ) )
			return 0;
		return MathF.Max( 0, to - from );

		bool Clip( float a, float b )
		{
			if ( a >= 0 && b >= 0 )
				return true;
			if ( a < 0 && b < 0 )
				return false;
			float crossing = a / (a - b);
			if ( a < 0 )
				from = MathF.Max( from, crossing );
			else
				to = MathF.Min( to, crossing );
			return to >= from;
		}
	}

	internal float SurfaceDistance( Vector3 position ) => Vector3.Dot( position - SurfaceOrigin, WorldRotation.Up );

	/// <summary>
	/// Signed distance above the animated surface, measured along the body's mean normal.
	/// Capture planes and volume bounds continue to use the stable mean plane.
	/// </summary>
	internal float WaveDistance( Vector3 position )
	{
		var point = ToSurface( position );
		return point.z - SampleHeight( point );
	}


	internal bool InFootprint( Vector3 position )
	{
		if ( Ocean )
			return true;

		var local = WorldTransform.PointToLocal( position ) - SurfaceOffset;
		return MathF.Abs( local.x ) <= MathF.Abs( Size.x ) * 0.5f && MathF.Abs( local.y ) <= MathF.Abs( Size.y ) * 0.5f;
	}

	/// <summary>
	/// Apply edited bounds in surface-relative world inches, moving the origin to the new top centre.
	/// </summary>
	internal void ResizeVolume( BBox bounds ) => ResizeVolume( bounds, new Transform( SurfaceOrigin, WorldRotation, 1 ) );

	/// <summary>
	/// Apply edited bounds measured in a unit-scale surface frame, which a gizmo drag holds
	/// at its starting origin while this moves the origin to the new top centre.
	/// </summary>
	internal void ResizeVolume( BBox bounds, Transform frame )
	{
		var scale = WorldScale.Abs();
		if ( scale.x <= 0 || scale.y <= 0 || scale.z <= 0 )
			return;

		WorldPosition = frame.PointToWorld( bounds.Center.WithZ( bounds.Maxs.z ) );
		Size = new Vector2( bounds.Size.x / scale.x, bounds.Size.y / scale.y );
		Depth = bounds.Size.z / scale.z;
	}

	/// <summary>
	/// Keep the native water trigger aligned with the authored surface and depth.
	/// Its unit scale keeps mirrored transforms from placing the fluid above the surface.
	/// </summary>
	void UpdateVolume()
	{
		var scale = WorldScale.Abs();
		if ( scale.x <= 0 || scale.y <= 0 || scale.z <= 0 )
		{
			if ( _volumeObject.IsValid() )
				_volumeObject.Enabled = false;
			return;
		}

		if ( !_volume.IsValid() )
		{
			_volumeObject = new GameObject( GameObject, false, "Water fluid volume" );
			_volumeObject.Flags = GameObjectFlags.NotSaved | GameObjectFlags.Hidden | GameObjectFlags.NotNetworked;
			_volumeObject.Tags.Add( "water" );
			_volume = _volumeObject.Components.Create<BoxCollider>();
			_volume.IsTrigger = true;
			_volume.Surface = Surface.FindByName( "water" );
		}

		_volumeObject.WorldTransform = new Transform( SurfaceOrigin, WorldRotation, 1 );
		float rise = HasWaves ? MathF.Max( 0, WaveHeight ) : 0;
		_volume.Center = new Vector3( 0, 0, (rise - WorldDepth) * 0.5f );
		_volume.Scale = new Vector3(
			Ocean ? OceanSimulationSize : MathF.Max( 0.1f, MathF.Abs( Size.x * WorldScale.x ) ),
			Ocean ? OceanSimulationSize : MathF.Max( 0.1f, MathF.Abs( Size.y * WorldScale.y ) ), WorldDepth + rise );
		_volumeObject.Enabled = true;
	}

	/// <summary>
	/// Refresh the water trigger when its properties or transform are edited.
	/// </summary>
	protected override void OnUpdate()
	{
		bool meshChanged = UpdateMeshBounds();
		if ( Scene.IsEditor || meshChanged )
			UpdateVolume();
		UpdateCaustics();
		UpdateUnderwater();
	}

	/// <summary>
	/// Align the fluid region before simulation, including on dedicated servers.
	/// </summary>
	protected override void OnFixedUpdate()
	{
		UpdateVolume();
		ApplyBuoyancy();
	}

	/// <summary>
	/// Stop native contacts immediately and release the transient fluid trigger.
	/// </summary>
	void DestroyVolume()
	{
		if ( _volume.IsValid() )
			_volume.Enabled = false;
		_volumeObject?.Destroy();
		_volumeObject = null;
		_volume = null;
		ClearBuoyancy();
	}
}
