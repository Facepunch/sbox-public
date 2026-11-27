
namespace Editor.ShaderGraph.Nodes;

/// <summary>
/// Returns the tangent view vector, which is the direction from the camera to the position in tangent space.
/// </summary>
[Title( "Tangent View Vector" ), Category( "Variables" ), Icon( "visibility" )]
public sealed class TangentViewVector : ShaderNode
{
	[Hide]
	public static string GetTangentViewVector => @"
float3 GetTangentViewVector( float3 vCameraToPositionDirWs, float3 vNormalWs, float3 vTangentUWs, float3 vTangentVWs )
{
    vNormalWs = normalize( vNormalWs.xyz );
    float3 vTangentViewVector = Vec3WsToTs( vCameraToPositionDirWs.xyz, vNormalWs.xyz, vTangentUWs.xyz, vTangentVWs.xyz );
	
    // Result
    return vTangentViewVector.xyz;
}
";

	[Title( "Position" ), Hide]
	[Input( typeof( Vector3 ) )]
	public NodeInput WorldPosition { get; set; }

	[Title( "Normal" ), Hide]
	[Input( typeof( Vector3 ) )]
	public NodeInput WorldNormal { get; set; }

	[Title( "World Tangent U" ), Hide]
	[Input( typeof( Vector3 ) )]
	public NodeInput TangentUWs { get; set; }

	[Title( "World Tangent V" ), Hide]
	[Input( typeof( Vector3 ) )]
	public NodeInput TangentVWs { get; set; }

	public TangentViewVector()
	{
		ExpandSize = new Vector2( 32, 0 );
	}

	[Hide]
	[Output( typeof( Vector3 ) )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		if ( compiler.Graph.Domain is ShaderDomain.PostProcess )
		{
			return NodeResult.Error( $"You cannot use `{DisplayInfo.Name}` nodes with post-processing shaders." );
		}

		var worldPosition = compiler.Result( WorldPosition );
		var worldNormal = compiler.Result( WorldNormal );
		var tangentUws = compiler.Result( TangentUWs );
		var tangentVws = compiler.Result( TangentVWs );

		// A surface has no Vec3WsToTs of the engine's: the graph's own
		if ( compiler.IsSurface ) compiler.ResultFunction( "Vec3WsToTs" );
		string func = compiler.RegisterFunction( GetTangentViewVector );
		string funcResult = compiler.ResultFunction( func,
			compiler.CameraToPosition( worldPosition.IsValid ? worldPosition.Code : compiler.WorldPosition ),
			$"{(worldNormal.IsValid ? worldNormal : compiler.WorldNormal)}",
			$"{(tangentUws.IsValid ? tangentUws : compiler.WorldTangentU)}",
			$"{(tangentVws.IsValid ? tangentVws : compiler.WorldTangentV)}"
		);

		return new NodeResult( NodeResultType.Vector3, $"{funcResult}" );
	};
}
