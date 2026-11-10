namespace Sandbox.Layout;

internal enum GridPlacementKind : byte
{
	/// <summary><c>auto</c></summary>
	Auto,

	/// <summary>An explicit line number (1-based, negative counts from the end).</summary>
	Line,

	/// <summary><c>span N</c></summary>
	Span,

	/// <summary>A named line, optionally the Nth occurrence: <c>foo</c> / <c>2 foo</c>.</summary>
	NamedLine,

	/// <summary><c>span foo</c> - span until the Nth line named foo.</summary>
	NamedSpan,
}

/// <summary>One side of a grid item's placement (<c>grid-column-start</c> etc).</summary>
internal readonly struct GridPlacement : IEquatable<GridPlacement>
{
	public readonly GridPlacementKind Kind;
	public readonly int Value;
	public readonly string Name;

	private GridPlacement( GridPlacementKind kind, int value, string name )
	{
		Kind = kind;
		Value = value;
		Name = name;
	}

	public static readonly GridPlacement Auto = new( GridPlacementKind.Auto, 0, null );

	public static GridPlacement Line( int line ) => line == 0 ? Auto : new( GridPlacementKind.Line, line, null );
	public static GridPlacement Span( int span ) => new( GridPlacementKind.Span, Math.Max( 1, span ), null );
	public static GridPlacement NamedLine( string name, int index = 1 ) => new( GridPlacementKind.NamedLine, index == 0 ? 1 : index, name );
	public static GridPlacement NamedSpan( string name, int count = 1 ) => new( GridPlacementKind.NamedSpan, Math.Max( 1, count ), name );

	public bool IsAuto => Kind == GridPlacementKind.Auto;
	public bool IsSpan => Kind is GridPlacementKind.Span or GridPlacementKind.NamedSpan;
	public bool IsLine => Kind is GridPlacementKind.Line or GridPlacementKind.NamedLine;

	public bool Equals( GridPlacement other )
	{
		return Kind == other.Kind
			&& Value == other.Value
			&& string.Equals( Name, other.Name, StringComparison.Ordinal );
	}

	public override bool Equals( object obj ) => obj is GridPlacement other && Equals( other );
	public override int GetHashCode() => HashCode.Combine( (int)Kind, Value, Name );
	public static bool operator ==( GridPlacement a, GridPlacement b ) => a.Equals( b );
	public static bool operator !=( GridPlacement a, GridPlacement b ) => !a.Equals( b );

	public override string ToString()
	{
		return Kind switch
		{
			GridPlacementKind.Auto => "auto",
			GridPlacementKind.Line => Value.ToString(),
			GridPlacementKind.Span => $"span {Value}",
			GridPlacementKind.NamedLine => Value == 1 ? Name : $"{Value} {Name}",
			_ => Value == 1 ? $"span {Name}" : $"span {Value} {Name}",
		};
	}
}
