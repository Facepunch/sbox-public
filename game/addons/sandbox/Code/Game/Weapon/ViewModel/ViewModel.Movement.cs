public partial class ViewModel
{
	/// <summary>
	/// Moves and tilts the first-person model with the player's stride.
	/// </summary>
	[Property, Group( "Movement" )]
	public bool MovementBob { get; set; }

	void ApplyMovementBob( Player player, Rotation eyeRotation, ref Vector3 position, ref Rotation rotation )
	{
		var (vertical, lateral) = player.GetViewModelBob();
		if ( vertical == 0f && lateral == 0f ) return;

		// The lift stays vertical even when the player looks up or down.
		position += (eyeRotation.Forward + Vector3.Up) * (vertical * 0.1f);
		position += eyeRotation.Right * (lateral * 0.8f);

		var tilt = new Angles( -vertical * 0.4f, -lateral * 0.3f, vertical * 0.5f );
		rotation = (rotation.Angles() + tilt).ToRotation();
	}
}
