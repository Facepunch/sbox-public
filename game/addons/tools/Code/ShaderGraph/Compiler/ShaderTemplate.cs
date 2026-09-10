using System.Reflection;

namespace Editor.ShaderGraph;

public static class ShaderTemplate
{
	public static string Code => @"
HEADER
{{
	Description = ""{0}"";
}}

FEATURES
{{
	#include ""common/features.hlsl""
}}

MODES
{{
	Forward();
	Depth();
	ToolsShadingComplexity( ""tools_shading_complexity.shader"" );
}}

COMMON
{{
{1}
	#include ""common/shared.hlsl""
	#include ""procedural.hlsl""

	#define S_UV2 1
}}

struct VertexInput
{{
	#include ""common/vertexinput.hlsl""
	float4 vColor : COLOR0 < Semantic( Color ); >;
}};

struct PixelInput
{{
	#include ""common/pixelinput.hlsl""
	float3 vPositionOs : TEXCOORD14;
	float3 vNormalOs : TEXCOORD15;
	float4 vTangentUOs_flTangentVSign : TANGENT	< Semantic( TangentU_SignV ); >;
	float4 vColor : COLOR0;
	float4 vTintColor : COLOR1;
	#if ( PROGRAM == VFX_PROGRAM_PS )
		bool vFrontFacing : SV_IsFrontFace;
	#endif
}};

VS
{{
	#include ""common/vertex.hlsl""
{7}{6}{10}
	PixelInput MainVs( VertexInput v )
	{{
{5}
	}}
}}

PS
{{
	#include ""common/pixel.hlsl""
{8}{2}{9}
	float4 MainPs( PixelInput i ) : SV_Target0
	{{
{11}
{3}
{4}
{12}
	}}
}}
";

	public static string Material_init => @"
Material m = Material::Init( i );
m.Albedo = float3( 1, 1, 1 );
m.Normal = float3( 0, 0, 1 );
m.Roughness = 1;
m.Metalness = 0;
m.AmbientOcclusion = 1;
m.TintMask = 1;
m.Opacity = 1;
m.Emission = float3( 0, 0, 0 );
m.Transmission = 0;";

	public static string Material_output => @"
m.AmbientOcclusion = saturate( m.AmbientOcclusion );
m.Roughness = saturate( m.Roughness );
m.Metalness = saturate( m.Metalness );
m.Opacity = saturate( m.Opacity );

// Result node takes normal as tangent space, convert it to world space now
m.Normal = TransformNormal( m.Normal, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );

// for some toolvis shit
m.WorldTangentU = i.vTangentUWs;
m.WorldTangentV = i.vTangentVWs;
m.TextureCoords = i.vTextureCoords.xy;
		
return ShadingModelStandard::Shade( m );";

	[Function( "ColorBurn_blend" )]
	public static string ColorBurn_blend => @"
float ColorBurn_blend( float a, float b )
{
    if ( a >= 1.0f ) return 1.0f;
    if ( b <= 0.0f ) return 0.0f;
    return 1.0f - saturate( ( 1.0f - a ) / b );
}

float3 ColorBurn_blend( float3 a, float3 b )
{
    return float3(
        ColorBurn_blend( a.r, b.r ),
        ColorBurn_blend( a.g, b.g ),
        ColorBurn_blend( a.b, b.b )
	);
}

float4 ColorBurn_blend( float4 a, float4 b, bool blendAlpha = false )
{
    return float4(
        ColorBurn_blend( a.rgb, b.rgb ).rgb,
        blendAlpha ? ColorBurn_blend( a.a, b.a ) : max( a.a, b.a )
    );
}
";

	[Function( "LinearBurn_blend" )]
	public static string LinearBurn_blend => @"
float LinearBurn_blend( float a, float b )
{
    return max( 0.0f, a + b - 1.0f );
}

float3 LinearBurn_blend( float3 a, float3 b )
{
    return float3(
        LinearBurn_blend( a.r, b.r ),
        LinearBurn_blend( a.g, b.g ),
        LinearBurn_blend( a.b, b.b )
	);
}

float4 LinearBurn_blend( float4 a, float4 b, bool blendAlpha = false )
{
    return float4(
        LinearBurn_blend( a.rgb, b.rgb ).rgb,
        blendAlpha ? LinearBurn_blend( a.a, b.a ) : max( a.a, b.a )
    );
}
";

