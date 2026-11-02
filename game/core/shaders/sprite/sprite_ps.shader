HEADER
{
	DevShader = true;
	Description = "Sprite2D rendering";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}

COMMON
{
	#if ( D_OPAQUE == 0 )
		#define S_TRANSLUCENT 1
	#endif

	#include "common/shared.hlsl"

	enum SpriteFlags
	{
		None = 0x0,
		CastShadows = 0x1,
		FlipW = 0x2,
		FlipH = 0x4,
		SnapToFrame = 0x8,
		Text = 0x10		// glyph instances instead of a texture, see GpuFontText
	};

	struct SpriteData
	{
		float3 Position;
		float3 Rotation;
		float2 Scale;
		uint TintColor;
		uint OverlayColor;
		int TextureHandle;
		int RenderFlags;
		uint BillboardMode;
		uint FogStrengthCutout; // Lower 16 bits: fog, upper 16 bits: alpha cutout
		uint Lighting;
		float DepthFeather;
		int SamplerIndex;
		int Splots;
		int Sequence;
		float SequenceTime;
		float RotationOffset;
		float4 MotionBlur;
		float3 Velocity;
		float4 BlendSheetUV;
		float2 Offset;
		uint CameraFade;
		int NormalTextureHandle;
		float RefractionStrength;
	};

	StructuredBuffer<SpriteData> Sprites < Attribute( "Sprites" ); >;
	StructuredBuffer<uint> SortLUT < Attribute( "SortLUT" ); >;
	int IsSorted < Attribute( "IsSorted" ); >;
	int CurrentBufferSize < Attribute( "SpriteCount" ); >;
	float4 g_vFrameBufferCopyRectangle < Attribute( "FrameBufferCopyRectangle" ); Default4( 0.0, 0.0, 1.0, 1.0 ); >;

	SpriteData GetSprite( uint index )
	{
		int spriteIndex = index;
		if ( IsSorted == 1 )
		{
			spriteIndex = SortLUT[CurrentBufferSize - 1 - index];
		}
		return Sprites[spriteIndex];
	}
}

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	uint instanceID : TEXCOORD8;
	float4 uv : TEXCOORD9;
	float uvBlend : TEXCOORD10;
	#if ( D_REFRACTION )
		float4 refractionBasis : TEXCOORD12;
	#endif
};

struct VS_INPUT
{
	float3 pos : POSITION < Semantic( None ); >;
};

