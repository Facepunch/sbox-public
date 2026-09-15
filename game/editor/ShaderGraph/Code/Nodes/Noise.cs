
namespace Editor.ShaderGraph.Nodes;

public abstract class NoiseNode : ShaderNode
{
	protected virtual string Func { get; }

	[Input( typeof( Vector2 ) )]
	[Hide]
	public NodeInput Coords { get; set; }

	[Output( typeof( float ) ), Title( "Alpha" )]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var result = compiler.Result( Coords );
		return new( 1, $"{Func}( {(result.IsValid ? $"{result.Cast( 2 )}" : "i.vTextureCoords.xy")} )" );
	};
}

/// <summary>
/// Fuzzy noise is identical to the noise of TV static
/// </summary>
[Title( "Fuzzy Noise" ), Category( "Noise" ), Icon( "blur_on" )]
public sealed class FuzzyNoise : NoiseNode
{
	[Hide]
	protected override string Func => "FuzzyNoise";
}

/// <summary>
/// Value noise is a type of noise that creates a pattern of
/// random values on a grid. It typically is used to create patchy
/// areas
/// </summary>
[Title( "Value Noise" ), Category( "Noise" ), Icon( "dashboard" )]
public sealed class ValueNoise : NoiseNode
{
	[Hide]
	protected override string Func => "ValueNoise";
}

/// <summary>
/// Simplex noise is a type of noise that generates smooth and natural looking patterns.
/// This is an improvement to Perlin Noise and is typically used for textures and terrains.
/// </summary>
[Title( "Simplex Noise" ), Category( "Noise" ), Icon( "waves" )]
public sealed class SimplexNoise : NoiseNode
{
	[Hide]
	protected override string Func => "Simplex2D";
}

/// <summary>
/// Voronoi noise generates a collection of cells or cracks.
/// This node can also be used to generate Worley noise, a type of noise which looks more cloud like.
/// </summary>
[Title( "Voronoi Noise" ), Category( "Noise" ), Icon( "ssid_chart" )]
public sealed class VoronoiNoise : ShaderNode
{
	[Input( typeof( Vector2 ) )]
	[Hide]
	public NodeInput Coords { get; set; }

	[Input( typeof( float ) ), Title( "Angle Offset" )]
	[Hide, NodeValueEditor( nameof( AngleOffset ) )]
	[MinMax( 0.0f, 6.28319f )]
	public NodeInput A { get; set; }

	[Input( typeof( float ) ), Title( "Cell Density" )]
	[Hide, NodeValueEditor( nameof( CellDensity ) )]
	[MinMax( 0.0f, 100.0f )]
	public NodeInput B { get; set; }

	[MinMax( 0.0f, 6.28319f )]
	public float AngleOffset { get; set; } = 3.1415926f;

	[MinMax( 0.0f, 100.0f )]
	public float CellDensity { get; set; } = 10.0f;

	/// <summary>
	/// Invert the output to generate Worley noise
	/// </summary>
	public bool Worley { get; set; } = false;

	[Output( typeof( float ) ), Title( "Alpha" )]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var result = compiler.Result( Coords );
		var angleOffset = compiler.Result( A );
		var cellDensity = compiler.Result( B );

		string angleStr = $"{(angleOffset.IsValid ? angleOffset.Cast( 1 ) : compiler.ResultValue( AngleOffset ))}";
		string densityStr = $"{(cellDensity.IsValid ? cellDensity.Cast( 1 ) : compiler.ResultValue( CellDensity ))}";

		return new( 1, $"{(Worley ? "1.0f - " : string.Empty)}VoronoiNoise( {(result.IsValid ? $"{result.Cast( 2 )}" : "i.vTextureCoords.xy")}, {angleStr}, {densityStr} )" );
	};
}
