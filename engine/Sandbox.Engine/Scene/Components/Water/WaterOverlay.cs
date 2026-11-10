namespace Sandbox;

/// <summary>
/// Projects a colour/alpha texture onto the water surface at this object's position.
/// Leaves, dirt, scum and foam can use rough coverage; oil can use low roughness and translucent tint.
/// </summary>
[Expose, Title( "Water Overlay" ), Category( "Rendering" ), Icon( "layers" )]
public sealed class WaterOverlay : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// Automatically resolved water surface at the object's position. Uses the nearest active
	/// surface footprint. Finite surfaces take precedence at equal distances.
	/// </summary>
	public WaterBody Body => Scene is { } scene ? WaterBody.FindSurface( scene, WorldPosition ) : null;

	/// <summary>
	/// Colour and coverage alpha. Defaults to the existing standalone foam wake.
	/// </summary>
	[Property]
	public Texture Texture { get; set; } = Sandbox.Texture.Load( "textures/water/splashwake1.png" );

	/// <summary>
	/// Footprint along the object's projected X and Y axes, in inches, multiplied by its XY scale.
	/// </summary>
	[Property]
	public Vector2 Size { get; set; } = new( 128, 128 );

	/// <summary>
	/// Colour tint and opacity. Lower alpha leaves some water transmission visible.
	/// </summary>
	[Property]
	public Color Tint { get; set; } = Color.White;

	/// <summary>
	/// Surface roughness under the texture's alpha. Use low values for oil and higher values for foam or debris.
	/// </summary>
	[Property, Range( 0, 1 )]
	public float Roughness { get; set; } = 0.8f;

	/// <summary>
	/// Transmission through covered pixels. Zero suits opaque debris and foam;
	/// higher values allow clear oil films to change roughness without hiding the water below.
	/// </summary>
	[Property, Range( 0, 1 )]
	public float Transmission { get; set; }

	/// <summary>
	/// Maximum texture displacement in inches from the animated water normals.
	/// Zero disables texture distortion; lighting still uses the animated water normals.
	/// </summary>
	[Property, Range( 0, 32 )]
	public float NormalDistortion { get; set; } = 2;

	/// <summary>
	/// Higher layers blend over lower ones. Equal layers use a stable component identity order.
	/// </summary>
	[Property]
	public int SortLayer { get; set; }

	/// <summary>
	/// Four aligned vectors matching WaterOverlayData in the water shader.
	/// </summary>
	internal struct Data
	{
		internal Vector4 U;
		internal Vector4 V;
		internal Vector4 Tint;
		internal Vector4 Settings;
	}

	/// <summary>
	/// Project the authored heading onto the body's plane, including rotated water bodies.
	/// </summary>
	internal Transform Projection( WaterBody body )
	{
		var heading = WorldRotation.Forward;
		heading -= body.WorldRotation.Up * Vector3.Dot( heading, body.WorldRotation.Up );
		if ( heading.LengthSquared < 0.0001f )
			heading = body.WorldRotation.Forward;

		var point = body.ToSurface( WorldPosition );
		return new Transform( body.FromSurface( point.WithZ( body.SampleHeight( point ) ) ),
			Rotation.LookAt( heading.Normal, body.WorldRotation.Up ), 1 );
	}

	internal Vector2 Footprint => new( MathF.Max( 0.01f, MathF.Abs( Size.x * WorldScale.x ) ),
		MathF.Max( 0.01f, MathF.Abs( Size.y * WorldScale.y ) ) );

	/// <summary>
	/// Pack world-sized water-plane coordinates into texture UV rows without creating render objects.
	/// </summary>
	internal Data GetData( WaterBody body )
	{
		var projection = Projection( body );
		var center = body.WorldRotation.Inverse * (projection.Position - body.WorldPosition);
		var size = Footprint;
		var u = new Vector2( Vector3.Dot( projection.Rotation.Forward, body.WorldRotation.Forward ), Vector3.Dot( projection.Rotation.Forward, body.WorldRotation.Left ) ) / size.x;
		var v = new Vector2( Vector3.Dot( projection.Rotation.Left, body.WorldRotation.Forward ), Vector3.Dot( projection.Rotation.Left, body.WorldRotation.Left ) ) / size.y;
		return new Data
		{
			U = new Vector4( u.x, u.y, 0.5f - u.x * center.x - u.y * center.y, 0 ),
			V = new Vector4( v.x, v.y, 0.5f - v.x * center.x - v.y * center.y, 0 ),
			Tint = new Vector4( Tint.r, Tint.g, Tint.b, Math.Clamp( Tint.a, 0, 1 ) ),
			Settings = new Vector4( Texture.Index, Math.Clamp( Roughness, 0.001f, 1 ), Math.Clamp( NormalDistortion, 0, 32 ), Math.Clamp( Transmission, 0, 1 ) )
		};
	}

	/// <summary>
	/// Show the projected footprint when the overlay is selected.
	/// </summary>
	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected )
			return;

		var body = Body;
		if ( !body.IsValid() )
			return;

		using ( Gizmo.Scope() )
		{
			Gizmo.Transform = Projection( body );
			var size = Footprint;
			Gizmo.Draw.LineBBox( BBox.FromPositionAndSize( Vector3.Zero,
				new Vector3( size.x, size.y, 2 ) ) );
		}
	}
}
