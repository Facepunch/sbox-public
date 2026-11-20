namespace Sandbox;

/// <summary>
/// Identifies one generated builder model. Sizes and parameters are stored in sixteenths, so equal recipes share a
/// model and a key never depends on float noise or string formatting. Parameter slots follow the order in the shape's
/// <see cref="BuilderShapeDefinition"/>; keys are runtime only, saves keep a named <see cref="BuilderRecipe"/>.
/// </summary>
public readonly record struct BuilderShapeKey( BuilderCatalog.Shape Shape, int X, int Y, int Z, bool Fit,
	int P0 = 0, int P1 = 0, int P2 = 0, int P3 = 0, int P4 = 0, int P5 = 0, int P6 = 0, int P7 = 0 )
{
	/// <summary>Steps per unit that sizes and parameters are rounded to.</summary>
	public const float Precision = 16f;

	/// <summary>Smallest and largest custom block size along any axis, in units.</summary>
	public const float MinSize = 1f;
	public const float MaxSize = 8192f;

	/// <summary>Size in units.</summary>
	public Vector3 Size => new Vector3( X, Y, Z ) / Precision;

	public float Parameter( int slot ) => slot switch
	{
		0 => P0,
		1 => P1,
		2 => P2,
		3 => P3,
		4 => P4,
		5 => P5,
		6 => P6,
		7 => P7,
		_ => 0
	} / Precision;

	/// <summary>The same recipe with a different size, for shapes whose size comes from their parameters.</summary>
	public BuilderShapeKey WithSize( Vector3 size ) => this with { X = Quantize( size.x ), Y = Quantize( size.y ), Z = Quantize( size.z ) };

	public BuilderShapeKey WithParameter( int slot, float value )
	{
		var q = Quantize( value );
		return slot switch
		{
			0 => this with { P0 = q },
			1 => this with { P1 = q },
			2 => this with { P2 = q },
			3 => this with { P3 = q },
			4 => this with { P4 = q },
			5 => this with { P5 = q },
			6 => this with { P6 = q },
			7 => this with { P7 = q },
			_ => this
		};
	}

	/// <summary>A parameter by name, or 0 if this shape doesn't have it.</summary>
	public float Parameter( string name )
	{
		var definition = BuilderShapes.Get( Shape );
		var index = definition?.IndexOf( name ) ?? -1;
		return index >= 0 ? Parameter( index ) : 0;
	}

	/// <summary>
	/// A catalog piece keeps its original proportions instead of being fitted to a box,
	/// so saves made before custom sizes existed look exactly the same. Its bend angle goes in slot 0.
	/// </summary>
	public static BuilderShapeKey For( BuilderCatalog.Piece piece )
	{
		var size = piece.Size * BuilderCatalog.Unit;
		return new( piece.Kind, Quantize( size.x ), Quantize( size.y ), Quantize( size.z ), false, Quantize( piece.Angle ) );
	}

	/// <summary>
	/// A custom block whose model exactly fills <paramref name="size"/>, with <paramref name="parameters"/> in the order of
	/// the shape's definition. False for anything a client or a dupe could send that shouldn't be built.
	/// </summary>
	public static bool TryCustom( BuilderCatalog.Shape shape, Vector3 size, ReadOnlySpan<float> parameters, out BuilderShapeKey key )
	{
		key = default;

		var definition = BuilderShapes.Get( shape );
		if ( definition is null ) return false;
		if ( !float.IsFinite( size.x ) || !float.IsFinite( size.y ) || !float.IsFinite( size.z ) ) return false;
		if ( size.x < MinSize || size.y < MinSize || size.z < MinSize ) return false;
		if ( size.x > MaxSize || size.y > MaxSize || size.z > MaxSize ) return false;
		if ( parameters.Length < definition.Parameters.Length ) return false;

		key = new( shape, Quantize( size.x ), Quantize( size.y ), Quantize( size.z ), true );
		for ( int i = 0; i < definition.Parameters.Length; i++ )
		{
			if ( !definition.Parameters[i].TryNormalize( parameters[i], out var value ) ) return false;
			key = key.WithParameter( i, value );
		}

		if ( definition.Validate is null ) return true;

		var validated = definition.Validate( key );
		key = validated ?? default;
		return validated is not null;
	}

	/// <summary>Like <see cref="TryCustom(BuilderCatalog.Shape, Vector3, ReadOnlySpan{float}, out BuilderShapeKey)"/> from a saved parameter string.</summary>
	public static bool TryCustom( BuilderCatalog.Shape shape, Vector3 size, string parameters, out BuilderShapeKey key )
	{
		key = default;
		if ( BuilderShapes.Get( shape ) is not { } definition ) return false;

		Span<float> values = stackalloc float[BuilderShapes.MaxParameters];
		return BuilderShapes.TryParse( definition, parameters, values ) && TryCustom( shape, size, values, out key );
	}

	static int Quantize( float value ) => (int)MathF.Round( value * Precision );

	public override string ToString() => $"{Shape} {X / Precision:0.##}x{Y / Precision:0.##}x{Z / Precision:0.##} {BuilderShapes.Format( this )}";
}

/// <summary>
/// What a building block is made of, by name: a catalog piece, or a shape with a size and its parameters.
/// This is what saves, dupes and dupe previews keep; <see cref="TryGetKey"/> turns it into a model key.
/// </summary>
public sealed record BuilderRecipe( string PieceId, BuilderCatalog.Shape Shape, Vector3 Size, string Parameters )
{
	public bool TryGetKey( out BuilderShapeKey key )
	{
		if ( !string.IsNullOrEmpty( PieceId ) )
		{
			var piece = BuilderCatalog.Find( PieceId );
			key = piece is null ? default : BuilderShapeKey.For( piece );
			return piece is not null;
		}

		return BuilderShapeKey.TryCustom( Shape, Size, Parameters, out key );
	}
}
