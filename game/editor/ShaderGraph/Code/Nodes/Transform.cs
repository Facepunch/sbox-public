
namespace Editor.ShaderGraph.Nodes;

public enum BlendNodeMode
{
	Mix,
	Darken,
	Multiply,
	ColorBurn,
	LinearBurn,
	Lighten,
	Screen,
	ColorDodge,
	LinearDodge,
	Overlay,
	SoftLight,
	HardLight,
	VividLight,
	LinearLight,
	HardMix,
	Difference,
	Exclusion,
	Subtract,
	Divide,
	Add,
}

/// <summary>
/// Normalize a vector to have a length of 1 unit
/// </summary>
[Title( "Normalize" ), Category( "Transform" ), Icon( "arrow_forward" )]
public sealed class Normalize : Unary
{
	[Hide]
	protected override string Op => "normalize";
}

public enum NormalSpace
{
	Tangent,
	Object,
	World,
}

public enum OutputNormalSpace
{
	Tangent,
	World
}

/// <summary>
/// Transforms a normal from tangent or object space into world space
/// </summary>
[Title( "Transform Normal" ), Category( "Transform" ), Icon( "shortcut" )]
public sealed class TransformNormal : ShaderNode
{
	/// <summary>
	/// Normal input. No input specified will output vertex normal in world space
	/// </summary>
	[Input]
	[Hide]
	public NodeInput Input { get; set; }

	/// <summary>
	/// Space of the input normal, tangent or object.
	/// </summary>
	public NormalSpace InputSpace { get; set; } = NormalSpace.Tangent;

	/// <summary>
	/// Should we output in world space or tangent space.
	/// </summary>
	public OutputNormalSpace OutputSpace { get; set; } = OutputNormalSpace.Tangent;

	/// <summary>
	/// Scale and shifts input value to ( -1, 1 ) range
	/// </summary>
	public bool DecodeNormal { get; set; } = true;

	[Output]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var result = compiler.Result( Input );

		if ( !result.IsValid )
		{
			// No input, just return the vertex normal in worldspace or a default tangent space output.
			return OutputSpace == OutputNormalSpace.World ? new NodeResult( 3, "i.vNormalWs.xyz" ) : new NodeResult( 3, "float3( 0, 0, 1 )" );
		}

		// Cast the result to a float3
		var resultCast = result.Cast( 3 );

		string inputNormal;

		if ( compiler.IsPreview )
		{
			// Because this is in preview mode, we can afford to use a dynamic branch for the decode normal option
			inputNormal = $"{compiler.ResultValue( DecodeNormal )} ? DecodeNormal( {resultCast} ) : {resultCast}";
		}
		else
		{
			// Decode normal if it's enabled, otherwise just use it as is
			inputNormal = DecodeNormal ? $"DecodeNormal( {resultCast} )" : resultCast;
		}

		if ( InputSpace == NormalSpace.Object )
		{
			inputNormal = compiler.ResultFunction( "Vec3OsToTs", inputNormal,
				"i.vNormalOs.xyz",
				"i.vTangentUOs_flTangentVSign.xyz",
				"cross( i.vNormalOs.xyz, i.vTangentUOs_flTangentVSign.xyz ) * i.vTangentUOs_flTangentVSign.w" );
		}
		else if ( InputSpace == NormalSpace.World )
		{
			inputNormal = $"Vec3WsToTs( {inputNormal}, i.vNormalWs, i.vTangentUWs, i.vTangentVWs )";
		}

		return OutputSpace == OutputNormalSpace.World ? new NodeResult( 3, $"TransformNormal( {inputNormal}, i.vNormalWs, i.vTangentUWs, i.vTangentVWs )" ) : new NodeResult( 3, $"{inputNormal}" );
	};
}

/// <summary>
/// Translate, rotate and scale a <see cref="Vector3"/>.
/// </summary>
[Title( "Apply TRS" ), Category( "Transform" ), Icon( "3d_rotation" )]
public sealed class ApplyTrs : ShaderNode
{
	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput Vector { get; set; }

	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput Translation { get; set; }

	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput Rotation { get; set; }

	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput Scale { get; set; }

