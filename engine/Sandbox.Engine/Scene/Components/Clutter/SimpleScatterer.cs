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
		var points = JitteredGridPoints( bounds, pointCount );
		var totalPoints = points.Length;
		var instances = new List<ClutterInstance>( totalPoints );

		if ( totalPoints == 0 )
			return instances;

		var scales = new float[totalPoints];
		var yaws = new float[totalPoints];

		for ( int i = 0; i < totalPoints; i++ )
		{
			scales[i] = Random.Float( Scale.Min, Scale.Max );
			yaws[i] = Random.Float( 0f, 360f );
		}

		SceneTraceResult[] traces = null;
		if ( PlaceOnGround )
		{
			var sceneBounds = scene.GetBounds();
			traces = BatchTraceGround( scene, points, sceneBounds );
		}

		for ( int i = 0; i < totalPoints; i++ )
		{
			var point = points[i];
			var yaw = yaws[i];
			var rotation = Rotation.FromYaw( yaw );

			if ( PlaceOnGround )
			{
				var trace = traces[i];
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
				Transform = new Transform( point, rotation, scales[i] ),
				Entry = entry
			} );
		}

		return instances;
	}
}
