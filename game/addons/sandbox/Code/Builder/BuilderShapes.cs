namespace Sandbox;

/// <summary>
/// The shape-local axis that points along the surface a block is built on.
/// </summary>
public enum BuilderUpAxis
{
	Y,

	Z
}

/// <summary>
/// A setting of its own that a shape (or some of its subtypes) has, like a pipe's wall thickness.
/// Saved by <see cref="Name"/>, so parameters can be added or reordered without breaking saves.
/// </summary>
public sealed record BuilderParameter( string Name, float Default, float Min, float Max, float[] Choices = null )
{
	/// <summary>
	/// The value to build with: one of <see cref="Choices"/> when there are any, otherwise clamped to the range.
	/// False for anything that isn't a usable number or choice.
	/// </summary>
	public bool TryNormalize( float value, out float result )
	{
		result = Default;
		if ( !float.IsFinite( value ) ) return false;

		if ( Choices is not null )
		{
			foreach ( var choice in Choices )
			{
				if ( MathF.Abs( choice - value ) > 0.01f ) continue;
				result = choice;
				return true;
			}

			return false;
		}

		result = Math.Clamp( value, Min, Max );
		return true;
	}
}

/// <summary>
/// Everything that differs between shapes besides their geometry: which way is up, their own parameters, and the
/// proportions Shift keeps. Add a shape here, give it parameters here, and read them in <see cref="BuilderGeometry"/>.
/// </summary>
public sealed class BuilderShapeDefinition
{
	/// <summary>The shape axis that follows the surface normal.</summary>
	public BuilderUpAxis Up { get; init; } = BuilderUpAxis.Z;

	/// <summary>The shape's own settings, at most <see cref="BuilderShapes.MaxParameters"/>.</summary>
	public BuilderParameter[] Parameters { get; init; } = [];

	/// <summary>
	/// Shape-local X:Y:Z proportions that Shift keeps while drawing, so subtypes can differ (a 180° bend is 2:1).
	/// A zero leaves that side free (a half pipe's length). Null means 1:1:1.
	/// </summary>
	public Func<BuilderShapeKey, Vector3> Proportions { get; init; }

	/// <summary>
	/// Last say over a recipe: adjust it to what will really be built (so equal meshes share a key), or return null if
	/// this shape can't be built at that size. Null hook accepts everything.
	/// </summary>
	public Func<BuilderShapeKey, BuilderShapeKey?> Validate { get; init; }

	public int IndexOf( string name )
	{
		for ( int i = 0; i < Parameters.Length; i++ )
		{
			if ( string.Equals( Parameters[i].Name, name, StringComparison.OrdinalIgnoreCase ) ) return i;
		}

		return -1;
	}

	public Vector3 GetProportions( BuilderShapeKey key ) => Proportions?.Invoke( key ) ?? Vector3.One;

	public Func<BuilderShapeKey, BuilderFaces> FlatFaces { get; init; }

	public BuilderFaces GetFlatFaces( BuilderShapeKey key ) => FlatFaces?.Invoke( key ) ?? BuilderFaces.All;

	/// <summary>How the Builder tool draws this shape.</summary>
	public BuilderPlacementMode Placement { get; init; } = BuilderPlacementMode.Box;

	public bool FitsBox { get; init; } = true;

	/// <summary>The model's bounds in the block's frame, for shapes that aren't centred on their size. Null hook or result means a centred box.</summary>
	public Func<BuilderShapeKey, BBox?> Bounds { get; init; }

	public BBox GetBounds( BuilderShapeKey key ) => Bounds?.Invoke( key ) ?? BBox.FromPositionAndSize( Vector3.Zero, key.Size );
}

/// <summary>
/// How the Builder tool draws a shape.
/// </summary>
public enum BuilderPlacementMode
{
	/// <summary>Two corners of a footprint on a surface, then a height.</summary>
	Box,

	/// <summary>A start point, an end point on any surface, then the tube radius (bent pipes).</summary>
	Path
}

/// <summary>
/// Sides of a shape's local box.
/// </summary>
[Flags]
public enum BuilderFaces
{
	None = 0,
	PositiveX = 1,
	NegativeX = 2,
	PositiveY = 4,
	NegativeY = 8,
	PositiveZ = 16,
	NegativeZ = 32,

	Caps = PositiveZ | NegativeZ,
	All = 63
}

/// <summary>
/// The definition of every <see cref="BuilderCatalog.Shape"/>.
/// </summary>
public static class BuilderShapes
{
	/// <summary>How many parameters a shape can have; the slots in <see cref="BuilderShapeKey"/>.</summary>
	public const int MaxParameters = 8;

