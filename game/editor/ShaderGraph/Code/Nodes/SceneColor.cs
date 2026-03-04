namespace Editor.ShaderGraph.Nodes;


/// <summary>
/// Blends two normal maps together, normalizing to return an appropriate normal.
/// </summary>
[Title( "Scene Color" ), Category( "Variables" ), Icon( "palette" )]
public sealed class SceneColor : ShaderNode
{
	[Sandbox.Hide]
	public string MapSceneColorCoords => @"
float2 MapSceneColorCoords( float2 vInput, float2 modes )
{
	float2 result;

	// X
	if ( modes.x == 1 ) // Mirror
	{
		float xx = abs( vInput.x );
		result.x = (fmod( floor( xx ), 2.0 ) == 0.0) ? frac( xx ) : 1.0 - frac( xx );
	}
	else if ( modes.x == 2 ) // Clamp
	{
		result.x = clamp( vInput.x, 0.0, 1.0 );
	}
	else if ( modes.x == 3 ) // Border
	{
		result.x = (vInput.x < 0.0 || vInput.x > 1.0) ? 0.5 : vInput.x;
	}
	else if ( modes.x == 4 ) // MirrorOnce
	{
        float xx = abs( vInput.x );
		float floorX = floor( xx );
		if ( floorX < 1.0 )
		{
			result.x = frac( xx );
		}
		else if ( floorX < 2.0 )
		{
			result.x = 1.0 - frac( xx );
		}
		else
		{
			result.x = vInput.x;
		}
	}
	else // Wrap by default
	{
		result.x = vInput.x;
	}

	// Y
	if ( modes.y == 1 ) // Mirror
	{
		float yy = abs( vInput.y );
		result.y = (fmod( floor( yy ), 2.0 ) == 0.0) ? frac( yy ) : 1.0 - frac( yy );
	}
	else if ( modes.y == 2 ) // Clamp
	{
		result.y = clamp( vInput.y, 0.0, 1.0 );
	}
	else if ( modes.y == 3 ) // Border
	{
		result.y = (vInput.y < 0.0 || vInput.y > 1.0) ? 0.5 : vInput.y;
	}
	else if ( modes.y == 4 ) // MirrorOnce
	{
		float yy = abs( vInput.y );
		float floorY = floor( yy );
		if ( floorY < 1.0 )
		{
			result.y = frac( yy );
		}
		else if ( floorY < 2.0 )
		{
			result.y = 1.0 - frac( yy );
		}
		else
		{
			result.y = vInput.y;
		}
	}
	else // Wrap by default
	{
		result.y = vInput.y;
	}

	return result;
}
";

	[Input( typeof( Vector2 ) )]
	[Hide]
	public NodeInput Coords { get; set; }

	public SamplerAddress AddressU { get; set; } = SamplerAddress.Wrap;
	public SamplerAddress AddressV { get; set; } = SamplerAddress.Wrap;

	[Hide]
	[Output( typeof( Vector3 ) )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var coords = compiler.Result( Coords );
		var graph = compiler.Graph;

		if ( graph.Domain != ShaderDomain.PostProcess && graph.BlendMode != BlendMode.Translucent )
		{
			return NodeResult.Error( $"Graph `{nameof( BlendMode )}` must be set to `{nameof( BlendMode.Translucent )}` in order to use `{DisplayInfo.Name}` while in `{graph.Domain}`" );
		}

		var uvModes = $"float2({(int)AddressU},{(int)AddressV})";
		var func = compiler.RegisterFunction( MapSceneColorCoords );

		if ( graph.Domain is ShaderDomain.PostProcess )
		{
			return new NodeResult( NodeResultType.Vector3, $"g_tColorBuffer.Sample( g_sAniso, {(
				coords.IsValid
				? $"{compiler.ResultFunction( func, coords.Cast( 2 ), uvModes )}"
				: $"CalculateViewportUv( {compiler.ResultFunction( func, "i.vPositionSs.xy", uvModes )} )"
			)} ).rgb" );
		}

		compiler.RegisterGlobal( "bWantsFBCopyTexture", "BoolAttribute( bWantsFBCopyTexture, true );" );
		compiler.RegisterGlobal( "g_tFrameBufferCopyTexture", "Texture2D g_tFrameBufferCopyTexture < Attribute( \"FrameBufferCopyTexture\"); SrgbRead( false ); >;" );
		var sample = $"g_tFrameBufferCopyTexture.Sample( g_sAniso, {(
				coords.IsValid
				? $"{compiler.ResultFunction( func, coords.Cast( 2 ), uvModes )}"
				: $"CalculateViewportUv( {compiler.ResultFunction( func, "i.vPositionSs.xy", uvModes )} )"
		)}{(compiler.IsPreview ? "* g_vFrameBufferCopyInvSizeAndUvScale.zw" : "")}).rgb";
		return new NodeResult( NodeResultType.Vector3, sample );
	};
}
