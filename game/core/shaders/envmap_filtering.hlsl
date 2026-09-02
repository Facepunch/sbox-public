#ifndef ENVMAP_FILTERING_H
#define ENVMAP_FILTERING_H

TextureCube<float4> Source < Attribute("Source"); > ;
RWTexture2DArray<float4> Destination1 < Attribute("Destination1"); > ;
RWTexture2DArray<float4> Destination2 < Attribute("Destination2"); > ;
RWTexture2DArray<float4> Destination3 < Attribute("Destination3"); > ;
RWTexture2DArray<float4> Destination4 < Attribute("Destination4"); > ;
RWTexture2DArray<float4> Destination5 < Attribute("Destination5"); > ;
RWTexture2DArray<float4> Destination6 < Attribute("Destination6"); > ;

SamplerState LinearSampler < Filter(BILINEAR); > ;

uint BaseResolution < Attribute("BaseResolution"); > ;

//--------------------------------------------------------------------------------------

// #include "common/thirdparty/envmap/coeffs_8_taps.hlsl"
#if D_QUALITY == 0
	#include "common/thirdparty/envmap/coeffs_16_taps.hlsl"
#elif D_QUALITY == 1
	#include "common/thirdparty/envmap/coeffs_32_taps.hlsl"
#endif

//--------------------------------------------------------------------------------------

#define NUM_MIPS 7

// Evaluated at compile time
float4 GetCoeff( int mip_level, int axis, int iSuperTap, int j )
{
	float4 v = 0;
	[unroll]
	for ( int m = 1; m < NUM_MIPS; m++ )
	{
		[unroll]
		for ( int a = 0; a < 3; a++ )
		{
			if ( m == mip_level && a == axis ) v = coeffs[m][j][(NUM_TAPS / 4) * a + iSuperTap];
		}
	}
	return v;
}

//--------------------------------------------------------------------------------------

// Copyright 2016 Activision Publishing, Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software
// is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

void get_dir( out float3 dir, in float2 uv, in int face )
{
	switch ( face )
	{
	case 0:
		dir[0] = 1;
		dir[1] = uv[1];
		dir[2] = -uv[0];
		break;
	case 1:
		dir[0] = -1;
		dir[1] = uv[1];
		dir[2] = uv[0];
		break;
	case 2:
		dir[0] = uv[0];
		dir[1] = 1;
		dir[2] = -uv[1];
		break;
	case 3:
		dir[0] = uv[0];
		dir[1] = -1;
		dir[2] = uv[1];
		break;
	case 4:
		dir[0] = uv[0];
		dir[1] = uv[1];
		dir[2] = 1;
		break;
	default:
		dir[0] = -uv[0];
		dir[1] = uv[1];
		dir[2] = -1;
		break;
	}
}

float3 SampleEnvironmentMapLevel(float3 vReflectionDirWs, float flLevel = 0.0f)
{
    float3 vColor = Source.SampleLevel(LinearSampler, vReflectionDirWs, flLevel ).xyz;
    return vColor;
}

void AdjustIdAndMipLevel(inout int3 id, inout int mip_level)
{
	uint offset = 0;
	uint baseRes = BaseResolution / 2; // Start at mip 1 (half of base resolution)
	
	for (int i = 1; i < NUM_MIPS; i++)
	{
		uint texelCount = baseRes * baseRes;
		
		if (id.x < offset + texelCount)
		{
			mip_level = i;
			id.x -= offset;
			return;
		}
		
		offset += texelCount;
		baseRes /= 2;
	}
}

void StoreEnvironmentMap( int3 id, int mip_level, float4 color )
{
	switch ( mip_level )
	{
	case 1:
		Destination1[id] = color;
		break;
	case 2:
		Destination2[id] = color;
		break;
	case 3:
		Destination3[id] = color;
		break;
	case 4:
		Destination4[id] = color;
		break;
	case 5:
		Destination5[id] = color;
		break;
    case 6:
        Destination6[id] = color;
	}
}

