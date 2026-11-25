
/// <summary>
/// A pair of ball sockets, we try to align the balls towards eachother.
/// </summary>
public sealed class BallSocketPairEntity : Component
{
	[Property]
	public SkinnedModelRenderer BallModelA { get; set; }

	[Property]
	public SkinnedModelRenderer BallModelB { get; set; }

	[Property]
	public LineRenderer ShaftRenderer { get; set; }

	protected override void OnUpdate()
	{
		if ( !BallModelA.IsValid() || !BallModelB.IsValid() ) return;

		var ballDir = (BallModelB.GameObject.WorldPosition - BallModelA.GameObject.WorldPosition).Normal;
		var ballUp = MathF.Abs( Vector3.Dot( ballDir, Vector3.Up ) ) > 0.99f ? Vector3.Forward : Vector3.Up;

		BallModelA.GameObject.WorldRotation = Rotation.LookAt( -ballDir, ballUp ) * Rotation.FromPitch( -90f );
		BallModelB.GameObject.WorldRotation = Rotation.LookAt( ballDir, ballUp ) * Rotation.FromPitch( -90f );

		if ( ShaftRenderer.IsValid() )
		{
			var endA = BallModelA.GetBoneObject( "end" );
			var endB = BallModelB.GetBoneObject( "end" );

			if ( endA.IsValid() && endB.IsValid() )
			{
				ShaftRenderer.Points = [endA, endB];
			}
		}
	}
}
