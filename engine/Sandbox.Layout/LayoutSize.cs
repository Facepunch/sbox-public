namespace Sandbox.Layout;

/// <summary>
/// A width/height pair. Either component may be undefined (NaN).
/// </summary>
internal struct LayoutSize : IEquatable<LayoutSize>
{
	public float Width;
	public float Height;

	public LayoutSize( float width, float height )
	{
		Width = width;
		Height = height;
	}

	public static readonly LayoutSize Zero = new( 0, 0 );
	public static readonly LayoutSize Undefined = new( Num.Undefined, Num.Undefined );

	public float this[Dimension dimension]
	{
		readonly get => dimension == Dimension.Width ? Width : Height;
		set
		{
			if ( dimension == Dimension.Width )
			{
				Width = value;
			}
			else
			{
				Height = value;
			}
		}
	}

	public readonly bool Equals( LayoutSize other )
	{
		return Num.OptionalEquals( Width, other.Width )
			&& Num.OptionalEquals( Height, other.Height );
	}

	public override readonly bool Equals( object obj ) => obj is LayoutSize other && Equals( other );
	public override readonly int GetHashCode() => HashCode.Combine( Width, Height );
	public override readonly string ToString() => $"{Width} x {Height}";
}