	public const string Radius = "radius";
	public const string Thickness = "thickness";

	static readonly BuilderParameter WallThickness = new( Thickness, 6, 1, 256 );

	static readonly BuilderShapeDefinition[] Definitions = Create();

	/// <summary>The face flag for shape axis <paramref name="axis"/> (0 X, 1 Y, 2 Z) on side <paramref name="sign"/>.</summary>
	public static BuilderFaces Face( int axis, int sign ) => (BuilderFaces)(1 << (axis * 2 + (sign < 0 ? 1 : 0)));

	public static BuilderShapeDefinition Get( BuilderCatalog.Shape shape )
	{
		var index = (int)shape;
		return index >= 0 && index < Definitions.Length ? Definitions[index] : null;
	}

	static BuilderShapeDefinition[] Create()
	{
		var shapes = Enum.GetValues<BuilderCatalog.Shape>();
		var definitions = new BuilderShapeDefinition[shapes.Length];

		foreach ( var shape in shapes )
		{
			definitions[(int)shape] = shape switch
			{
				BuilderCatalog.Shape.Triangle or BuilderCatalog.Shape.Carved => new()
				{
					Up = BuilderUpAxis.Y,
					FlatFaces = _ => BuilderFaces.Caps
				},

				BuilderCatalog.Shape.BoxFrame => new()
				{
					Parameters = [WallThickness],
					Validate = key => ClampThickness( key, 0, MathF.Min( key.Size.x, MathF.Min( key.Size.y, key.Size.z ) ) / 2 )
				},

				BuilderCatalog.Shape.Trapezium or BuilderCatalog.Shape.Parallelogram => new()
				{
					Up = BuilderUpAxis.Y,
					FlatFaces = _ => BuilderFaces.Caps
				},

				BuilderCatalog.Shape.Pentagon => new()
				{
					Up = BuilderUpAxis.Y,
					FlatFaces = _ => BuilderFaces.Caps
				},

				BuilderCatalog.Shape.HalfPipe => new()
				{
					Proportions = _ => new Vector3( 2, 1, 0 ),
					Up = BuilderUpAxis.Y,
					Parameters = [WallThickness],
					Validate = key => ClampThickness( key, 0, MathF.Min( key.Size.x / 2, key.Size.y ) ),
					FlatFaces = _ => BuilderFaces.NegativeY | BuilderFaces.Caps
				},

				BuilderCatalog.Shape.Tube => new()
				{
					Parameters = [WallThickness],
					Validate = key => ClampThickness( key, 0, MathF.Min( key.Size.x, key.Size.y ) / 2 ),
					FlatFaces = _ => BuilderFaces.Caps
				},

				BuilderCatalog.Shape.PlateHole => new()
				{
					Parameters = [WallThickness],
					Validate = key => ClampThickness( key, 0, MathF.Min( key.Size.x, key.Size.y ) / 2 )
				},

				BuilderCatalog.Shape.Dome => new()
				{
					Proportions = _ => new Vector3( 1, 1, 0.5f ),
					Parameters = [WallThickness],
					Validate = key => ClampThickness( key, 0, MathF.Min( MathF.Min( key.Size.x, key.Size.y ) / 2, key.Size.z ) ),
					FlatFaces = _ => BuilderFaces.NegativeZ
				},

				BuilderCatalog.Shape.QuarterDome => new()
				{
					Proportions = _ => new Vector3( 2, 1, 1 ),
					Parameters = [WallThickness],
					Validate = key => ClampThickness( key, 0, MathF.Min( MathF.Min( key.Size.x / 2, key.Size.y ), key.Size.z ) ),
					FlatFaces = _ => BuilderFaces.NegativeZ | BuilderFaces.NegativeY
				},

				BuilderCatalog.Shape.Bend => new()
				{
					Placement = BuilderPlacementMode.Path,
					FitsBox = false,
					Parameters =
					[
						WallThickness,
						new( Radius, 8, 0.5f, 1024 ),
						new( "endx", 0, -BuilderShapeKey.MaxSize, BuilderShapeKey.MaxSize ),
						new( "endy", 0, -BuilderShapeKey.MaxSize, BuilderShapeKey.MaxSize ),
						new( "endz", 0, -BuilderShapeKey.MaxSize, BuilderShapeKey.MaxSize ),
						new( "endnx", 0, -BuilderPipe.NormalScale, BuilderPipe.NormalScale ),
						new( "endny", 0, -BuilderPipe.NormalScale, BuilderPipe.NormalScale ),
						new( "endnz", 0, -BuilderPipe.NormalScale, BuilderPipe.NormalScale )
					],
					Validate = BuilderPipe.Validate,
					Bounds = key => key.Fit && BuilderPipe.TryFromKey( key, out var pipe ) ? pipe.Bounds : null,
					FlatFaces = key => key.Fit ? BuilderFaces.None : CatalogBendFaces( key )
				},

				BuilderCatalog.Shape.Ring => new() { FlatFaces = _ => BuilderFaces.Caps },
				BuilderCatalog.Shape.Cone => new() { FlatFaces = _ => BuilderFaces.NegativeZ },
				BuilderCatalog.Shape.MirroredCone or BuilderCatalog.Shape.Sphere => new() { FlatFaces = _ => BuilderFaces.None },

				BuilderCatalog.Shape.Stairs => new()
				{
					FlatFaces = _ => BuilderFaces.NegativeZ | BuilderFaces.PositiveX | BuilderFaces.PositiveY | BuilderFaces.NegativeY
				},

				_ => new()
			};

			if ( definitions[(int)shape].Parameters.Length > MaxParameters )
				throw new InvalidOperationException( $"{shape} has more than {MaxParameters} builder parameters" );
		}

		return definitions;
	}

