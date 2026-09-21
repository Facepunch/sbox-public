namespace Sandbox;

public readonly ref partial struct Painter
{
	/// <summary>
	/// 2D affine transform applied before the destination transform. Defaults to identity.
	/// </summary>
	public Matrix Transform
	{
		get => ActiveContext.State.Transform;
		set
		{
			if ( !float.IsFinite( value.M11 ) || !float.IsFinite( value.M12 ) || !float.IsFinite( value.M21 )
				|| !float.IsFinite( value.M22 ) || !float.IsFinite( value.M41 ) || !float.IsFinite( value.M42 )
				|| value.M13 != 0 || value.M14 != 0 || value.M23 != 0 || value.M24 != 0
				|| value.M31 != 0 || value.M32 != 0 || value.M33 != 1 || value.M34 != 0 || value.M43 != 0 || value.M44 != 1 )
				throw new ArgumentException( "Expected a finite 2D affine matrix.", nameof( value ) );

			ActiveContext.State.Transform = value;
		}
	}

	/// <summary>
	/// Moves the drawing origin along its current axes.
	/// </summary>
	public void Translate( Vector2 offset ) => Translate( offset.x, offset.y );

	/// <summary>
	/// Moves the drawing origin along its current axes.
	/// </summary>
	public void Translate( float x, float y ) => Transform = Matrix.CreateTranslation( new Vector3( x, y, 0 ) ) * Transform;

	/// <summary>
	/// Rotates the drawing axes clockwise in degrees around their current origin.
	/// </summary>
	public void Rotate( float degrees ) => Transform = Matrix.CreateRotationZ( degrees ) * Transform;

	/// <summary>
	/// Scales both drawing axes around their current origin, including stroke widths and text.
	/// </summary>
	public void Scale( float scale ) => Scale( scale, scale );

	/// <summary>
	/// Scales each drawing axis around the current origin.
	/// </summary>
	public void Scale( Vector2 scale ) => Scale( scale.x, scale.y );

	/// <summary>
	/// Scales each drawing axis around the current origin.
	/// </summary>
	public void Scale( float x, float y ) => Transform = Matrix.CreateScale( new Vector3( x, y, 1 ) ) * Transform;
}