	[InputDefault( nameof( Vector ) )]
	public Vector3 DefaultVector { get; set; } = Vector3.Zero;
	[InputDefault( nameof( Translation ) )]
	public Vector3 DefaultTranslation { get; set; } = Vector3.Zero;
	[InputDefault( nameof( Rotation ) )]
	public Rotation DefaultRotation { get; set; } = global::Rotation.Identity;
	[InputDefault( nameof( Scale ) )]
	public Vector3 DefaultScale { get; set; } = Vector3.One;

	[Output]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var vector = compiler.Result( Vector );

		if ( !vector.IsValid() )
		{
			vector = compiler.ResultValue( DefaultVector );
		}

		// Only use DefaultXYZ if a non-default value is specified, so we can skip some matrix multiplications

		var translation = DefaultTranslation == Vector3.Zero ? compiler.Result( Translation ) : compiler.ResultOrDefault( Translation, DefaultTranslation );
		var scale = DefaultScale == Vector3.One ? compiler.Result( Scale ) : compiler.ResultOrDefault( Scale, DefaultScale );

		NodeResult rotation;

		if ( compiler.Result( Rotation ) is { IsValid: true } rotationResult )
		{
			rotation = new NodeResult( 4, compiler.ResultFunction( "Quaternion_FromAngles", rotationResult.Code ) );
		}
		else
		{
			rotation = compiler.ResultValue( new Vector4( DefaultRotation.x, DefaultRotation.y, DefaultRotation.z, DefaultRotation.w ) );
		}

		string matrix = null;

		if ( scale.IsValid ) ApplyMatrix( ref matrix, compiler.ResultFunction( "Matrix_FromScale", scale.Code ) );
		if ( rotation.IsValid ) ApplyMatrix( ref matrix, compiler.ResultFunction( "Matrix_FromQuaternion", rotation.Code ) );
		if ( translation.IsValid ) ApplyMatrix( ref matrix, compiler.ResultFunction( "Matrix_FromTranslation", translation.Code ) );

		return matrix is null ? vector : new NodeResult( 3, $"mul( {matrix}, float4( {vector.Code}, 1.0 ) ).xyz" );
	};

	private static void ApplyMatrix( ref string lhs, string rhs )
	{
		lhs = lhs is null ? rhs : $"mul( {lhs}, {rhs} )";
	}
}

/// <summary>
/// Convert from Cartesian coordinates to polar coordinates.
/// </summary>
[Title( "Polar Coordinates" ), Category( "Transform" ), Icon( "explore" )]
public sealed class PolarCoordinates : ShaderNode
{
	[Input( typeof( Vector2 ) )]
	[Hide]
	public NodeInput Coords { get; set; }

	[Input( typeof( Vector2 ) )]
	[Hide]
	public NodeInput Center { get; set; }

	[Input( typeof( float ) )]
	[Hide]
	public NodeInput RadialScale { get; set; }

	[Input( typeof( float ) )]
	[Hide]
	public NodeInput LengthScale { get; set; }

	[InputDefault( nameof( Center ) )]
	public Vector2 DefaultCenter { get; set; } = 0.5f;
	[InputDefault( nameof( RadialScale ) )]
	public float DefaultRadialScale { get; set; } = 1.0f;
	[InputDefault( nameof( LengthScale ) )]
	public float DefaultLengthScale { get; set; } = 1.0f;

	[Output]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var coords = compiler.Result( Coords );
		var center = compiler.ResultOrDefault( Center, DefaultCenter );
		var radialScale = compiler.ResultOrDefault( RadialScale, DefaultRadialScale );
		var lengthScale = compiler.ResultOrDefault( LengthScale, DefaultLengthScale );

		return new NodeResult( 2, $"PolarCoordinates( ( {(coords.IsValid ? coords : "i.vTextureCoords.xy")} ) - ( {(center.IsValid ? center : "0.0f")} ), {(radialScale.IsValid ? radialScale : "1.0f")}, {(lengthScale.IsValid ? lengthScale : "1.0f")} )" );
	};
}