VS
{
	DynamicCombo( D_REFRACTION, 0..1, Sys( ALL ) );

	struct SpriteVertex
	{
		float4 position;
		float4 normal;
		float2 uv;
	};
	StructuredBuffer<SpriteVertex> Vertices < Attribute( "Vertices" ); >;

	float4x4 WorldToView < Attribute( "WorldToView" ); >;

	float3 GetScale( float4x4 mat )
	{
		float3 scale;
		scale.x = length( float3( mat._11, mat._12, mat._13 ) );
		scale.y = length( float3( mat._21, mat._22, mat._23 ) );
		scale.z = length( float3( mat._31, mat._32, mat._33 ) );
		return scale;
	}

	float3 GetSpriteOffset( float2 offset, float3 right, float3 up, float3 scale )
	{
		float3 offsetPos = float3( 0, 0, 0 );
		float pivotScale = 20.0f;
		offsetPos += ( offset.x - 0.5f ) * right * scale.x * pivotScale;
		offsetPos += ( offset.y - 0.5f ) * up * scale.y * pivotScale;
		return offsetPos;
	}

	float4x4 BuildBillboardMatrix(float3 position, float3 rotation, float3 scale, float2 offset, bool yLocked)
	{
		float3 cameraForward = g_vCameraDirWs;
		float3 cameraUp, cameraRight;
		
		if (yLocked)
		{
			// Project to XY plane and use world up
			float3 worldUp = float3(0, 0, 1);
			cameraForward.z = 0;
			float forwardLength = length(cameraForward);
			
			if (forwardLength < 0.001)
			{
				cameraForward = float3(1, 0, 0);
			}
			else
			{
				cameraForward = cameraForward / forwardLength;
			}
			
			cameraRight = normalize(cross(worldUp, -cameraForward));
			cameraUp = worldUp;
		}
		else
		{
			cameraUp = -g_vCameraUpDirWs;
			cameraRight = normalize(cross(cameraUp, cameraForward));
			cameraUp = normalize(cross(cameraForward, cameraRight));
		}
		
		// Create base billboard orientation
		float3 billboardRight = cameraRight;
		float3 billboardUp = cameraUp;
		float3 billboardForward = -cameraForward;
		
		// Apply Z/Roll rotation
		if (rotation.z != 0)
		{
			float3 rotAxis = cameraForward;
			float3x3 zRotation = MatrixBuildRotationAboutAxis(rotAxis, rotation.z);
			billboardRight = mul(zRotation, billboardRight);
			billboardUp = mul(zRotation, billboardUp);
			if (yLocked) billboardForward = mul(zRotation, billboardForward);
		}
		
		// Apply offset in billboard space
		float3 offsetPos = position + GetSpriteOffset(offset, -billboardRight, -billboardUp, scale);
		
		// Build transformation matrix
		float4x4 transform;
		transform[0] = float4(billboardRight * scale.x, 0);
		transform[1] = float4(billboardUp * scale.y, 0);
		transform[2] = float4(billboardForward * scale.z, 0);
		transform[3] = float4(offsetPos, 1);
		
		return transform;
	}

	PixelInput MainVs( uint vertexIndex : SV_VertexID, uint instanceID : SV_InstanceID )
	{
		SpriteVertex v = Vertices[vertexIndex];

		SpriteData sprite = GetSprite(instanceID);
		
		float3 position = sprite.Position;
		float3 scale = float3(sprite.Scale.x, sprite.Scale.y, 1.0) / 10;
		
		float4x4 transform;
		uint billboardMode = sprite.BillboardMode;
		
		if (billboardMode == 0) // Always billboard
		{
			transform = BuildBillboardMatrix(position, sprite.Rotation, scale, sprite.Offset, false);
		}
		else if (billboardMode == 1) // Y-Only billboard
		{
			scale.y *= -1;
			transform = BuildBillboardMatrix(position, sprite.Rotation, scale, sprite.Offset, true);
		}
		else // No billboard - use full rotation
		{
			float3x3 rotationMatrix = RotationMatrixFromAngles( sprite.Rotation.zxy );
			
			// Apply offset in local space after rotation
			float3 localRight = rotationMatrix[1];
			float3 localUp = rotationMatrix[2]; 
			float3 offsetPos = position + GetSpriteOffset(sprite.Offset, localRight, localUp, scale);
			
			transform[0] = float4(rotationMatrix[1] * scale.x, 0);
			transform[1] = float4(rotationMatrix[2] * -scale.y, 0);
			transform[2] = float4(rotationMatrix[0] * scale.z, 0);
			transform[3] = float4(offsetPos, 1); 
		}

		float3x3 normalMatrix = (float3x3)transform;
		float3 worldNormal = mul(transpose(normalMatrix), float3(1, 0, 0));

		float blendAmount = 0.0f;
		float4 blendedUV = float4(0.0f, 0.0f, 0.0f, 0.0f);

		bool snapToFrame = (sprite.RenderFlags & SnapToFrame) != 0;

		if (snapToFrame)
		{
			blendedUV.xy = Sheet::Single(sprite.BlendSheetUV, sprite.Sequence, sprite.SequenceTime, v.uv);
			blendedUV.zw = blendedUV.xy;
		}
		else
		{
			Sheet::Blended(sprite.BlendSheetUV, sprite.Sequence, sprite.SequenceTime, v.uv, blendedUV.xy, blendedUV.zw, blendAmount);
		}

		PixelInput o;
		float3 vPositionWs = mul(transpose(transform), float4(v.position.xyz, 1)).xyz;
		o.vPositionWs = vPositionWs - g_vHighPrecisionLightingOffsetWs.xyz;
		o.vPositionPs = Position3WsToPs( vPositionWs );
		o.instanceID = instanceID;
		o.vNormalWs = worldNormal;
		o.uv = blendedUV;
		o.uvBlend = blendAmount;

		#if ( D_REFRACTION )
		{
			// Project the sprite's UV axes into screen space so distortion follows billboarding,
			// particle roll, object alignment and velocity-facing rotation from the compute pass.
			float3 tangentU = mul( g_matWorldToView, float4( NormalizeSafe( transform[0].xyz ), 0 ) ).xyz;
			float3 tangentV = mul( g_matWorldToView, float4( NormalizeSafe( transform[1].xyz ), 0 ) ).xyz;
			o.refractionBasis = float4( tangentU.xy * float2( 1, -1 ), tangentV.xy * float2( 1, -1 ) );
		}
		#endif
		return o;
	}
}

