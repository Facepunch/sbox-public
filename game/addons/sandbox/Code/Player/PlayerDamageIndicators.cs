/// <summary>
/// Displays fading HUD indicators pointing toward recent damage sources.
/// </summary>
public sealed class PlayerDamageIndicators : Component, Local.IPlayerEvents
{
	[RequireComponent] Player Player { get; set; }

	float RadialDistanceFromCenter => 128f;
	float RadialIndicatorLifetime => 2f;

	List<(Vector3 WorldPos, TimeSince Lifetime)> radialIndicators = new();

	/// <summary>
	/// Texture drawn around the crosshair to indicate the direction of incoming damage.
	/// </summary>
	[Property] public Texture RadialDamageIcon { get; set; }

	protected override void OnPreRender()
	{
		if ( !Player.IsLocalPlayer ) return;
		if ( Scene.Camera is null ) return;

		UpdateRadialIndicators();
	}

	/// <summary>
	/// Draws each active damage indicator and removes indicators whose lifetime has elapsed.
	/// </summary>
	void UpdateRadialIndicators()
	{
		if ( RadialDamageIcon is null )
			return;

		using var painter = Scene.Camera.BeginHud();
		var playerPos = Player.EyeTransform.Position;
		var playerRot = Player.EyeTransform.Rotation;
		var center = Screen.Size / 2f;

		// rough approx of where the crosshair is in worldspace, makes close-up directions more easily parsable/accurate
		var focalPoint = playerPos + playerRot.Forward * 16;

		for ( int i = radialIndicators.Count - 1; i >= 0; i-- )
		{
			var entry = radialIndicators[i];
			if ( entry.Lifetime >= RadialIndicatorLifetime )
			{
				radialIndicators.RemoveAt( i );
				continue;
			}

			var dir = (entry.WorldPos - focalPoint).Normal;
			var angle = -MathF.Atan2( dir.y, dir.x ) + playerRot.Angles().yaw.DegreeToRadian() - (MathF.PI / 2f);

			using var indicatorScope = painter.Scope();
			painter.Translate( center );
			painter.Rotate( angle.RadianToDegree() );

			var size = new Vector2( 256, 512 ) * Hud.Scale;
			var rect = new Rect( new Vector2( RadialDistanceFromCenter * Hud.Scale, -size.y / 2 ), size );

			// scale alpha based on damage dealt or something?
			painter.Texture( RadialDamageIcon, rect, Color.Red.WithAlpha( 1f - (entry.Lifetime / RadialIndicatorLifetime) ) );
		}
	}

	void Local.IPlayerEvents.OnDamage( PlayerDamageParams args )
	{
		if ( !args.Attacker.IsValid() ) return;
		
		radialIndicators.Add( (args.Attacker.WorldPosition, 0f) );
	}
}