void FilterCubemapFast( uint3 DispatchThreadID )
{
    // INPUT:
    // id.x = the linear address of the texel (ignoring face)
    // id.y = the face
    // -> use to index output texture
    // id.x = texel x
    // id.y = texel y
    // id.z = face
    int3 id = DispatchThreadID;
    int mip_level = 0;

	// Adjusted level determination logic
	AdjustIdAndMipLevel( id, mip_level );

	// determine dir / pos for the texel
	float3 dir, adir, frameZ;
	{
        id.z = id.y;
        int res = BaseResolution >> mip_level;
		id.y = id.x / res;
		id.x -= id.y * res;

		float2 uv;
		uv.x = ( (float)id.x * 2.0f + 1.0f ) / (float)res - 1.0f;
		uv.y = -( (float)id.y * 2.0f + 1.0f ) / (float)res + 1.0f;

		get_dir( dir, uv, id.z );
		frameZ = normalize( dir );

		adir[0] = abs( dir[0] );
		adir[1] = abs( dir[1] );
		adir[2] = abs( dir[2] );
	}


	// GGX gather colors
	float4 color = 0;
	for ( int axis = 0; axis < 3; axis++ )
	{
		const int otherAxis0 = 1 - ( axis & 1 ) - ( axis >> 1 );
		const int otherAxis1 = 2 - ( axis >> 1 );

		float frameweight = ( max( adir[otherAxis0], adir[otherAxis1] ) - .75f ) / .25f;
		if ( frameweight > 0 )
		{
			// determine frame
			float3 UpVector;
			switch ( axis )
			{
			case 0:
				UpVector = float3( 1, 0, 0 );
				break;
			case 1:
				UpVector = float3( 0, 1, 0 );
				break;
			default:
				UpVector = float3( 0, 0, 1 );
				break;
			}
			
			float3 frameX = normalize( cross( UpVector, frameZ ) );
			float3 frameY = cross( frameZ, frameX );

			// calculate parametrization for polynomial
			float Nx = dir[otherAxis0];
			float Ny = dir[otherAxis1];
			float Nz = adir[axis];

			float NmaxXY = max( abs( Ny ), abs( Nx ) );
			Nx /= NmaxXY;
			Ny /= NmaxXY;

			float theta;
			if ( Ny < Nx )
			{
				if ( Ny <= -.999 )
					theta = Nx;
				else
					theta = Ny;
			}
			else
			{
				if ( Ny >= .999 )
					theta = -Nx;
				else
					theta = -Ny;
			}

			float phi;
			if ( Nz <= -.999 )
				phi = -NmaxXY;
			else if ( Nz >= .999 )
				phi = NmaxXY;
			else
				phi = Nz;

			float theta2 = theta*theta;
			float phi2 = phi*phi;

			// sample
			[unroll]
			for ( int iSuperTap = 0; iSuperTap < NUM_TAPS / 4; iSuperTap++ )
			{
				float4 coeffsDir0 = GetCoeff( mip_level, axis, iSuperTap, 0 );
				float4 coeffsDir1 = GetCoeff( mip_level, axis, iSuperTap, 1 );
				float4 coeffsDir2 = GetCoeff( mip_level, axis, iSuperTap, 2 );
				float4 coeffsLevel = GetCoeff( mip_level, axis, iSuperTap, 3 );
				float4 coeffsWeight = GetCoeff( mip_level, axis, iSuperTap, 4 );
                [unroll]
                for (int iSubTap = 0; iSubTap < 4; iSubTap++) {
                    // determine sample attributes (dir, weight, mip_level)
                    float3 sample_dir = frameX * coeffsDir0[iSubTap] + frameY * coeffsDir1[iSubTap] + frameZ * coeffsDir2[iSubTap];

                    float sample_level = coeffsLevel[iSubTap];

                    float sample_weight = coeffsWeight[iSubTap];
					sample_weight *= frameweight;

					// adjust for jacobian
					sample_dir /= max( abs( sample_dir[0] ), max( abs( sample_dir[1] ), abs( sample_dir[2] ) ) );
					sample_level += 0.75f * log2( dot( sample_dir, sample_dir ) );
					
					// Reduce fireflies by sampling a bit higher mip level
                    sample_level += float(mip_level) / 5.0f;

					// sample cubemap
					color.xyz += pow( SampleEnvironmentMapLevel( sample_dir, sample_level ).xyz, 1.0f / 2.2f ) * sample_weight;
					color.w += sample_weight;
				}
			}
		}
	}
	color /= color.w;

	// write color
	color.x = max( 0, color.x );
	color.y = max( 0, color.y );
	color.z = max( 0, color.z );
    color.w = 1;
	StoreEnvironmentMap( id, mip_level, pow( color, 2.2f ) );
}

//-----------------------------------------------------------------------

// Copyright 2016 Activision Publishing, Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software
// is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

TextureCube DownsampleInput < Attribute("DownsampleInput"); > ;
RWTexture2DArray<float4> DownsampleOutput < Attribute("DownsampleOutput"); > ;
int Size < Attribute("Size"); > ;

