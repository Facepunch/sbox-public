using Sandbox;

/// <summary>
/// You're not seeing things, QT uses fucking doubles
/// </summary>
internal struct QRectF
{
	public double x;
	public double y;
	public double w;
	public double h;

	public static implicit operator QRectF( in Rect value )
	{
		return new QRectF { x = value.Left, y = value.Top, w = value.Width, h = value.Height };
	}

	public readonly Rect Rect => new( (float)x, (float)y, (float)w, (float)h );

}