	[Function( "ColorDodge_blend" )]
	public static string ColorDodge_blend => @"
float ColorDodge_blend( float a, float b )
{
    if ( a <= 0.0f ) return 0.0f;
    if ( b >= 1.0f ) return 1.0f;
    return saturate( a / ( 1.0f - b ) );
}

float3 ColorDodge_blend( float3 a, float3 b )
{
    return float3(
        ColorDodge_blend( a.r, b.r ),
        ColorDodge_blend( a.g, b.g ),
        ColorDodge_blend( a.b, b.b )
	);
}

float4 ColorDodge_blend( float4 a, float4 b, bool blendAlpha = false )
{
    return float4(
        ColorDodge_blend( a.rgb, b.rgb ).rgb,
        blendAlpha ? ColorDodge_blend( a.a, b.a ) : max( a.a, b.a )
    );
}
";

	[Function( "LinearDodge_blend" )]
	public static string LinearDodge_blend => @"
float LinearDodge_blend( float a, float b )
{
    return min( 1.0f, a + b );
}

float3 LinearDodge_blend( float3 a, float3 b )
{
    return float3(
        LinearDodge_blend( a.r, b.r ),
        LinearDodge_blend( a.g, b.g ),
        LinearDodge_blend( a.b, b.b )
	);
}

float4 LinearDodge_blend( float4 a, float4 b, bool blendAlpha = false )
{
    return float4(
        LinearDodge_blend( a.rgb, b.rgb ).rgb,
        blendAlpha ? LinearDodge_blend( a.a, b.a ) : max( a.a, b.a )
    );
}
";

	[Function( "Overlay_blend" )]
	public static string Overlay_blend => @"
float Overlay_blend( float a, float b )
{
    if ( a <= 0.5f )
        return 2.0f * a * b;
    else
        return 1.0f - 2.0f * ( 1.0f - a ) * ( 1.0f - b );
}

float3 Overlay_blend( float3 a, float3 b )
{
    return float3(
        Overlay_blend( a.r, b.r ),
        Overlay_blend( a.g, b.g ),
        Overlay_blend( a.b, b.b )
	);
}

float4 Overlay_blend( float4 a, float4 b, bool blendAlpha = false )
{
    return float4(
        Overlay_blend( a.rgb, b.rgb ).rgb,
        blendAlpha ? Overlay_blend( a.a, b.a ) : max( a.a, b.a )
    );
}
";

	[Function( "SoftLight_blend" )]
	public static string SoftLight_blend => @"
float SoftLight_blend( float a, float b )
{
    if ( b <= 0.5f )
        return 2.0f * a * b + a * a * ( 1.0f * 2.0f * b );
    else 
        return sqrt( a ) * ( 2.0f * b - 1.0f ) + 2.0f * a * (1.0f - b);
}

float3 SoftLight_blend( float3 a, float3 b )
{
    return float3(
        SoftLight_blend( a.r, b.r ),
        SoftLight_blend( a.g, b.g ),
        SoftLight_blend( a.b, b.b )
	);
}

float4 SoftLight_blend( float4 a, float4 b, bool blendAlpha = false )
{
    return float4(
        SoftLight_blend( a.rgb, b.rgb ).rgb,
        blendAlpha ? SoftLight_blend( a.a, b.a ) : max( a.a, b.a )
    );
}
";

	[Function( "HardLight_blend" )]
	public static string HardLight_blend => @"
float HardLight_blend( float a, float b )
{
    if(b <= 0.5f)
        return 2.0f * a * b;
    else
        return 1.0f - 2.0f * (1.0f - a) * (1.0f - b);
}

float3 HardLight_blend( float3 a, float3 b )
{
    return float3(
        HardLight_blend( a.r, b.r ),
        HardLight_blend( a.g, b.g ),
        HardLight_blend( a.b, b.b )
	);
}

float4 HardLight_blend( float4 a, float4 b, bool blendAlpha = false )
{
    return float4(
        HardLight_blend( a.rgb, b.rgb ).rgb,
        blendAlpha ? HardLight_blend( a.a, b.a ) : max( a.a, b.a )
    );
}
";

	[Function( "VividLight_blend" )]
	public static string VividLight_blend => @"
float VividLight_blend( float a, float b )
{
    if ( b <= 0.5f )
	{
		b *= 2.0f;
		if ( a >= 1.0f ) return 1.0f;
		if ( b <= 0.0f ) return 0.0f;
		return 1.0f - saturate( ( 1.0f - a ) / b );
	}
    else
	{
		b = 2.0f * ( b - 0.5f );
		if ( a <= 0.0f ) return 0.0f;
		if ( b >= 1.0f ) return 1.0f;
		return saturate( a / ( 1.0f - b ) );
	}
}

float3 VividLight_blend( float3 a, float3 b )
{
    return float3(
        VividLight_blend( a.r, b.r ),
        VividLight_blend( a.g, b.g ),
        VividLight_blend( a.b, b.b )
	);
}

float4 VividLight_blend( float4 a, float4 b, bool blendAlpha = false )
{
    return float4(
        VividLight_blend( a.rgb, b.rgb ).rgb,
        blendAlpha ? VividLight_blend( a.a, b.a ) : max( a.a, b.a )
    );
}
";

