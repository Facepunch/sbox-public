namespace Sandbox;

/// <summary>
/// Emits particles within a box shape.
/// </summary>
[Title( "Box Emitter" )]
[Category( "Effects" )]
[Icon( "check_box_outline_blank" )]
public sealed class ParticleBoxEmitter : ParticleEmitter
{
	[Property] public Vector3 Size { get; set; } = 50.0f;
	[Property] public bool OnEdge { get; set; } = false;

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected )
			return;

		Gizmo.Draw.Color = Color.White.WithAlpha( 0.1f );
		Gizmo.Draw.LineBBox( BBox.FromPositionAndSize( 0, Size ) );

		// TODO - Box Resize Gizmo

	}

	public override bool Emit( ParticleEffect target )
	{
		var size = Random.Shared.VectorInCube( 0.5f );
		size *= Size;

		if ( OnEdge )
		{
			var face = Random.Shared.Int( 0, 5 );
			if ( face == 0 ) size.x = -Size.x * 0.5f;
			else if ( face == 1 ) size.y = -Size.y * 0.5f;
			else if ( face == 2 ) size.z = -Size.z * 0.5f;
			else if ( face == 3 ) size.x = Size.x * 0.5f;
			else if ( face == 4 ) size.y = Size.y * 0.5f;
			else if ( face == 5 ) size.z = Size.z * 0.5f;
		}

		var pos = WorldPosition + (size * WorldScale) * WorldRotation;

		target.Emit( pos, Delta );

		return true;
	}
}