void get_dir_0(out float3 dir, in float u, in float v)
{
    dir[0] = 1;
    dir[1] = v;
    dir[2] = -u;
}
void get_dir_1(out float3 dir, in float u, in float v)
{
    dir[0] = -1;
    dir[1] = v;
    dir[2] = u;
}
void get_dir_2(out float3 dir, in float u, in float v)
{
    dir[0] = u;
    dir[1] = 1;
    dir[2] = -v;
}
void get_dir_3(out float3 dir, in float u, in float v)
{
    dir[0] = u;
    dir[1] = -1;
    dir[2] = v;
}
void get_dir_4(out float3 dir, in float u, in float v)
{
    dir[0] = u;
    dir[1] = v;
    dir[2] = 1;
}
void get_dir_5(out float3 dir, in float u, in float v)
{
    dir[0] = -u;
    dir[1] = v;
    dir[2] = -1;
}

void get_dir(out float3 dir, in float u, in float v, in int face)
{
    switch (face)
    {
    case 0:
        get_dir_0(dir, u, v);
        break;
    case 1:
        get_dir_1(dir, u, v);
        break;
    case 2:
        get_dir_2(dir, u, v);
        break;
    case 3:
        get_dir_3(dir, u, v);
        break;
    case 4:
        get_dir_4(dir, u, v);
        break;
    default:
        get_dir_5(dir, u, v);
        break;
    }
	dir = normalize(dir);
}

float calcWeight(float u, float v)
{
    float val = u * u + v * v + 1;
    return val * sqrt(val);
}

void DownsampleCubemap( uint3 id )
{
    uint res_lo = Size;


    if (id.x < res_lo && id.y < res_lo)
    {
        float inv_res_lo = rcp((float)res_lo);

        float u0 = ((float)id.x * 2.0f + 1.0f - .75f) * inv_res_lo - 1.0f;
        float u1 = ((float)id.x * 2.0f + 1.0f + .75f) * inv_res_lo - 1.0f;

        float v0 = ((float)id.y * 2.0f + 1.0f - .75f) * -inv_res_lo + 1.0f;
        float v1 = ((float)id.y * 2.0f + 1.0f + .75f) * -inv_res_lo + 1.0f;

        float weights[4];
        weights[0] = calcWeight(u0, v0);
        weights[1] = calcWeight(u1, v0);
        weights[2] = calcWeight(u0, v1);
        weights[3] = calcWeight(u1, v1);

        const float wsum = 0.5f / (weights[0] + weights[1] + weights[2] + weights[3]);
        [unroll]
        for (int i = 0; i < 4; i++)
            weights[i] = weights[i] * wsum + .125f;

#if 1
        float3 dir;
        float4 color;
        switch (id.z)
        {
        case 0:
            get_dir_0(dir, u0, v0);
            color = DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[0];

            get_dir_0(dir, u1, v0);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[1];

            get_dir_0(dir, u0, v1);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[2];

            get_dir_0(dir, u1, v1);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[3];
            break;
        case 1:
            get_dir_1(dir, u0, v0);
            color = DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[0];

            get_dir_1(dir, u1, v0);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[1];

            get_dir_1(dir, u0, v1);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[2];

            get_dir_1(dir, u1, v1);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[3];
            break;
        case 2:
            get_dir_2(dir, u0, v0);
            color = DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[0];

            get_dir_2(dir, u1, v0);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[1];

            get_dir_2(dir, u0, v1);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[2];

            get_dir_2(dir, u1, v1);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[3];
            break;
        case 3:
            get_dir_3(dir, u0, v0);
            color = DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[0];

            get_dir_3(dir, u1, v0);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[1];

            get_dir_3(dir, u0, v1);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[2];

            get_dir_3(dir, u1, v1);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[3];
            break;
        case 4:
            get_dir_4(dir, u0, v0);
            color = DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[0];

            get_dir_4(dir, u1, v0);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[1];

            get_dir_4(dir, u0, v1);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[2];

            get_dir_4(dir, u1, v1);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[3];
            break;
        default:
            get_dir_5(dir, u0, v0);
            color = DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[0];

            get_dir_5(dir, u1, v0);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[1];

            get_dir_5(dir, u0, v1);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[2];

            get_dir_5(dir, u1, v1);
            color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[3];
            break;
        }
#else
        float3 dir;
        get_dir(dir, u0, v0, id.z);
        float4 color = DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[0];

        get_dir(dir, u1, v0, id.z);
        color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[1];

        get_dir(dir, u0, v1, id.z);
        color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[2];

        get_dir(dir, u1, v1, id.z);
        color += DownsampleInput.SampleLevel(LinearSampler, dir, 0) * weights[3];
#endif

        DownsampleOutput[id] = color;
    }
}

#endif // ENVMAP_FILTERING_H