PS
{
	#define CUSTOM_MATERIAL_INPUTS 1
	#include "common/pixel.hlsl"
	#if ( D_TEXT )
		#include "ui/text.hlsl"
	#endif

	#if ( D_BLEND == 1 ) 
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, SRC_ALPHA );
		RenderState( DstBlend, ONE );
		RenderState( DepthWriteEnable, false );
	#else
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, SRC_ALPHA );
		RenderState( DstBlend, INV_SRC_ALPHA );
		RenderState( BlendOp, ADD );
		RenderState( SrcBlendAlpha, ONE );
		RenderState( DstBlendAlpha, INV_SRC_ALPHA );
		RenderState( DepthWriteEnable, D_OPAQUE || S_MODE_DEPTH == 1 );
		RenderState( AlphaToCoverageEnable, S_MODE_DEPTH == 1 );
	#endif

	RenderState( CullMode, NONE );

	DynamicCombo( D_BLEND, 0..1, Sys( ALL ) );
	DynamicCombo( D_OPAQUE, 0..1, Sys( ALL ) );
	DynamicCombo( D_TEXT, 0..1, Sys( ALL ) );
	DynamicCombo( D_REFRACTION, 0..1, Sys( ALL ) );

	DynamicCombo( D_REFRACTION_TEST, 0..1, Sys( ALL ) );

	Texture2D g_tFrameBufferCopyTexture < Attribute( "FrameBufferCopyTexture" ); SrgbRead( false ); >;

	// Lit sprites' light, per froxel of the light clusters (ParticleLightingSystem)
	Texture3D g_tParticleLighting < Attribute( "ParticleLighting" ); SrgbRead( false ); >;


	// Performs UV Flipping
	float2 GetUV(SpriteData sprite, float2 inUV)
	{
		float2 uv = inUV;

		if((sprite.RenderFlags & FlipW) != 0)
		{
			uv.x = 1.0 - uv.x;
		}

		if((sprite.RenderFlags & FlipH) != 0)
		{
			uv.y = 1.0 - uv.y;
		}

		return uv;
	}

	#if ( D_REFRACTION )
	float2 ReadRefractionNormal( SpriteData sprite, PixelInput i, float2 uv )
	{
		Texture2D normalTexture = Bindless::GetTexture2D( sprite.NormalTextureHandle );
		SamplerState normalSampler = Bindless::GetSampler( sprite.SamplerIndex );
		float2 normal = normalTexture.Sample( normalSampler, uv ).rg * 2.0 - 1.0;
		if ( i.uvBlend > 0.0 )
		{
			float2 nextNormal = normalTexture.Sample( normalSampler, i.uv.zw ).rg * 2.0 - 1.0;
			normal = lerp( normal, nextNormal, i.uvBlend );
		}

		// UNORM8 cannot represent 0.5 exactly: both 127 and 128 encode a flat normal.
		// Remove only that neutral quantization error, including on interpolated sheet frames.
		if ( all( abs( normal ) <= (1.0 / 255.0 + 0.000001) ) ) normal = 0.0;

		if ( (sprite.RenderFlags & FlipW) != 0 ) normal.x = -normal.x;
		if ( (sprite.RenderFlags & FlipH) != 0 ) normal.y = -normal.y;
		return normal;
	}
	#endif

	// Helper function to unpack RGBA8 uint to float4
	float4 UnpackTintColor(uint packedColor)
	{
		float4 color;
		color.r = ((packedColor      ) & 0xFF) / 255.0f;
		color.g = ((packedColor >>  8) & 0xFF) / 255.0f;
		color.b = ((packedColor >> 16) & 0xFF) / 255.0f;
		color.a = ((packedColor >> 24) & 0xFF) / 255.0f;
		return color;
	}

	void UnpackLighting( uint lightingPacked, out uint lightingFlag, out uint exponent )
	{
		// Extract 8-bit lighting flag (lower 8 bits)
		lightingFlag = lightingPacked & 0xFF;

		// Extract 8-bit exponent from bits 16-23
		exponent = (lightingPacked >> 16) & 0xFF;
	}

	void UnpackFogAndAlpha( uint packed, out float fogStrength, out float alphaCutoff )
	{
		// Extract lower 16 bits for fog strength
		uint fogPacked = packed & 0xFFFF;
		fogStrength = fogPacked / 65535.0f;

		// Extract upper 16 bits for alpha cutoff
		uint alphaPacked = (packed >> 16) & 0xFFFF;
		alphaCutoff = alphaPacked / 65535.0f;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		SpriteData sprite = GetSprite(i.instanceID); 

		#if ( S_MODE_DEPTH == 1 && D_REFRACTION_TEST )
		// Ordinary sprites share this batch but must not contribute to the refraction mask.
		if ( sprite.NormalTextureHandle == 0 ) discard;
		#endif

		bool isRefractive = false;
		#if ( D_REFRACTION )
		isRefractive = sprite.NormalTextureHandle != 0;
		#endif

		// Unpack fog strength and alpha cutoff
		float fogStrength, alphaCutoff;
		UnpackFogAndAlpha( sprite.FogStrengthCutout, fogStrength, alphaCutoff );

		float2 uv = GetUV(sprite, i.uv.xy);
		float4 textureColor;

		#if ( D_TEXT )
		if ( ( sprite.RenderFlags & Text ) != 0 )
		{
			// Text draws straight from its glyph outlines at whatever size it lands on screen. TextureHandle,
			// SamplerIndex and Sequence carry the block's instance offset, tile offset and tile row width,
			// SequenceTime its pixel size.
			int2 size = int2( asuint( sprite.SequenceTime ) & 0xFFFF, asuint( sprite.SequenceTime ) >> 16 );
			float2 p = uv * size;
			float ps = TextFootprint( p );
			textureColor = TextComposite( p, ps, sprite.TextureHandle, sprite.SamplerIndex, sprite.Sequence, size, 0 );
			textureColor.rgb = SrgbGammaToLinear( textureColor.rgb );
		}
		else
		#endif
		{
			Texture2D ColorTexture = Bindless::GetTexture2DSrgb( sprite.TextureHandle );
			SamplerState spriteSampler = Bindless::GetSampler( sprite.SamplerIndex );

			textureColor = ColorTexture.Sample( spriteSampler, uv.xy ).rgba;
			if(i.uvBlend > 0.0f)
			{
				float4 sampleB = ColorTexture.Sample( spriteSampler, i.uv.zw ).rgba;
				textureColor = lerp(textureColor, sampleB, i.uvBlend);
			}
		}

		// Unpack exponent and flags from packed int
		uint hasLighting = 0;
		uint exp = 0;
		UnpackLighting( sprite.Lighting, hasLighting, exp );

		// Unpack RGBA8 color
		float4 tintColor = UnpackTintColor( sprite.TintColor );
		float4 overlayColor = UnpackTintColor( sprite.OverlayColor );

		if ( exp == 0 )
		{
			tintColor.rgb = 0.0;
		}
		else
		{
			float3 signedRGB = tintColor.rgb * 255.0 - 128.0;
			tintColor.rgb = signedRGB * exp2( float( exp ) - 128.0 - 7.0 );
		}

		float3 signedOverlayRGB = overlayColor.rgb * 255.0 - 128.0;
		overlayColor.rgb = signedOverlayRGB * exp2( float( exp ) - 128.0 - 7.0 );

		float4 col = float4( textureColor.r, textureColor.g, textureColor.b, textureColor.a );

		if(overlayColor.a > 0.0)
		{
			col.rgb = lerp( col.rgb, overlayColor.rgb, overlayColor.a );
		}
	#if ( S_MODE_DEPTH == 1 )
		// Keep continuous coverage in the quarter-resolution refraction mask, including
		// neutral interior pixels, to avoid holes at normal-map transitions. The color pass
		// independently leaves undistorted pixels alone instead of sampling this copy.
		#if ( !D_REFRACTION_TEST )
		OpaqueFadeDepth(col.a * tintColor.a , i.vPositionSs.xy );
		#endif
		return 1;
	#else
		// Cutoff on untinted alpha so tint opacity doesn't interact with the scissor threshold.
		// No coverage adjustment: sprites only use alpha-to-coverage in depth mode.
		if ( !isRefractive && col.a < alphaCutoff ) discard;
	#endif

		col.rgba *= tintColor;

		if ( sprite.DepthFeather > 0 )
		{
			float3 pos = Depth::GetWorldPosition( i.vPositionSs.xy );
			float3 spriteWorldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

			float dist = distance( pos, spriteWorldPos );
			float feather = clamp(dist / sprite.DepthFeather, 0.0, 1.0 );
			col.a *= feather;
		}

		float cameraFadeNear = f16tof32( sprite.CameraFade & 0xFFFF );
		float cameraFadeFar = f16tof32( sprite.CameraFade >> 16 );
		if ( cameraFadeFar > cameraFadeNear )
		{
			float3 spriteWorldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

			float dist = distance( spriteWorldPos, g_vCameraPositionWs.xyz );
			col.a *= clamp( ( dist - cameraFadeNear ) / ( cameraFadeFar - cameraFadeNear ), 0.0, 1.0 );
		}

		#if ( D_REFRACTION && S_MODE_DEPTH == 0 )
		bool hasRefraction = false;
		float3 refractedBackground = 0.0;
		if ( isRefractive )
		{
			float2 refractionNormal = ReadRefractionNormal( sprite, i, uv );

			// Positive normal Y points up in tangent space, while sprite UV V points down.
			float2 offset = i.refractionBasis.xy * refractionNormal.x - i.refractionBasis.zw * refractionNormal.y;
			offset *= sprite.RefractionStrength;
			hasRefraction = any( offset != 0.0 );
			if ( hasRefraction )
			{
				float2 screenUv = CalculateViewportUv( i.vPositionSs.xy ) + offset * g_vInvViewportSize;

				// Copies can be larger than the viewport. Clamp to its texel centers, not the
				// backing texture's edge, to avoid reading unused pixels in editor/split views.
				float2 halfTexel = g_vInvViewportSize * 0.5;
				screenUv = clamp( screenUv, halfTexel, 1.0 - halfTexel );
				float2 copyUv = screenUv * g_vFrameBufferCopyRectangle.zw;
				refractedBackground = g_tFrameBufferCopyTexture.SampleLevel( g_sBilinearClamp, copyUv, 0 ).rgb;
			}
		}
		#endif

		if ( hasLighting )
		{
			float3 worldPosition = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
			float depth = clamp( dot( worldPosition - g_vCameraPositionWs, g_vCameraDirWs ), ClusterZParams.z, ClusterZParams.w );
			float3 uvw = float3( CalculateViewportUv( i.vPositionSs.xy ), Cluster::DepthToSlice( depth ) * ClusterInvCounts.z );
			col.rgb *= g_tParticleLighting.SampleLevel( g_sTrilinearClamp, uvw, 0 ).rgb;
		}

		if ( g_bFogEnabled )
		{
			float3 worldPosition = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

			#if D_BLEND == 1
				col.a *= Fog::Transmittance( worldPosition, i.vPositionSs.xy, fogStrength );
			#else
				col.rgb = Fog::Apply( worldPosition, i.vPositionSs.xy, col.rgb, fogStrength );
			#endif
		}

		#if ( D_REFRACTION && S_MODE_DEPTH == 0 )
		// Composite the ordinary sprite artwork over the displaced scene in this same draw.
		// Alpha only fades the artwork; even fully transparent pixels retain their refraction.
		// Light and fog the artwork above, never the already shaded scene-color sample.
		if ( hasRefraction ) return float4( lerp( refractedBackground, col.rgb, col.a ), 1.0 );
		#endif
		// An undistorted pixel uses ordinary sprite blending. Re-copying scene color here
		// would expose the quad through filtering, temporal history or intervening translucency.
		return col;
	}
}
