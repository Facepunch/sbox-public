HEADER
{
	DevShader = true;
	Description = "Compute vertices for sprite rendering";
}

MODES
{
	Default();
}

FEATURES
{
}

COMMON
{
	#include "common/shared.hlsl"
}

CS
{
	// Helper function to unpack RGBA8 uint to float4
	float4 UnpackColor(uint packedColor)
	{
		float4 color;
		color.r = ((packedColor      ) & 0xFF) / 255.0f;
		color.g = ((packedColor >>  8) & 0xFF) / 255.0f;
		color.b = ((packedColor >> 16) & 0xFF) / 255.0f;
		color.a = ((packedColor >> 24) & 0xFF) / 255.0f;
		return color;
	}

	// Helper function to pack float4 to RGBA8 uint
	uint PackColor(float4 color)
	{
		uint r = (uint)(color.r * 255.0f);
		uint g = (uint)(color.g * 255.0f);
		uint b = (uint)(color.b * 255.0f);
		uint a = (uint)(color.a * 255.0f);
		return (r | (g << 8) | (b << 16) | (a << 24));
	}

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
		uint FogStrengthCutout;
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
	StructuredBuffer<SpriteData> SpriteBuffer < Attribute( "Sprites" ); >;
	RWStructuredBuffer<SpriteData> SpriteBufferOut < Attribute( "SpriteBufferOut" ); >;

	// Sorting related
	RWStructuredBuffer<float> DistanceBuffer < Attribute( "DistanceBuffer" ); >;
	float3 CameraPosition < Attribute ("CameraPosition"); >;

	int SpriteCount < Attribute( "SpriteCount"); >;
	RWStructuredBuffer<int> AtomicCounter < Attribute( "AtomicCounter" ); >;

	// Motion blur
	float4 g_MotionBlur < Attribute( "g_MotionBlur" ); >;
	
	float4x4 SetScale(float4x4 matrix, float3 newScale)
	{
		// Remove old scale by normalizing each basis vector (column)
		float4x4 newMatrix = matrix;
		newMatrix[0].xyz = normalize(matrix[0].xyz) * newScale.x;
		newMatrix[1].xyz = normalize(matrix[1].xyz) * newScale.y;
		newMatrix[2].xyz = matrix[2].xyz * newScale.z;
		return newMatrix;
	}

	float3 ExtractScale(float4x4 m)
	{
		float3 scale;
		scale.x = length(m[0].xyz); // X axis basis vector
		scale.y = length(m[1].xyz); // Y axis basis vector
		scale.z = length(m[2].xyz); // Z axis basis vector
		return scale;
	}

	float4x4 CreateRotationMatrix(float3 eulerRad)
	{
		float cx = cos(eulerRad.x);
		float sx = sin(eulerRad.x);
		float cy = cos(eulerRad.y);
		float sy = sin(eulerRad.y);
		float cz = cos(eulerRad.z);
		float sz = sin(eulerRad.z);

		float4x4 rotX = float4x4(
			1, 0, 0, 0,
			0, cx, -sx, 0,
			0, sx, cx, 0,
			0, 0, 0, 1
		);

		float4x4 rotY = float4x4(
			cy, 0, sy, 0,
			0, 1, 0, 0,
			-sy, 0, cy, 0,
			0, 0, 0, 1
		);

		float4x4 rotZ = float4x4(
			cz, -sz, 0, 0,
			sz, cz, 0, 0,
			0, 0, 1, 0,
			0, 0, 0, 1
		);

		return mul(mul(rotZ, rotY), rotX);
	}

	#define FLT_MAX 3.402823466e+38f

	groupshared int groupWriteSize[64];
    groupshared int groupWriteOffset[64];
    groupshared int groupBaseOffset;

	// Here we calculate sqrt distance, its faster
	float CalculateDistance(float3 worldPosition)
	{
		float3 delta = (worldPosition - CameraPosition);
		return dot(delta, delta);
	}

	[numthreads( 64, 1, 1 ) ]
	void MainCs( uint2 dispatchId : SV_DispatchThreadID, uint3 GTid : SV_GroupThreadID )
	{
		uint i = dispatchId.x; 

		bool isValid = i < SpriteCount;

		SpriteData sprite = SpriteBuffer[i];
		float3 cameraAxis = g_vCameraDirWs;
		float3 cameraUp = g_vCameraUpDirWs;

		// Transfer sprite to out buffer
		if(isValid)
		{
			if ( sprite.RotationOffset > -1 )
			{	
				float4 ss = mul( g_matWorldToView, float4( sprite.Velocity, 0 ) );
				ss.z = 0;
				ss = normalize( ss );
				sprite.Rotation.z += ToDegrees * atan2( ss.x, ss.y ) + sprite.RotationOffset;
			}


			DistanceBuffer[i] = CalculateDistance(sprite.Position);
			SpriteBufferOut[i] = sprite;
		}
		else
		{
			DistanceBuffer[i] = FLT_MAX;
		}

		int writeSize = 0;
		int splotCount = 0;
		int leading = 1;

		groupWriteSize[GTid.x] = 0;

		if(isValid)
		{
			splotCount = sprite.Splots;

			leading = sprite.MotionBlur.r > 1 ? 2 : 1;
			writeSize = splotCount * leading;
		}

		groupWriteSize[GTid.x] = writeSize;

		GroupMemoryBarrierWithGroupSync();
		
		// Calculate prefix sum for group offsets
		if (GTid.x == 0)
		{
			groupWriteOffset[0] = 0;

			for (uint j = 1; j < 64; ++j)
			{
				groupWriteOffset[j] = groupWriteOffset[j - 1] + groupWriteSize[j - 1];
			}
			
			// Single atomic add per group instead of per thread
			int totalGroupSize = groupWriteOffset[63] + groupWriteSize[63];
			InterlockedAdd(AtomicCounter[0], totalGroupSize, groupBaseOffset);
		}
		
		GroupMemoryBarrierWithGroupSync();

		if ( isValid && splotCount > 0 )
		{
			float4 tintColor = UnpackColor(sprite.TintColor);
			float4 overlayColor = UnpackColor(sprite.OverlayColor);

			if(overlayColor.a > 0.0)
			{
				tintColor.rgb = lerp( tintColor.rgb, overlayColor.rgb, overlayColor.a );
			}
			tintColor.a *= sprite.MotionBlur.a;

			float3 velocity = sprite.Velocity;
			float3 scale = velocity * (sprite.MotionBlur.z * 0.002f);

			int index = 0;
			int writeOffset = SpriteCount + groupBaseOffset + groupWriteOffset[GTid.x];
			for ( int f = 0; f < splotCount; f++ )
			{
				tintColor.a *= sprite.MotionBlur.a;
				tintColor.a = clamp( tintColor.a, 0.001, 1 );

				uint writeLocation = writeOffset + index;

				float3 moveStep = scale * f;

				// Leading trail
				SpriteData b = sprite;
				b.TintColor = PackColor(tintColor); // Pack the modified color back

				float3 spritePosition = b.Position;
				if ( leading > 1 )
				{
					float3 pos = spritePosition;
					pos += moveStep;

					b.Position = pos;
					// We fill distance buffer for sorting
					DistanceBuffer[writeLocation] = CalculateDistance(b.Position);
					SpriteBufferOut[writeLocation] = b;

					index++;
				}

				// Move motionBlur sprite
				spritePosition -= moveStep;
				b.Position = spritePosition;

				// Fil distance buffer for sorting
				writeLocation = writeOffset + index;
				DistanceBuffer[writeLocation] = CalculateDistance(b.Position);

				SpriteBufferOut[writeLocation] = b;

				index++;
			}
		}
	}
}
