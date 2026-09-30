namespace Sandbox.Layout;

/// <summary>
/// A style length: points, a percentage of a reference length, <c>auto</c>, or undefined (unset).
/// </summary>
internal readonly struct StyleLength : IEquatable<StyleLength>
{
	public readonly float Value;
	public readonly Unit Unit;

	private StyleLength( float value, Unit unit )
	{
		Value = value;
		Unit = unit;
	}

	public static StyleLength Points( float value )
	{
		return Num.IsUndefined( value ) || float.IsInfinity( value )
			? Undefined
			: new StyleLength( value, Unit.Point );
	}

	public static StyleLength Percent( float value )
	{
		return Num.IsUndefined( value ) || float.IsInfinity( value )
			? Undefined
			: new StyleLength( value, Unit.Percent );
	}

	public static readonly StyleLength Auto = new( Num.Undefined, Unit.Auto );
	public static readonly StyleLength Undefined = new( Num.Undefined, Unit.Undefined );
	public static readonly StyleLength Zero = new( 0, Unit.Point );

	public bool IsAuto => Unit == Unit.Auto;
	public bool IsUndefined => Unit == Unit.Undefined;
	public bool IsDefined => Unit != Unit.Undefined;
	public bool IsPercent => Unit == Unit.Percent;
	public bool IsPoints => Unit == Unit.Point;

	/// <summary>
	/// Resolve to points against a reference length. Auto and undefined resolve to undefined (NaN),
	/// as does a percentage of an undefined reference.
	/// </summary>
	public float Resolve( float referenceLength )
	{
		return Unit switch
		{
			Unit.Point => Value,
			Unit.Percent => Value * referenceLength * 0.01f,
			_ => Num.Undefined,
		};
	}

	public bool Equals( StyleLength other ) => Unit == other.Unit
		&& Num.OptionalEquals( Value, other.Value );
	public override bool Equals( object obj ) => obj is StyleLength other && Equals( other );
	public override int GetHashCode() => HashCode.Combine( Num.IsUndefined( Value ) ? 0 : Value, (int)Unit );
	public static bool operator ==( StyleLength a, StyleLength b ) => a.Equals( b );
	public static bool operator !=( StyleLength a, StyleLength b ) => !a.Equals( b );

	public static bool InexactEquals( StyleLength a, StyleLength b ) => a.Unit == b.Unit
		&& Num.InexactEquals( a.Value, b.Value );

	public static implicit operator StyleLength( float points ) => Points( points );

	public override string ToString()
	{
		return Unit switch
		{
			Unit.Point => $"{Value}px",
			Unit.Percent => $"{Value}%",
			Unit.Auto => "auto",
			_ => "undefined",
		};
	}
}
