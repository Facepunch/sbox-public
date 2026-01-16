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
	#include "common/shared.hlsl"
}

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	uint instanceID : TEXCOORD8;
	float4 uv : TEXCOORD9;
	float uvBlend : TEXCOORD10;
};

struct VS_INPUT
{
	float3 pos : POSITION < Semantic( None ); >;
};

VS
{
	struct SpriteVertex
	{
		float4 position;
		float4 normal;
		float2 uv;
	};
	StructuredBuffer<SpriteVertex> Vertices < Attribute("Vertices"); >; 

	struct SpriteData
	{
		float3 Position;
		float3 Rotation;
		float2 Scale;
		uint TintColor;
		uint OverlayColor;
		int TextureID;
		int Flags;
		uint BillboardMode;
		uint FogStrengthCutout;
		int Lighting;
		float DepthFeather;
		int SamplerIndex;
		uint Splots;
		int Sequence;
		float SequenceTime;
		float RotationOffset;
		float4 MotionBlur;
		float3 Velocity;
		float4 BlendSheetUV;
		float2 Offset;
	};

	StructuredBuffer<SpriteData> Sprites < Attribute("Sprites"); >; 
	
	StructuredBuffer<uint> SortLUT < Attribute("SortLUT"); >; 

	int IsSorted < Attribute("IsSorted"); >;
	int CurrentBufferSize < Attribute("SpriteCount"); >;

	float4x4 WorldToView < Attribute( "WorldToView" ); >;

	float3 GetScale(float4x4 mat)
	{
		float3 scale;
		scale.x = length(float3(mat._11, mat._12, mat._13));
		scale.y = length(float3(mat._21, mat._22, mat._23));
		scale.z = length(float3(mat._31, mat._32, mat._33));
		return scale;
	}


	SpriteData GetSprite(uint index)
	{
		int spriteIndex = index;
		if ( IsSorted == 1 )
		{
			spriteIndex = SortLUT[CurrentBufferSize - 1 - index];
		}

		return Sprites[spriteIndex];
	}

	float3 GetSpriteOffset(float2 offset, float3 right, float3 up, float3 scale)
	{
		float3 offsetPos = float3(0, 0, 0);
		float pivotScale = 20.0f;
		offsetPos += (offset.x - 0.5f) * right * scale.x * pivotScale;
		offsetPos += (offset.y - 0.5f) * up * scale.y * pivotScale;
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
		
		Sheet::Blended(sprite.BlendSheetUV, sprite.Sequence, sprite.SequenceTime, v.uv, blendedUV.xy, blendedUV.zw, blendAmount );

		PixelInput o;
		o.vPositionWs = mul(transpose(transform), float4(v.position.xyz, 1)).xyz;
		o.vPositionPs = Position3WsToPs( o.vPositionWs );
		o.instanceID = instanceID;
		o.vNormalWs = worldNormal;
		o.uv = blendedUV;
		o.uvBlend = blendAmount;
		return o;
	}
}

