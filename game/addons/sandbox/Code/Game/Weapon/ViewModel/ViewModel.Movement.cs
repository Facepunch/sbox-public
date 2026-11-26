public partial class ViewModel
{
	/// <summary>
	/// Moves and tilts the first-person model with the player's stride.
	/// </summary>
	[Property, Group( "Movement" )]
	public bool MovementBob { get; set; }

	/// <summary>
	/// Multiplies the bob while aiming down sights (on weapons with an <see cref="IronSightsWeapon"/>), eased in
	/// with the aim - below 1 keeps the sights steadier, 0 stops the bob.
	/// </summary>
	[Property, Title( "ADS Bob Scale" ), Group( "Movement" ), ShowIf( nameof( MovementBob ), true )]
	public float ADSBobScale { get; set; } = 0.15f;

	void ApplyMovementBob( Player player, Rotation eyeRotation, ref Vector3 position, ref Rotation rotation )
	{
		var (vertical, lateral) = player.GetViewModelBob();

		if ( GetComponentInParent<IronSightsWeapon>() is { } sights )
		{
			var scale = MathX.Lerp( 1f, ADSBobScale, sights.AimBlend );
			vertical *= scale;
			lateral *= scale;
		}

		if ( vertical == 0f && lateral == 0f ) return;

		// The lift stays vertical even when the player looks up or down.
		position += (eyeRotation.Forward + Vector3.Up) * (vertical * 0.1f);
		position += eyeRotation.Right * (lateral * 0.8f);

		var tilt = new Angles( -vertical * 0.4f, -lateral * 0.3f, vertical * 0.5f );
		rotation = (rotation.Angles() + tilt).ToRotation();
	}
}
