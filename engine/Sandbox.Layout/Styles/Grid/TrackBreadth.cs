namespace Sandbox.Layout;

/// <summary>
/// One side of a track sizing function: a fixed length, a percentage, a flexible <c>fr</c> factor, or one of
/// the intrinsic keywords.
/// </summary>
internal enum TrackBreadthKind : byte
{
	Auto,
	MinContent,
	MaxContent,
	Points,
	Percent,

	/// <summary>Flexible track: <c>1fr</c>. Only valid as the max side of a sizing function.</summary>
	Fraction,
}

internal readonly struct TrackBreadth : IEquatable<TrackBreadth>
{
	public readonly TrackBreadthKind Kind;
	public readonly float Value;

	private TrackBreadth( TrackBreadthKind kind, float value )
	{
		Kind = kind;
		Value = value;
	}

	public static readonly TrackBreadth Auto = new( TrackBreadthKind.Auto, 0 );
	public static readonly TrackBreadth MinContent = new( TrackBreadthKind.MinContent, 0 );
	public static readonly TrackBreadth MaxContent = new( TrackBreadthKind.MaxContent, 0 );

	public static TrackBreadth Points( float value ) => new( TrackBreadthKind.Points, value );
	public static TrackBreadth Percent( float value ) => new( TrackBreadthKind.Percent, value );
	public static TrackBreadth Fraction( float value ) => new( TrackBreadthKind.Fraction, MathF.Max( 0, value ) );

	public bool IsAuto => Kind == TrackBreadthKind.Auto;
	public bool IsMinContent => Kind == TrackBreadthKind.MinContent;
	public bool IsMaxContent => Kind == TrackBreadthKind.MaxContent;
	public bool IsFraction => Kind == TrackBreadthKind.Fraction;
	public bool IsIntrinsic => Kind is TrackBreadthKind.Auto or TrackBreadthKind.MinContent or TrackBreadthKind.MaxContent;
	public bool IsFixed => Kind is TrackBreadthKind.Points or TrackBreadthKind.Percent;

	/// <summary>
	/// Resolve a fixed breadth to points. Percentages against an undefined reference behave as auto
	/// (returns undefined), per css-grid-1 §7.2.1.
	/// </summary>
	public float ResolveFixed( float referenceLength )
	{
		return Kind switch
		{
			TrackBreadthKind.Points => Value,
			TrackBreadthKind.Percent => Num.IsDefined( referenceLength )
				? Value * referenceLength * 0.01f
				: Num.Undefined,
			_ => Num.Undefined,
		};
	}

	public bool Equals( TrackBreadth other ) => Kind == other.Kind && Value == other.Value;
	public override bool Equals( object obj ) => obj is TrackBreadth other && Equals( other );
	public override int GetHashCode() => HashCode.Combine( (int)Kind, Value );

	public override string ToString()
	{
		return Kind switch
		{
			TrackBreadthKind.Auto => "auto",
			TrackBreadthKind.MinContent => "min-content",
			TrackBreadthKind.MaxContent => "max-content",
			TrackBreadthKind.Points => $"{Value}px",
			TrackBreadthKind.Percent => $"{Value}%",
			_ => $"{Value}fr",
		};
	}
}