/// <summary>
/// Tile or shift your texture coordinates. Tile works by scaling the texture up
/// and down. Offset works by adding or subtracting from the texture coordinates
/// </summary>
[Title( "Tile And Offset" ), Category( "Transform" ), Icon( "grid_view" )]
public sealed class TileAndOffset : ShaderNode
{
	[Input( typeof( Vector2 ) )]
	[Hide]
	public NodeInput Coords { get; set; }

	[Input( typeof( Vector2 ) )]
	[Hide]
	public NodeInput Tile { get; set; }

	[Input( typeof( Vector2 ) )]
	[Hide]
	public NodeInput Offset { get; set; }

	[InputDefault( nameof( Tile ) )]
	public Vector2 DefaultTile { get; set; } = 1.0f;
	[InputDefault( nameof( Offset ) )]
	public Vector2 DefaultOffset { get; set; } = 0.0f;

	public bool WrapTo01 { get; set; } = false;

	[Output( typeof( Vector2 ) ), Title( "Coords" )]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var coords = compiler.Result( Coords );
		var tile = compiler.ResultOrDefault( Tile, DefaultTile );
		var offset = compiler.ResultOrDefault( Offset, DefaultOffset );

		var resultCode = $"TileAndOffsetUv( {(coords.IsValid ? coords.Cast( 2 ) : "i.vTextureCoords.xy")}," +
			$" {(tile.IsValid ? tile.Cast( 2 ) : "1.0f")}," +
			$" {(offset.IsValid ? offset.Cast( 2 ) : "0.0f")} )";

		if ( compiler.IsPreview )
		{
			resultCode = $"{compiler.ResultValue( WrapTo01 )} ? frac( {resultCode} ) : {resultCode}";
		}
		else if ( WrapTo01 )
		{
			resultCode = $"frac( {resultCode} )";
		}

		return new NodeResult( 2, resultCode );
	};
}

/// <summary>
/// Blend two colors or textures together using various different blending modes
/// </summary>
[Title( "Blend" ), Category( "Transform" ), Icon( "blender" )]
public sealed class Blend : ShaderNode
{
	[Input( typeof( Color ) )]
	[Hide]
	public NodeInput A { get; set; }

	[Input( typeof( Color ) )]
	[Hide]
	public NodeInput B { get; set; }

	[Input( typeof( float ) ), Title( "Fraction" )]
	[Hide, Editor( nameof( Fraction ) )]
	public NodeInput C { get; set; }

	[InputDefault( nameof( A ) )]
	public Color DefaultA { get; set; } = Color.Black;
	[InputDefault( nameof( B ) )]
	public Color DefaultB { get; set; } = Color.White;
	[InputDefault( nameof( C ) ), MinMax( 0, 1 )]
	public float Fraction { get; set; } = 0.5f;

	public BlendNodeMode BlendMode { get; set; } = BlendNodeMode.Mix;

	/// <summary>
	/// Clamp result between 0 and 1
	/// </summary>
	public bool Clamp { get; set; } = true;

	[Output]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var resultA = compiler.Result( A );
		var resultB = compiler.Result( B );
		var results = compiler.Result( A, B );
		var fraction = compiler.Result( C );
		var fractionType = fraction.IsValid && fraction.Components > 1 ? Math.Max( results.Item1.Components, results.Item2.Components ) : 1;

		string fractionStr = $"{(fraction.IsValid ? fraction.Cast( fractionType ) : compiler.ResultValue( Fraction ))}";
		string aStr = resultA.IsValid ? results.Item1.ToString() : compiler.ResultValue( DefaultA ).ToString();
		string bStr = resultB.IsValid ? results.Item2.ToString() : compiler.ResultValue( DefaultB ).ToString();

		string returnCall = string.Empty;

