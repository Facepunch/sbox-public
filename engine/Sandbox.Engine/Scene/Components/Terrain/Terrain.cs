using NativeEngine;

namespace Sandbox;

/// <summary>
/// Terrain renders heightmap based terrain.
/// </summary>
[Expose]
[Category( "World" )]
public sealed partial class Terrain : Collider, Component.ExecuteInEditor
{
	[ConVar( "r_terrain_displacement" )]
	internal static bool UseVertexDisplacement { get; set; } = true;

	public override bool IsConcave => true;

	protected override void OnEnabled()
	{
		Create();
		Transform.OnTransformChanged += OnTerrainChanged;
		Storage?.MaterialSettings?.OnChanged += OnTerrainChanged;
	}

	protected override void OnDisabled()
	{
		Transform.OnTransformChanged -= OnTerrainChanged;
		Storage?.MaterialSettings?.OnChanged -= OnTerrainChanged;

		_so?.Delete();
		_so = null;

		HeightMap?.Dispose();
		ControlMap?.Dispose();
	}

	void OnTerrainChanged()
	{
		if ( !_so.IsValid() ) return;
		_so.Transform = WorldTransform;
		UpdateTerrainBuffer();
	}

	protected override void OnPreRender()
	{
		if ( !_so.IsValid() )
			return;

		_so.Attributes.Set( "VertexDisplacement", UseVertexDisplacement );
	}

	protected override void OnTagsChanged()
	{
		base.OnTagsChanged(); // Collider
		_so?.Tags.SetFrom( Tags );
	}

	/// <summary>
	/// Call on enable or storage change
	/// </summary>
	public void Create()
	{
		if ( !Active )
			return;

		_so?.Delete();
		_so = null;

		HeightMap?.Dispose();
		ControlMap?.Dispose();

		if ( Storage is null )
			return;

		if ( !Application.IsHeadless )
		{
			CreateTextureMaps();
			CreateClipmapSceneObject();

			UpdateTerrainBuffer();
			UpdateMaterialsBuffer();
		}

		// Rebuild the collider
		Rebuild();
	}

	protected override void DrawGizmos()
	{
		base.DrawGizmos();

		if ( !Gizmo.Settings.Selection )
			return;

		if ( Gizmo.IsSelected )
		{
			Gizmo.Draw.Color = Color.White.WithAlpha( 0.4f );
			if ( Storage != null )
				Gizmo.Draw.LineBBox( new BBox( Vector3.Zero, new Vector3( Storage.TerrainSize, Storage.TerrainSize, Storage.TerrainHeight ) ) );
		}

		// Oh this is so bad
		if ( RayIntersects( Gizmo.CurrentRay, Gizmo.RayDepth, out var hitPosition ) )
		{
			Gizmo.Hitbox.TrySetHovered( hitPosition );
		}
	}

	/// <summary>
	/// Given a world ray, finds out the LOCAL position it intersects with this terrain.
	/// </summary>
	public unsafe bool RayIntersects( Ray ray, float distance, out Vector3 position )
	{
		position = default;

		if ( Storage is null )
			return false;

		if ( Storage.HeightMap is null )
			return false;

		if ( Storage.Resolution <= 0 )
			throw new InvalidOperationException( "Terrain resolution must be positive." );

		int resolutionSquared;
		try
		{
			resolutionSquared = checked(Storage.Resolution * Storage.Resolution);
		}
		catch ( OverflowException )
		{
			throw new InvalidOperationException( "Terrain resolution is too large." );
		}

		if ( Storage.HeightMap.Length != resolutionSquared )
			throw new InvalidOperationException( "Terrain data is invalid." );

		var sizeScale = Storage.TerrainSize / Storage.Resolution;
		var heightScale = Storage.TerrainHeight / ushort.MaxValue;
		var offset = new Transform( new Vector3( 0.5f, 0.5f ) * sizeScale, Rotation.From( 0, 0, 90 ) );

		// Transform ray into local space
		ray = ray.ToLocal( WorldTransform.ToWorld( offset ) );

		fixed ( ushort* heights = &Storage.HeightMap[0] )
		{
			if ( g_pPhysicsSystem.CastHeightField(
				out position,
				ray.Position,
				ray.ProjectSafe( distance ),
				(IntPtr)heights,
				Storage.Resolution,
				Storage.Resolution,
				sizeScale,
				heightScale ) )
			{
				position = offset.PointToWorld( position );

				return true;
			}
		}

		return false;
	}

	private void CreateTextureMaps()
	{
		// Absolutely fucked if these aren't valid
		Assert.NotNull( Storage );
		Assert.NotNull( Storage.HeightMap );
		Assert.NotNull( Storage.ControlMap );

		HeightMap?.Dispose();
		ControlMap?.Dispose();

		HeightMap = Texture.Create( Storage.Resolution, Storage.Resolution, ImageFormat.R16 )
			.WithData( new ReadOnlySpan<ushort>( Storage.HeightMap ) )
			.WithUAVBinding()
			.WithName( "terrain_heightmap" )
			.Finish();

		ControlMap = Texture.Create( Storage.Resolution, Storage.Resolution, ImageFormat.R32_UINT )
			.WithData( new ReadOnlySpan<UInt32>( Storage.ControlMap ) )
			.WithUAVBinding()
			.WithName( "terrain_controlmap" )
			.Finish();
	}
}
