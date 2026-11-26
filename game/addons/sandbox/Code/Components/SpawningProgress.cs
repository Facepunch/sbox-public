public class SpawningProgress : Component
{
	public BBox? SpawnBounds { get; internal set; }

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( SpawnBounds.HasValue )
		{
			for ( int i = 0; i < 8; i++ )
			{
				var color = Color.Lerp( Color.White, Color.Cyan, Random.Shared.Float( 0, 1 ) );
				color = color.WithAlpha( Random.Shared.Float( 0.1f, 0.4f ) );
				var bounds = SpawnBounds.Value;
				bounds.Mins += Vector3.Random * 1.0f;
				bounds.Maxs += Vector3.Random * 1.0f;

				DebugOverlay.Box( bounds, color, transform: WorldTransform );
			}
		}
	}
}