		switch ( BlendMode )
		{
			case BlendNodeMode.Mix:
				returnCall = $"lerp( {aStr}, {bStr}, {fractionStr} )";
				break;
			case BlendNodeMode.Darken:
				returnCall = $"min( {aStr}, {bStr} )";
				break;
			case BlendNodeMode.Multiply:
				returnCall = $"{aStr}*{bStr}";
				break;
			case BlendNodeMode.Lighten:
				returnCall = $"max( {aStr}, {bStr} )";
				break;
			case BlendNodeMode.Screen:
				returnCall = $"({aStr}) + ({bStr}) - ({aStr}) * ({bStr})";
				break;
			case BlendNodeMode.Difference:
				returnCall = $"abs( ({aStr}) - ({bStr}) )";
				break;
			case BlendNodeMode.Exclusion:
				returnCall = $"({aStr}) + ({bStr}) - 2.0f * ({aStr}) * ({bStr})";
				break;
			case BlendNodeMode.Subtract:
				returnCall = $"max( 0.0f, ({aStr}) - ({bStr}) )";
				break;
			case BlendNodeMode.Add:
				returnCall = $"min( 1.0f, ({aStr}) + ({bStr}) )";
				break;
			case BlendNodeMode.ColorBurn:
				returnCall = compiler.ResultFunction( "ColorBurn_blend", aStr, bStr );
				break;
			case BlendNodeMode.LinearBurn:
				returnCall = compiler.ResultFunction( "LinearBurn_blend", aStr, bStr );
				break;
			case BlendNodeMode.ColorDodge:
				returnCall = compiler.ResultFunction( "ColorDodge_blend", aStr, bStr );
				break;
			case BlendNodeMode.LinearDodge:
				returnCall = compiler.ResultFunction( "LinearDodge_blend", aStr, bStr );
				break;
			case BlendNodeMode.Overlay:
				returnCall = compiler.ResultFunction( "Overlay_blend", aStr, bStr );
				break;
			case BlendNodeMode.SoftLight:
				returnCall = compiler.ResultFunction( "SoftLight_blend", aStr, bStr );
				break;
			case BlendNodeMode.HardLight:
				returnCall = compiler.ResultFunction( "HardLight_blend", aStr, bStr );
				break;
			case BlendNodeMode.VividLight:
				returnCall = compiler.ResultFunction( "VividLight_blend", aStr, bStr );
				break;
			case BlendNodeMode.LinearLight:
				returnCall = compiler.ResultFunction( "LinearLight_blend", aStr, bStr );
				break;
			case BlendNodeMode.HardMix:
				returnCall = compiler.ResultFunction( "HardMix_blend", aStr, bStr );
				break;
			case BlendNodeMode.Divide:
				returnCall = compiler.ResultFunction( "Divide_blend", aStr, bStr );
				break;
		}

		if ( BlendMode != BlendNodeMode.Mix )
			returnCall = $"lerp( {aStr}, {returnCall}, {fractionStr} )";

		if ( Clamp )
			returnCall = $"saturate( {returnCall} )";

		return new NodeResult( results.Item1.Components, returnCall );
	};
}

/// <summary>
/// Blends two normal maps together, normalizing to return an appropriate normal.
/// </summary>
[Title( "Normal Blend" ), Category( "Transform" ), Icon( "gradient" )]
public sealed class NormalBlend : ShaderNode
{
	[Hide]
	public static string NormalBlendVector => @"
float3 NormalBlendVector( float3 a, float3 b)
{
	return normalize( float3( a.xy + b.xy, a.z * b.z ) );
}
";

	[Hide]
	public static string ReorientedNormalBlendVector => @"
float3 ReorientedNormalBlendVector( float3 a, float3 b )
{
	float3 t = a.xyz + float3( 0.0, 0.0, 1.0 );
	float3 u = b.xyz * float3( -1.0, -1.0, 1.0 );
	return ( t / t.z ) * dot( t, u ) - u;
}
";


	public enum BlendMode
	{
		Default,
		Reoriented
	}

	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput A { get; set; }

	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput B { get; set; }

	public BlendMode Mode { get; set; } = BlendMode.Default;

