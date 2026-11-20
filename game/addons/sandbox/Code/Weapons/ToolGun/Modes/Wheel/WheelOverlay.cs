internal static class WheelOverlay
{
	static Material _material;

	/// <summary>
	/// Draws a spinning direction arrow overlay on a wheel face.
	/// </summary>
	/// <param name="position">Center of the wheel.</param>
	/// <param name="spinAxis">The wheel's spin axis (points outward from the wheel face).</param>
	/// <param name="stableUp">A stable up reference that doesn't rotate with the wheel.</param>
	/// <param name="radius">Visual radius of the overlay.</param>
	/// <param name="reversed">Whether the spin direction is reversed.</param>
	public static void DrawDirection( Vector3 position, Vector3 spinAxis, Vector3 stableUp, float radius, bool reversed )
	{
		_material ??= Material.Load( "materials/game/wheel_dir.vmat" );

		var spinDir = reversed ? -1f : 1f;
		var spin = Rotation.FromAxis( spinAxis, Time.Now * 90f * spinDir );

		var overlayTrans = new Transform( position + spinAxis * 0.5f );
		overlayTrans.Rotation = spin * Rotation.LookAt( stableUp, spinAxis );
		overlayTrans.Scale = radius;
		overlayTrans.Scale.x *= -spinDir;

		Game.ActiveScene.DebugOverlay.Model( Model.Plane, transform: overlayTrans, overlay: true, materialOveride: _material );
	}
}
