public sealed class BuilderShapeMenu
{
	const float DeadZone = 4f;

	const float Reach = 12f;

	Vector2 _pointer;

	public bool IsOpen { get; private set; }

	public IReadOnlyList<BuilderCatalog.Shape> Shapes { get; } = Enum.GetValues<BuilderCatalog.Shape>();

	public BuilderCatalog.Shape? Hovered { get; private set; }

	public float AngleOf( int index ) => index * SliceAngle;

	public float SliceAngle => 360f / Shapes.Count;

	public void Open()
	{
		IsOpen = true;
		_pointer = default;
		Hovered = null;
	}

	public void Close() => IsOpen = false;

	public void Move( Angles look )
	{
		_pointer += new Vector2( -look.yaw, look.pitch );
		if ( _pointer.Length > Reach ) _pointer = _pointer.Normal * Reach;

		if ( _pointer.Length < DeadZone )
		{
			Hovered = null;
			return;
		}

		var angle = MathF.Atan2( _pointer.x, -_pointer.y ).RadianToDegree();
		var index = (int)MathF.Round( angle / SliceAngle );
		Hovered = Shapes[((index % Shapes.Count) + Shapes.Count) % Shapes.Count];
	}
}