	[Hide]
	[Output( typeof( Vector3 ) )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var a = compiler.Result( A );
		var b = compiler.Result( B );

		string func = compiler.RegisterFunction( NormalBlendVector );
		if ( Mode == BlendMode.Reoriented )
		{
			func = compiler.RegisterFunction( ReorientedNormalBlendVector );
		}



		string funcResult = compiler.ResultFunction( func,
			$"{(a.IsValid ? a.Cast( 3 ) : "1.0")}",
			$"{(b.IsValid ? b.Cast( 3 ) : "1.0")}"
		);

		return new NodeResult( NodeResultType.Vector3, $"{funcResult}" );

	};
}


/// <summary>
/// Blends two normal maps together, normalizing to return an appropriate normal.
/// </summary>
[Title( "Reflection" ), Category( "Transform" ), Icon( "network_ping" )]
public sealed class Reflection : ShaderNode
{
	[Hide]
	public static string ReflectVector => @"
float3 ReflectVector( float3 a, float3 b)
{
	return reflect( a, b );
}
";

	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput A { get; set; }

	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput B { get; set; }


	[InputDefault( nameof( A ) )]
	public Vector3 DefaultA { get; set; } = Vector3.Zero;

	[InputDefault( nameof( B ) )]
	public Vector3 DefaultB { get; set; } = Vector3.One;

	[Hide]
	[Output( typeof( Vector3 ) )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var a = compiler.Result( A );
		var b = compiler.Result( B );

		string func = compiler.RegisterFunction( ReflectVector );
		string funcResult = compiler.ResultFunction( func,
			$"{(a.IsValid ? a.Cast( 3 ) : "1.0")}",
			$"{(b.IsValid ? b.Cast( 3 ) : "1.0")}"
		);

		return new NodeResult( NodeResultType.Vector3, $"{funcResult}" );
	};
}


[Title( "RGB to HSV" ), Category( "Transform" ), Icon( "invert_colors" )]
public sealed class RGBtoHSV : ShaderNode
{
	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput In { get; set; }

	[Output( typeof( Vector3 ) )]
	[Hide]
	public NodeResult.Func Out => ( GraphCompiler compiler ) =>
	{
		return new NodeResult( 3, compiler.ResultFunction( "RGB2HSV", $"{compiler.ResultOrDefault( In, Vector3.One )}" ) );
	};
}

[Title( "HSV to RGB" ), Category( "Transform" ), Icon( "invert_colors" )]
public sealed class HSVtoRGB : ShaderNode
{
	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput In { get; set; }

	[Output( typeof( Vector3 ) )]
	[Hide]
	public NodeResult.Func Out => ( GraphCompiler compiler ) =>
	{
		return new NodeResult( 3, compiler.ResultFunction( "HSV2RGB", $"{compiler.ResultOrDefault( In, Vector3.One )}" ) );
	};
}

/// <summary>
/// Converts a color from sRGB gamma space to linear space.
/// </summary>
[Title( "Srgb Gamma To Linear" ), Category( "Transform" ), Icon( "invert_colors" )]
public sealed class SrgbGammaToLinear : ShaderNode
{
	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput In { get; set; }

	[Output( typeof( Vector3 ) )]
	[Hide]
	public NodeResult.Func Out => ( GraphCompiler compiler ) =>
	{
		return new NodeResult( 3, $"SrgbGammaToLinear( {compiler.ResultOrDefault( In, Vector3.One )} )" );
	};
}

/// <summary>
/// Converts a color from linear space to sRGB gamma space.
/// </summary>
[Title( "Srgb Linear To Gamma" ), Category( "Transform" ), Icon( "invert_colors" )]
public sealed class SrgbLinearToGamma : ShaderNode
{
	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput In { get; set; }

	[Output( typeof( Vector3 ) )]
	[Hide]
	public NodeResult.Func Out => ( GraphCompiler compiler ) =>
	{
		return new NodeResult( 3, $"SrgbLinearToGamma( {compiler.ResultOrDefault( In, Vector3.One )} )" );
	};
}
