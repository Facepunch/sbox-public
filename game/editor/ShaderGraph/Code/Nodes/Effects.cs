
namespace Editor.ShaderGraph.Nodes;

/// <summary>
/// Calculates a Fresnel term.
/// </summary>
[Title( "Fresnel" ), Category( "Effects" )]
public sealed class Fresnel : ShaderNode
{
	/// <summary>
	/// Normal at the point being shaded.
	/// </summary>
	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput Normal { get; set; }

	/// <summary>
	/// Direction of the viewer's eye.
	/// </summary>
	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput Direction { get; set; }

	/// <summary>
	/// Power that controls the strength of the Fresnel effect.
	/// </summary>
	[Input( typeof( float ) )]
	[Hide]
	public NodeInput Power { get; set; }

	/// <summary>
	/// Default value for when Power input is missing.
	/// </summary>
	[InputDefault( nameof( Power ) )]
	public float DefaultPower { get; set; } = 10.0f;

	[Output( typeof( float ) )]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var power = compiler.ResultOrDefault( Power, DefaultPower ).Cast( 1 );
		var normal = Normal.IsValid ? compiler.Result( Normal ).Cast( 3 ) : "i.vNormalWs";
		var direction = Direction.IsValid ? compiler.Result( Direction ).Cast( 3 ) :
			$"CalculatePositionToCameraDirWs( {(compiler.IsVs ? "i.vPositionWs.xyz" : "i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz")} )";

		return new( NodeResultType.Vector3, $"pow( 1.0 - dot( normalize( {normal} ), normalize( {direction} ) ), {power} )" );
	};
}