	[Function( "LinearLight_blend" )]
	public static string LinearLight_blend => @"
float LinearLight_blend( float a, float b )
{
    if ( b <= 0.5f )
	{
		b *= 2.0f;
		return max( 0.0f, a + b - 1.0f );
	}
    else
	{
		b = 2.0f * ( b - 0.5f );
		return min( 1.0f, a + b );
	}
}

float3 LinearLight_blend( float3 a, float3 b )
{
    return float3(
        LinearLight_blend( a.r, b.r ),
        LinearLight_blend( a.g, b.g ),
        LinearLight_blend( a.b, b.b )
	);
}

float4 LinearLight_blend( float4 a, float4 b, bool blendAlpha = false )
{
    return float4(
        LinearLight_blend( a.rgb, b.rgb ).rgb,
        blendAlpha ? LinearLight_blend( a.a, b.a ) : max( a.a, b.a )
    );
}
";

	[Function( "HardMix_blend" )]
	public static string HardMix_blend => @"
float HardMix_blend( float a, float b )
{
    if(a + b >= 1.0f) return 1.0f;
    else return 0.0f;
}

float3 HardMix_blend( float3 a, float3 b )
{
    return float3(
        HardMix_blend( a.r, b.r ),
        HardMix_blend( a.g, b.g ),
        HardMix_blend( a.b, b.b )
	);
}

float4 HardMix_blend( float4 a, float4 b, bool blendAlpha = false )
{
    return float4(
        HardMix_blend( a.rgb, b.rgb ).rgb,
        blendAlpha ? HardMix_blend( a.a, b.a ) : max( a.a, b.a )
    );
}
";

	[Function( "Divide_blend" )]
	public static string Divide_blend => @"
float Divide_blend( float a, float b )
{
    if( b > 0.0f )
        return saturate( a / b );
    else
        return 0.0f;
}

float3 Divide_blend( float3 a, float3 b )
{
    return float3(
        Divide_blend( a.r, b.r ),
        Divide_blend( a.g, b.g ),
        Divide_blend( a.b, b.b )
	);
}

float4 Divide_blend( float4 a, float4 b, bool blendAlpha = false )
{
    return float4(
        Divide_blend( a.rgb, b.rgb ).rgb,
        blendAlpha ? Divide_blend( a.a, b.a ) : max( a.a, b.a )
    );
}
";

	[Function( "RGB2HSV" )]
	public static string RGB2HSV => @"
float3 RGB2HSV( float3 c )
{
    float4 K = float4( 0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0 );
    float4 p = lerp( float4( c.bg, K.wz ), float4( c.gb, K.xy ), step( c.b, c.g ) );
    float4 q = lerp( float4( p.xyw, c.r ), float4( c.r, p.yzx ), step( p.x, c.r ) );

    float d = q.x - min( q.w, q.y );
    float e = 1.0e-10;
    return float3( abs( q.z + ( q.w - q.y ) / ( 6.0 * d + e ) ), d / ( q.x + e ), q.x );
}
";

	[Function( "HSV2RGB" )]
	public static string HSV2RGB => @"
float3 HSV2RGB( float3 c )
{
    float4 K = float4( 1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0 );
    float3 p = abs( frac( c.xxx + K.xyz ) * 6.0 - K.www );
    return c.z * lerp( K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y );
}
";

	[Function( "TexTriplanar_Color" )]
	public static string TexTriplanar_Color => @"
float4 TexTriplanar_Color( in Texture2D tTex, in SamplerState sSampler, float3 vPosition, float3 vNormal )
{
	float2 uvX = vPosition.yz;
	float2 uvY = vPosition.xz;
	float2 uvZ = vPosition.xy;

	float3 triblend = saturate(pow(abs(vNormal), 4));
	triblend /= max(dot(triblend, half3(1,1,1)), 0.0001);

	half3 axisSign = vNormal < 0 ? -1 : 1;

	uvX.x *= axisSign.x;
	uvY.x *= axisSign.y;
	uvZ.x *= -axisSign.z;

	float4 colX = Tex2DS( tTex, sSampler, uvX );
	float4 colY = Tex2DS( tTex, sSampler, uvY );
	float4 colZ = Tex2DS( tTex, sSampler, uvZ );

	return colX * triblend.x + colY * triblend.y + colZ * triblend.z;
}
";