	/// <summary>The end faces of a catalog bend piece, whose first parameter is its angle.</summary>
	static BuilderFaces CatalogBendFaces( BuilderShapeKey key ) => key.Parameter( 0 ) >= 180 ? BuilderFaces.NegativeY : BuilderFaces.NegativeY | BuilderFaces.NegativeX;

	static BuilderShapeKey? ClampThickness( BuilderShapeKey key, int slot, float radius )
	{
		if ( radius < 1f ) return null;

		var thickness = MathF.Min( key.Parameter( slot ), radius * 0.95f );
		return key.WithParameter( slot, thickness );
	}

	/// <summary>
	/// Read a saved parameter string like <c>angle=90;thickness=6</c> into values in definition order.
	/// Missing names use their default, unknown names are ignored, anything unusable fails.
	/// </summary>
	public static bool TryParse( BuilderShapeDefinition definition, string text, Span<float> values )
	{
		if ( values.Length < definition.Parameters.Length ) return false;

		for ( int i = 0; i < definition.Parameters.Length; i++ )
			values[i] = definition.Parameters[i].Default;

		if ( string.IsNullOrWhiteSpace( text ) ) return true;
		if ( text.Length > 256 ) return false;

		foreach ( var part in text.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
		{
			var equals = part.IndexOf( '=' );
			if ( equals <= 0 ) return false;

			var index = definition.IndexOf( part[..equals].Trim() );
			if ( index < 0 ) continue;

			if ( !float.TryParse( part[(equals + 1)..].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value ) )
				return false;

			if ( !definition.Parameters[index].TryNormalize( value, out values[index] ) ) return false;
		}

		return true;
	}

	/// <summary>
	/// A small buildable example of a shape with these parameters, sized by its proportions (free sides get a quarter).
	/// Stretched over a box the shape can't fill, it still shows what was being drawn.
	/// </summary>
	public static bool TryGetSample( BuilderCatalog.Shape shape, ReadOnlySpan<float> parameters, out BuilderShapeKey key )
	{
		key = default;
		if ( Get( shape ) is not { } definition || parameters.Length < definition.Parameters.Length ) return false;

		var probe = new BuilderShapeKey( shape, 16, 16, 16, true );
		for ( int i = 0; i < definition.Parameters.Length; i++ )
			probe = probe.WithParameter( i, parameters[i] );

		var p = definition.GetProportions( probe );
		var size = new Vector3( p.x > 0 ? p.x : 0.25f, p.y > 0 ? p.y : 0.25f, p.z > 0 ? p.z : 0.25f ) * 64;
		return BuilderShapeKey.TryCustom( shape, size, parameters, out key );
	}

	/// <summary>The parameter string for a key, the inverse of <see cref="TryParse"/>.</summary>
	public static string Format( BuilderShapeKey key )
	{
		var definition = Get( key.Shape );
		if ( definition is null || definition.Parameters.Length == 0 ) return null;

		var text = new System.Text.StringBuilder();
		for ( int i = 0; i < definition.Parameters.Length; i++ )
		{
			if ( i > 0 ) text.Append( ';' );
			text.Append( definition.Parameters[i].Name ).Append( '=' ).Append( key.Parameter( i ).ToString( "0.####", System.Globalization.CultureInfo.InvariantCulture ) );
		}

		return text.ToString();
	}
}