PS
{
	#define CUSTOM_MATERIAL_INPUTS 1
	#include "common/pixel.hlsl"

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

	enum SpriteFlags 
	{
		None = 0x0,
		CastShadows = 0x1,
		FlipW = 0x2,
		FlipH = 0x4
	};

	struct SpriteVertex
	{
		float4 position;
		float4 normal;
		float2 uv;
	};
	StructuredBuffer<SpriteVertex> Vertices < Attribute("Vertices"); >; 

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
		uint FogStrengthCutout;  // Lower 16 bits: fog, upper 16 bits: alpha cutout
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
	};

	StructuredBuffer<SpriteData> Sprites < Attribute("Sprites"); >; 
	StructuredBuffer<uint> SortLUT < Attribute("SortLUT"); >; 
	int IsSorted < Attribute("IsSorted"); >;
	int CurrentBufferSize < Attribute("SpriteCount"); >;

	DynamicCombo( D_BLEND, 0..1, Sys( ALL ) );
	DynamicCombo( D_OPAQUE, 0..1, Sys( ALL ) );

	float g_FogStrength < Attribute( "g_FogStrength" ); >;

	// Bindless Accesor for sprite data
	SpriteData GetSprite(uint index)
	{
		int spriteIndex = index;
		if ( IsSorted == 1 )
		{
			spriteIndex = SortLUT[CurrentBufferSize - 1 - index];
		}

		return Sprites[spriteIndex];
	}

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

		// Unpack fog strength and alpha cutoff
		float fogStrength, alphaCutoff;
		UnpackFogAndAlpha( sprite.FogStrengthCutout, fogStrength, alphaCutoff );

		Texture2D ColorTexture = Bindless::GetTexture2D( NonUniformResourceIndex( sprite.TextureHandle ), true );
		SamplerState spriteSampler = Bindless::GetSampler( NonUniformResourceIndex( sprite.SamplerIndex ) );

		float2 uv = GetUV(sprite, i.uv.xy); 
		float4 textureColor = ColorTexture.Sample( spriteSampler, uv.xy ).rgba;
		if(i.uvBlend > 0.0f)
		{
			float4 sampleB = ColorTexture.Sample( spriteSampler, i.uv.zw ).rgba;
			float4 mixed = lerp(textureColor, sampleB, i.uvBlend);
			textureColor = mixed;
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
		col.rgba *= tintColor;

	#if ( S_MODE_DEPTH == 1 )
		OpaqueFadeDepth(col.a * tintColor.a , i.vPositionSs.xy );
		return 1;
	#else
		col.a = AdjustOpacityForAlphaToCoverage( col.a, alphaCutoff, 1.0f, i.vPositionSs.xy );
		if(col.a < alphaCutoff) discard;
	#endif

		if ( sprite.DepthFeather > 0 )
		{
			float3 pos = Depth::GetWorldPosition( i.vPositionSs.xy );

			float dist = distance( pos, i.vPositionWithOffsetWs.xyz );
			float feather = clamp(dist / sprite.DepthFeather, 0.0, 1.0 );
			col.a *= feather;
		}

		if(hasLighting)
		{ 
			FinalCombinerInput_t finalCombinerInput;
			finalCombinerInput.vPositionWs.xyz = i.vPositionWithOffsetWs.xyz;
			finalCombinerInput.vRoughness = 1;
			finalCombinerInput.vNormalWs = i.vNormalWs;
			finalCombinerInput.vPositionSs = i.vPositionSs;
			finalCombinerInput.vPositionWithOffsetWs.xyz = finalCombinerInput.vPositionWs.xyz - g_vHighPrecisionLightingOffsetWs.xyz;
			finalCombinerInput.vSpecularColor = float3( 0.0, 0.0, 0.0 );

			LightingTerms_t lightingTerms = InitLightingTerms();
			ComputeDirectLighting( lightingTerms, finalCombinerInput );
			CalculateIndirectLighting( lightingTerms, finalCombinerInput ); 

			float4 lighting = float4( 0.0, 0.0, 0.0, 1.0 );
			lighting.rgb += lightingTerms.vDiffuse.rgb;
			lighting.rgb += lightingTerms.vIndirectDiffuse.rgb;

			col *= lighting;
		}

		if ( fogStrength > 0 ) 
		{
		#if (D_BLEND == 1)
			const float3 vPositionToCameraWs = i.vPositionWithOffsetWs.xyz - g_vCameraPositionWs;

			if ( g_bGradientFogEnabled )
			{
				col.a *= 1.0 - CalculateGradientFog( i.vPositionWithOffsetWs, vPositionToCameraWs ).a; 
			}

			if ( g_bCubemapFogEnabled )
			{
				col.a *= 1.0 - CalculateCubemapFog( i.vPositionWithOffsetWs, vPositionToCameraWs ).a;
			}

			if ( g_bVolumetricFogEnabled )
			{
				col.a *= CalculateVolumetricFog( i.vPositionWithOffsetWs.xyz, i.vPositionSs.xy ).a;
			}
		#else
			float3 fogged = Fog::Apply( i.vPositionWithOffsetWs, i.vPositionSs.xy, col.rgb );
			col.rgb = lerp( col.rgb, fogged, fogStrength );
		#endif
		}

		return col;
	}
}