	[Function( "TexTriplanar_Normal" )]
	public static string TexTriplanar_Normal => @"
float3 TexTriplanar_Normal( in Texture2D tTex, in SamplerState sSampler, float3 vPosition, float3 vNormal )
{
	float2 uvX = vPosition.yz;
	float2 uvY = vPosition.xz;
	float2 uvZ = vPosition.xy;

	float3 triblend = saturate( pow( abs( vNormal ), 4 ) );
	triblend /= max( dot( triblend, half3( 1, 1, 1 ) ), 0.0001 );

	half3 axisSign = vNormal < 0 ? -1 : 1;

	uvX.x *= axisSign.x;
	uvY.x *= axisSign.y;
	uvZ.x *= -axisSign.z;

	float3 tnormalX = DecodeNormal( Tex2DS( tTex, sSampler, uvX ).xyz );
	float3 tnormalY = DecodeNormal( Tex2DS( tTex, sSampler, uvY ).xyz );
	float3 tnormalZ = DecodeNormal( Tex2DS( tTex, sSampler, uvZ ).xyz );

	tnormalX.x *= axisSign.x;
	tnormalY.x *= axisSign.y;
	tnormalZ.x *= -axisSign.z;

	tnormalX = half3( tnormalX.xy + vNormal.yz, vNormal.x );
	tnormalY = half3( tnormalY.xy + vNormal.xz, vNormal.y );
	tnormalZ = half3( tnormalZ.xy + vNormal.xy, vNormal.z );

	return normalize(
		tnormalX.zxy * triblend.x +
		tnormalY.xzy * triblend.y +
		tnormalZ.xyz * triblend.z +
		vNormal
	);
}
";
	[Function( "Quaternion_FromAngles" )]
	public static string Quaternion_FromAngles => @"
float4 Quaternion_FromAngles( float3 vAngles )
{
	float4 rot = { 0.0, 0.0, 0.0, 1.0 };

	const float ANGLE_CONVERSION = 3.14159265 / 360.0;

	float pitch = vAngles.x * ANGLE_CONVERSION;
	float yaw = vAngles.y * ANGLE_CONVERSION;
	float roll = vAngles.z * ANGLE_CONVERSION;

	float sp = sin( pitch );
	float cp = cos( pitch );

	float sy = sin( yaw );
	float cy = cos( yaw );

	float sr = sin( roll );
	float cr = cos( roll );

	float srXcp = sr * cp;
	float crXsp = cr * sp;

	rot.x = srXcp * cy - crXsp * sy; // X
	rot.y = crXsp * cy + srXcp * sy; // Y

	float crXcp = cr * cp;
	float srXsp = sr * sp;

	rot.z = crXcp * sy - srXsp * cy; // Z
	rot.w = crXcp * cy + srXsp * sy; // W (real component)

	return rot;
}
";

	[Function( "Matrix_Identity" )]
	public static string Matrix_Identity => @"
float4x4 Matrix_Identity()
{
	return
	{
		1.0, 0.0, 0.0, 0.0,
		0.0, 1.0, 0.0, 0.0,
		0.0, 0.0, 1.0, 0.0,
		0.0, 0.0, 0.0, 1.0
	};
}
";

	[Function( "Matrix_FromQuaternion" )]
	public static string Matrix_FromQuaternion => @"
float4x4 Matrix_FromQuaternion( float4 qRotation )
{
	float xx = qRotation.x * qRotation.x;
	float yy = qRotation.y * qRotation.y;
	float zz = qRotation.z * qRotation.z;

	float xy = qRotation.x * qRotation.y;
	float wz = qRotation.z * qRotation.w;
	float xz = qRotation.z * qRotation.x;
	float wy = qRotation.y * qRotation.w;
	float yz = qRotation.y * qRotation.z;
	float wx = qRotation.x * qRotation.w;

	float4x4 result =
	{
		1.0, 0.0, 0.0, 0.0,
		0.0, 1.0, 0.0, 0.0,
		0.0, 0.0, 1.0, 0.0,
		0.0, 0.0, 0.0, 1.0
	};

	result._11 = 1.0 - 2.0 * (yy + zz);
	result._21 = 2.0 * (xy + wz);
	result._31 = 2.0 * (xz - wy);

	result._12 = 2.0 * (xy - wz);
	result._22 = 1.0 - 2.0 * (zz + xx);
	result._32 = 2.0 * (yz + wx);

	result._13 = 2.0 * (xz + wy);
	result._23 = 2.0 * (yz - wx);
	result._33 = 1.0 - 2.0 * (yy + xx);

	return result;
}
";

