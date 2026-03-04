namespace Sandbox.Clutter;

[Expose]
public class SimpleScatterer : Scatterer
{
	/// <summary>
	/// Scale range for spawned objects.
	/// </summary>
	[Property]
	public RangedFloat Scale { get; set; } = new RangedFloat( 0.8f, 1.2f );

	/// <summary>
	/// Points per square meter. 0.05 = sparse trees, 0.5 = dense grass.
	/// </summary>
	[Property, Range( 0.001f, 2f )]
	public float Density { get; set; } = 0.05f;

	[Property, Group( "Placement" )]
	public bool PlaceOnGround { get; set; } = true;

	[Property, Group( "Placement" ), ShowIf( nameof( PlaceOnGround ), true )]
	public float HeightOffset { get; set; }

	[Property, Group( "Placement" ), ShowIf( nameof( PlaceOnGround ), true )]
	public bool AlignToNormal { get; set; }

	protected override List<ClutterInstance> Generate( BBox bounds, ClutterDefinition clutter, Scene scene = null )
	{
		scene ??= Game.ActiveScene;
		if ( scene == null || clutter == null )
			return [];

		var pointCount = CalculatePointCount( bounds, Density );
		var instances = new List<ClutterInstance>( pointCount );

		for ( int i = 0; i < pointCount; i++ )
		{
			var point = new Vector3(
				bounds.Mins.x + Random.Float( bounds.Size.x ),
				bounds.Mins.y + Random.Float( bounds.Size.y ),
				0f
			);

			var scale = Random.Float( Scale.Min, Scale.Max );
			var yaw = Random.Float( 0f, 360f );
			var rotation = Rotation.FromYaw( yaw );

			if ( PlaceOnGround )
			{
				var trace = TraceGround( scene, point );
				if ( !trace.Hit )
					continue;

				point = trace.HitPosition + trace.Normal * HeightOffset;
				rotation = AlignToNormal
					? GetAlignedRotation( trace.Normal, yaw )
					: Rotation.FromYaw( yaw );
			}

			var entry = GetRandomEntry( clutter );
			if ( entry == null )
				continue;

			instances.Add( new ClutterInstance
			{
				Transform = new Transform( point, rotation, scale ),
				Entry = entry
			} );
		}

		return instances;
	}
}