	[Function( "Matrix_FromScale" )]
	public static string Matrix_FromScale => @"
float4x4 Matrix_FromScale( float3 vScale )
{
	float4x4 result =
	{
		1.0, 0.0, 0.0, 0.0,
		0.0, 1.0, 0.0, 0.0,
		0.0, 0.0, 1.0, 0.0,
		0.0, 0.0, 0.0, 1.0
	};

	result._11 = vScale.x;
	result._22 = vScale.y;
	result._33 = vScale.z;

	return result;
}
";

	[Function( "Matrix_FromTranslation" )]
	public static string Matrix_FromTranslation => @"
float4x4 Matrix_FromTranslation( float3 vTranslation )
{
	float4x4 result =
	{
		1.0, 0.0, 0.0, 0.0,
		0.0, 1.0, 0.0, 0.0,
		0.0, 0.0, 1.0, 0.0,
		0.0, 0.0, 0.0, 1.0
	};

	result._14 = vTranslation.x;
	result._24 = vTranslation.y;
	result._34 = vTranslation.z;

	return result;
}
";

	[Function( "Vec3OsToTs" )]
	public static string Vec3OsToTs => @"
float3 Vec3OsToTs( float3 vVectorOs, float3 vNormalOs, float3 vTangentUOs, float3 vTangentVOs )
{
	float3 vVectorTs;
	vVectorTs.x = dot( vVectorOs.xyz, vTangentUOs.xyz );
	vVectorTs.y = dot( vVectorOs.xyz, vTangentVOs.xyz );
	vVectorTs.z = dot( vVectorOs.xyz, vNormalOs.xyz );
	return vVectorTs.xyz;
}
";

	public static string TextureDefinition => @"<!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->
""CDmeVtex""
{{
    ""m_inputTextureArray"" ""element_array"" 
    [
        ""CDmeInputTexture""
        {{
            ""m_name"" ""string"" ""0""
            ""m_fileName"" ""string"" ""{0}""
            ""m_colorSpace"" ""string"" ""{1}""
            ""m_typeString"" ""string"" ""2D""
            ""m_imageProcessorArray"" ""element_array"" 
            [
                ""CDmeImageProcessor""
                {{
                    ""m_algorithm"" ""string"" ""{3}""
                    ""m_stringArg"" ""string"" """"
                    ""m_vFloat4Arg"" ""vector4"" ""0 0 0 0""
                }}
            ]
        }}
    ]
    ""m_outputTypeString"" ""string"" ""2D""
    ""m_outputFormat"" ""string"" ""{2}""
    ""m_textureOutputChannelArray"" ""element_array""
    [
        ""CDmeTextureOutputChannel""
        {{
            ""m_inputTextureArray"" ""string_array""
            [
                ""0""
            ]
            ""m_srcChannels"" ""string"" ""rgba""
            ""m_dstChannels"" ""string"" ""rgba""
            ""m_mipAlgorithm"" ""CDmeImageProcessor""
            {{
                ""m_algorithm"" ""string"" ""Box""
                ""m_stringArg"" ""string"" """"
                ""m_vFloat4Arg"" ""vector4"" ""0 0 0 0""
            }}
            ""m_outputColorSpace"" ""string"" ""{1}""
        }}
    ]
}}";

	[AttributeUsage( AttributeTargets.Property )]
	private class FunctionAttribute : Attribute
	{
		public string Name { get; set; }

		public FunctionAttribute( string name )
		{
			Name = name;
		}
	}

	private static Dictionary<string, string> Functions;

	public static bool TryGetFunction( string name, out string func )
	{
		return Functions.TryGetValue( name, out func );
	}

	public static bool HasFunction( string name )
	{
		return Functions.ContainsKey( name );
	}

	internal static bool RegisterFunction( string name, string code )
	{
		if ( Functions.ContainsKey( name ) )
			return false;
		Functions[name] = code;
		return true;
	}

	static ShaderTemplate()
	{
		CreateFunctions();
	}

	[EditorEvent.Hotload]
	private static void CreateFunctions()
	{
		Functions = new Dictionary<string, string>();
		var properties = typeof( ShaderTemplate ).GetProperties( BindingFlags.Public | BindingFlags.Static );

		foreach ( var property in properties )
		{
			if ( property.PropertyType == typeof( string ) )
			{
				var attr = (FunctionAttribute)Attribute.GetCustomAttribute( property, typeof( FunctionAttribute ) );
				if ( attr != null )
				{
					Functions[attr.Name] = (string)property.GetValue( null );
				}
			}
		}
	}
}
