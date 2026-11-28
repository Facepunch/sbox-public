// Bakes lightmap luxels for BakedLightmapSystem: static light strengths, indirect light and where it comes from, traced through a compute BVH
HEADER
{
	DevShader = true;
	Description = "Bakes static light strengths and indirect light into lightmap luxels";
}

MODES
{
	Default();
}

CS
{
	#include "system.fxc"
	#include "common/classes/Bvh.hlsl"
	#include "common/classes/BinnedLight.hlsl"

	// 0 traces a pass and 2 prepares the luxels for it. The rest write what's traced so far into the textures the scene is lit from:
	// 1 the lightmap's, 3 a probe volume's and 4 the fog's
	DynamicCombo( D_STAGE, 0..4, Sys( ALL ) );

	struct Luxel
	{
		float3 Position;
		uint Pixel;		// x and y in the lightmap, 16 bits each
		float3 Normal;
		uint Open;		// Which quarters of it it can be lit from, a bit each: across in the first bit, down in the second
		float3 Du;		// A luxel across the surface
		float Unused2;
		float3 Dv;		// And a luxel down it
		float Unused3;
	};

	struct BakeLight
	{
		float3 ToSun;	// Towards a directional light
		uint Channel;	// The lightmap channel it's in
		float SunSize;	// A directional light's radius in radians, from its shadow hardness
		float3 Fog;		// The colour it gives fog
		uint NoShadows;	// Its shadows are off
	};

	// What a ray hits: the colour light bounces off it with and the light it gives off
	struct Surface
	{
		float4 Albedo;
		float4 Emission;
	};

	// A traced triangle, by its id: its surface, and its corners in the lightmap with Uv0.x below 0 if it isn't in it
	struct BakeTriangle
	{
		uint Surface;
		float2 Uv0;
		float2 Uv1;
		float2 Uv2;
	};

	RWStructuredBuffer<Luxel> Luxels < Attribute( "Luxels" ); >;	// The prepare stage moves them
	StructuredBuffer<Surface> Surfaces < Attribute( "Surfaces" ); >;
	StructuredBuffer<BakeTriangle> Triangles < Attribute( "Triangles" ); >;
	StructuredBuffer<int> Slots < Attribute( "Slots" ); >;	// The luxel at each lightmap pixel, -1 for none
	StructuredBuffer<BinnedLight> Lights < Attribute( "Lights" ); >;
	StructuredBuffer<BakeLight> BakeLights < Attribute( "BakeLights" ); >;

	// Summed over passes, the baker divides by how many it ran
	RWStructuredBuffer<float4> OutStrengths < Attribute( "OutStrengths" ); >;
	RWStructuredBuffer<float4> OutIrradiance < Attribute( "OutIrradiance" ); >;	// With how open the luxel is in alpha, or how many of a probe's rays went into the backs of surfaces
	RWStructuredBuffer<float4> OutDirection < Attribute( "OutDirection" ); >;	// Where its indirect light came from, by brightness. Each sample's brightness squared in alpha, for how noisy it is
	RWStructuredBuffer<uint> OutIndices < Attribute( "OutIndices" ); >;
	// The light that reached each luxel off no more than so many bounces, a layer of every luxel for each count below
	// g_nBounces, starting with none: straight from the sky and glowing surfaces. OutIrradiance is the layer above them
	RWStructuredBuffer<float4> OutBounces < Attribute( "OutBounces" ); >;
	int g_nBounces < Attribute( "Bounces" ); >;

	RWStructuredBuffer<float4> OutSpread < Attribute( "OutSpread" ); >;		// Each sample's strengths squared, for how noisy its shadows are

	// Surfaces nearer than this occlude a luxel, 0 for none
	float g_flOcclusionDistance < Attribute( "OcclusionDistance" ); >;

	int g_nLuxelOffset < Attribute( "LuxelOffset" ); >;
	int g_nLuxelCount < Attribute( "LuxelCount" ); >;
	int g_nLightCount < Attribute( "LightCount" ); >;
	int g_nLightmapWidth < Attribute( "LightmapWidth" ); >;
	int g_nLightmapHeight < Attribute( "LightmapHeight" ); >;
	int g_nPass < Attribute( "Pass" ); >;

	// The skybox's cube, looked up as sky.shader does: by the direction in the sky's own space
	TextureCube g_tSky < Attribute( "SkyTexture" ); >;
	SamplerState g_sSky < Filter( BILINEAR ); >;
	float3 g_vSkyTint < Attribute( "SkyTint" ); >;
	float3 g_vSkyForward < Attribute( "SkyForward" ); >;
	float3 g_vSkyLeft < Attribute( "SkyLeft" ); >;
	float3 g_vSkyUp < Attribute( "SkyUp" ); >;

	// Off the surface, so a ray doesn't hit the triangle it starts on
	#define RAY_BIAS 0.05

	// A blurred mip, so a sun painted on the sky doesn't speckle the bake
	#define SKY_MIP 3.0

	uint Hash( uint x )
	{
		x ^= x >> 16;
		x *= 0x7feb352du;
		x ^= x >> 15;
		x *= 0x846ca68bu;
		x ^= x >> 16;
		return x;
	}

	float Random( inout uint nState )
	{
		nState = Hash( nState + 0x9e3779b9u );
		return float( nState >> 8 ) * ( 1.0 / 16777216.0 );
	}

	float3 Sky( float3 vDirection )
	{
		const float3 vLookup = float3( dot( vDirection, g_vSkyForward ), dot( vDirection, g_vSkyLeft ), dot( vDirection, g_vSkyUp ) );
		return g_tSky.SampleLevel( g_sSky, vLookup, SKY_MIP ).rgb * g_vSkyTint;
	}

	// Two numbers spread evenly over the passes, starting somewhere else in every luxel
	float2 Spread( uint nLuxel, uint nSalt )
	{
		const float2 vStart = float2( Hash( nLuxel * 2u + nSalt ), Hash( nLuxel * 2u + nSalt + 0x68bc21ebu ) ) * ( 1.0 / 4294967296.0 );
		return frac( vStart + float( g_nPass ) * float2( 0.7548776662, 0.5698402910 ) );
	}

	// Cosine weighted around a normal, from two numbers
	float3 SpreadDirection( float3 vNormal, float2 vSample )
	{
		const float3 vTangent = normalize( abs( vNormal.z ) < 0.9 ? cross( vNormal, float3( 0.0, 0.0, 1.0 ) ) : cross( vNormal, float3( 1.0, 0.0, 0.0 ) ) );
		const float3 vBitangent = cross( vNormal, vTangent );
		const float r = sqrt( vSample.x );
		const float a = 6.2831853 * vSample.y;
		return normalize( vTangent * ( r * cos( a ) ) + vBitangent * ( r * sin( a ) ) + vNormal * sqrt( max( 0.0, 1.0 - vSample.x ) ) );
	}

	// A light's falloff and cone, which a baked light skips at runtime. Not N.L, the pixel's own normal decides that.
	// With slack it's still a little something that far past its radius and cone: a luxel beyond its edge keeps its index
	// with no strength, so the filtered strength fades out there instead of stepping along luxels where the index stops
	float Attenuation( uint nLight, float3 vPosition, out float3 vToLight, out float flDistance, float flSlack = 0.0 )
	{
		const BinnedLight light = Lights[nLight];

		if ( light.Type == LightType::LightTypeDirectional )
		{
			vToLight = BakeLights[nLight].ToSun;
			flDistance = 1e30;
			return 1.0;
		}

		const float3 vRay = light.GetPosition() - vPosition;
		const float flDistSq = dot( vRay, vRay );
		flDistance = sqrt( flDistSq );
		vToLight = vRay / max( flDistance, 1e-6 );

		const float flOuter = light.SpotLightInnerOuterConeCosines.y;
		const float flCone = dot( vToLight, -light.GetDirection() ) - flOuter;
		const float flNear = flSlack > 0.0 && flDistance < sqrt( light.RadiusSquared ) + flSlack && flCone * flDistance > -flSlack * sqrt( max( 1.0 - flOuter * flOuter, 1e-4 ) ) ? 1e-6 : 0.0;
		if ( flDistSq > light.RadiusSquared || flCone <= 0.0 )
			return flNear;

		// CalculateDistanceFalloff
		const float flClampedSq = max( flDistSq, 1.0 );
		const float flFalloff = saturate( 1.0 / dot( float2( sqrt( flClampedSq ), flClampedSq ), float2( light.LinearFalloff, light.QuadraticFalloff ) ) - light.FalloffBias );
		return flFalloff * flCone * light.SpotLightInnerOuterConeCosines.z + flNear;
	}

	// Whether a point sees a light, towards a point across it, -1 to 1 each way, so shadows soften over passes
	float Visibility( uint nLight, float3 vPosition, float3 vToLight, float flDistance, float2 vAcross )
	{
		if ( BakeLights[nLight].NoShadows != 0u )
			return 1.0;

		const BinnedLight light = Lights[nLight];
		const bool bSun = light.Type == LightType::LightTypeDirectional;

		// Every luxel looks at the same point of the sun each pass, so a pass is one sharp shadow and they add up to a
		// soft one with no noise between luxels
		if ( bSun )
			vAcross = Spread( nLight, 2u ) * 2.0 - 1.0;

		const float3 vTangent = normalize( cross( vToLight, abs( vToLight.z ) < 0.9 ? float3( 0.0, 0.0, 1.0 ) : float3( 1.0, 0.0, 0.0 ) ) );
		const float3 vJitter = vTangent * vAcross.x + cross( vToLight, vTangent ) * vAcross.y;

		if ( bSun )
			return Bvh::Occluded( vPosition, normalize( vToLight + vJitter * BakeLights[nLight].SunSize ), 1e30 ) ? 0.0 : 1.0;

		const float3 vRay = vToLight * flDistance + vJitter * max( light.ShapeSize.x, 1.0 );
		const float flLength = length( vRay );
		return Bvh::Occluded( vPosition, vRay / flLength, flLength - RAY_BIAS ) ? 0.0 : 1.0;
	}

	// What every static light gives a surface, before its albedo
	float3 DirectLight( float3 vPosition, float3 vNormal, inout uint nState )
	{
		float3 vLight = float3( 0.0, 0.0, 0.0 );

		for ( int l = 0; l < g_nLightCount; l++ )
		{
			float3 vToLight;
			float flDistance;
			const float flStrength = Attenuation( l, vPosition, vToLight, flDistance );
			const float flNDotL = dot( vNormal, vToLight );
			if ( flStrength <= 0.0 || flNDotL <= 0.0 )
				continue;

			vLight += Lights[l].Color * ( flStrength * flNDotL * Visibility( l, vPosition, vToLight, flDistance, float2( Random( nState ), Random( nState ) ) * 2.0 - 1.0 ) );
		}

		return vLight;
	}

	// What the static lights give a surface a ray hit, before its albedo, and the luxel there. On a lightmapped surface
	// it's read from that luxel's strengths traced so far, with nothing more to trace. Anywhere else, and before
	// anything is traced, the lights are traced there and there's no luxel, -1
	float3 HitLight( BakeTriangle tri, float2 vBarycentrics, float3 vPosition, float3 vNormal, bool bBack, inout uint nState, out int nSlot )
	{
		const float2 vUv = tri.Uv0 + ( tri.Uv1 - tri.Uv0 ) * vBarycentrics.x + ( tri.Uv2 - tri.Uv0 ) * vBarycentrics.y;
		const int2 vPixel = clamp( int2( vUv ), int2( 0, 0 ), int2( g_nLightmapWidth - 1, g_nLightmapHeight - 1 ) );
		nSlot = tri.Uv0.x >= 0.0 && !bBack && g_nPass > 0 ? Slots[vPixel.y * g_nLightmapWidth + vPixel.x] : -1;
		if ( nSlot < 0 )
			return DirectLight( vPosition, vNormal, nState );

		const float4 vStrengths = OutStrengths[nSlot] / float( g_nPass );
		const uint nIndices = OutIndices[nSlot];
		float3 vLight = float3( 0.0, 0.0, 0.0 );

		for ( uint c = 0; c < 4; c++ )
		{
			const uint nLight = ( nIndices >> ( c * 8 ) ) & 0xFFu;
			if ( nLight >= (uint)g_nLightCount )
				continue;

			const BinnedLight light = Lights[nLight];
			const float3 vToLight = light.Type == LightType::LightTypeDirectional ? BakeLights[nLight].ToSun : normalize( light.GetPosition() - vPosition );
			vLight += light.Color * ( vStrengths[c] * max( 0.0, dot( vNormal, vToLight ) ) );
		}

		return vLight;
	}

	// The luxel each lightmap luxel shows, and its material's tangent with the side its bitangent is on
	StructuredBuffer<uint> Froms < Attribute( "Froms" ); >;
	StructuredBuffer<float4> Tangents < Attribute( "Tangents" ); >;

	RWTexture2D<float4> LiveStrengths < Attribute( "LiveStrengths" ); >;
	RWTexture2D<float4> LiveIrradiance < Attribute( "LiveIrradiance" ); >;
	RWTexture2D<float4> LiveDirection < Attribute( "LiveDirection" ); >;

	// What's traced so far into the lightmap
	void Resolve( uint nLuxel )
	{
		const uint nFrom = Froms[nLuxel];
		const float flSamples = float( g_nPass ) + ( nFrom < (uint)g_nLuxelOffset ? 1.0 : 0.0 );
		if ( flSamples == 0.0 )
			return;

		const uint nPixel = Luxels[nLuxel].Pixel;
		const uint2 vPixel = uint2( nPixel & 0xFFFFu, nPixel >> 16 );
		const float4 vIrradiance = OutIrradiance[nFrom] / flSamples;

		LiveStrengths[vPixel] = sqrt( OutStrengths[nFrom] / flSamples );
		LiveIrradiance[vPixel] = float4( vIrradiance.rgb, 1.0 );

		// As vr_lighting.fxc reads it: where the indirect light comes from in the tangent space complex.shader's vertex
		// shader makes, how much of it comes from everywhere else, and how open the luxel is
		const float3 vNormal = Luxels[nFrom].Normal;
		const float4 vTangent = Tangents[nFrom];
		const float3 vU = normalize( vTangent.xyz );
		const float3 vV = cross( vNormal, vU ) * vTangent.w;
		const float3 vTowards = OutDirection[nFrom].xyz / flSamples;
		const float flLight = dot( vIrradiance.rgb, float3( 0.2126, 0.7152, 0.0722 ) );

		float2 vXY = float2( dot( vTowards, vU ), dot( vTowards, vV ) );
		const float flLength = length( vXY ) + max( 0.0, dot( vTowards, vNormal ) );
		vXY = flLength > 1e-6 ? vXY / flLength : float2( 0.0, 0.0 );
		vXY *= min( 1.0, 0.95 / max( length( vXY ), 1e-6 ) );

		LiveDirection[vPixel] = saturate( float4( vXY * 0.5 + 0.5, flLight > 1e-6 ? 1.0 - length( vTowards ) / flLight : 1.0, vIrradiance.w ) );
	}

	float g_flLuxelSize < Attribute( "LuxelSize" ); >;

	// Whether a luxel can be lit from somewhere across it: nothing's in the way, and there's a surface beneath
	bool Open( float3 vOrigin, float3 vNormal, float3 vOffset )
	{
		const float flOffset = length( vOffset );
		return flOffset > 1e-4 && !Bvh::Occluded( vOrigin, vOffset / flOffset, flOffset + RAY_BIAS ) && Bvh::Occluded( vOrigin + vOffset, -vNormal, RAY_BIAS * 4.0 );
	}

	// Once before tracing, what stays the same every pass: where a luxel is, where in it it can be lit from and its lights
	#if D_STAGE == 2
	void Prepare( uint nLuxel, bool bSurface )
	{
		Luxel luxel = Luxels[nLuxel];
		uint nOpen = 0;

		if ( bSurface )
		{
			// A luxel behind a surface moves along its own out to that surface's open side, the nearest a bilinear tap reaches
			// A wall thinner than a luxel still leaks along its base, its two sides share luxels. Split charts along walls to fix that
			const float3 vU = normalize( cross( luxel.Normal, abs( luxel.Normal.z ) < 0.9 ? float3( 0.0, 0.0, 1.0 ) : float3( 1.0, 0.0, 0.0 ) ) );
			const float3 vV = cross( luxel.Normal, vU );
			float3 vOrigin = luxel.Position + luxel.Normal * RAY_BIAS;
			float flReach = g_flLuxelSize * 1.5;
			float3 vPush = float3( 0.0, 0.0, 0.0 );

			for ( int i = 0; i < 8; i++ )
			{
				const float3 vDirection = vU * cos( i * 0.7853982 ) + vV * sin( i * 0.7853982 );

				// Leaving through a surface's back means it started behind it
				Bvh::Hit hit;
				if ( !Bvh::Trace( vOrigin, vDirection, flReach, hit ) || dot( hit.Normal, vDirection ) <= 0.0 )
					continue;

				flReach = hit.Distance;
				vPush = vDirection * ( hit.Distance + g_flLuxelSize * 0.25 );
			}

			luxel.Position += vPush;
			vOrigin += vPush;

			// A quarter is open when its two edges and its corner are, not through a wall or past an edge, a luxel each way as a pass's jitter is
			const float3 vAcross = luxel.Du;
			const float3 vDown = luxel.Dv;
			const bool2 bAcross = bool2( Open( vOrigin, luxel.Normal, vAcross ), Open( vOrigin, luxel.Normal, -vAcross ) );
			const bool2 bDown = bool2( Open( vOrigin, luxel.Normal, vDown ), Open( vOrigin, luxel.Normal, -vDown ) );

			for ( uint q = 0; q < 4; q++ )
			{
				if ( bAcross[q & 1] && bDown[q >> 1] && Open( vOrigin, luxel.Normal, ( q & 1 ? -vAcross : vAcross ) + ( q >> 1 ? -vDown : vDown ) ) )
					nOpen |= 1u << q;
			}
		}

		Luxels[nLuxel].Position = luxel.Position;
		Luxels[nLuxel].Open = nOpen;

		// A light keeps its channel wherever it reaches, shadowed or not, so neighbouring luxels' strengths can be filtered
		float4 vReach = float4( 0.0, 0.0, 0.0, 0.0 );
		uint nIndices = 0xFFFFFFFF;

		for ( int l = 0; l < g_nLightCount; l++ )
		{
			float3 vToLight;
			float flDistance;
			const float flStrength = Attenuation( l, luxel.Position, vToLight, flDistance, length( luxel.Du ) * 2.0 );
			const uint nChannel = BakeLights[l].Channel;
			if ( flStrength <= vReach[nChannel] )
				continue;

			vReach[nChannel] = flStrength;
			nIndices = ( nIndices & ~( 0xFFu << ( nChannel * 8 ) ) ) | ( (uint)l << ( nChannel * 8 ) );
		}

		OutIndices[nLuxel] = nIndices;
	}
	#endif

	// A probe grid: its first luxel, six a probe, and how many probes it is each way
	int g_nProbeFirst < Attribute( "ProbeFirst" ); >;
	int3 g_vProbeSize < Attribute( "ProbeSize" ); >;

	RWTexture3D<float4> LiveProbes < Attribute( "LiveProbes" ); >;
	RWTexture3D<float4> LiveProbeStrengths < Attribute( "LiveProbeStrengths" ); >;
	RWTexture3D<float4> LiveFog < Attribute( "LiveFog" ); >;

	#if D_STAGE >= 3
	uint ProbeLuxel( int3 vCell )
	{
		return g_nProbeFirst + ( ( vCell.z * g_vProbeSize.y + vCell.y ) * g_vProbeSize.x + vCell.x ) * 6u;
	}

	// A probe inside geometry: over a quarter of its rays went into the backs of surfaces
	bool Buried( uint nFirst )
	{
		float flBacks = 0.0;
		for ( uint a = 0; a < 6; a++ )
			flBacks += OutIrradiance[nFirst + a].a;

		return flBacks > 1.5 * max( g_nPass, 1 );
	}

	// What's traced so far into a probe volume's textures or the fog's. A probe inside geometry traces black, so it takes
	// the light of the probes around it that aren't
	void ResolveProbe( uint nProbe )
	{
		const int3 vCell = int3( nProbe % g_vProbeSize.x, nProbe / g_vProbeSize.x % g_vProbeSize.y, nProbe / ( g_vProbeSize.x * g_vProbeSize.y ) );
		const uint nFirst = ProbeLuxel( vCell );
		const bool bBuried = Buried( nFirst );

		// Which of the probes around it it takes its light from, a bit each: itself alone unless it's buried
		uint nFrom = 0;
		for ( int n = 0; n < 27; n++ )
		{
			const int3 vOther = vCell + int3( n % 3, n / 3 % 3, n / 9 ) - 1;
			if ( ( n == 13 ) != bBuried && all( vOther >= 0 ) && all( vOther < g_vProbeSize ) && !( bBuried && Buried( ProbeLuxel( vOther ) ) ) )
				nFrom |= 1u << n;
		}

		const float flSamples = max( countbits( nFrom ), 1 ) * ( float( g_nPass ) + ( nFirst < (uint)g_nLuxelOffset ? 1.0 : 0.0 ) );
		if ( flSamples == 0.0 )
			return;

		float4 vStrengths = float4( 0.0, 0.0, 0.0, 0.0 );
		float3 vAround = float3( 0.0, 0.0, 0.0 );

		for ( int nAxis = 0; nAxis < 6; nAxis++ )
		{
			float3 vLight = float3( 0.0, 0.0, 0.0 );

			for ( int n = 0; n < 27; n++ )
			{
				if ( ( ( nFrom >> n ) & 1u ) == 0 )
					continue;

				const uint nLuxel = ProbeLuxel( vCell + int3( n % 3, n / 3 % 3, n / 9 ) - 1 ) + nAxis;
				vLight += OutIrradiance[nLuxel].rgb / flSamples;
				vStrengths += OutStrengths[nLuxel] / ( flSamples * 6.0 );
			}

			vAround += vLight / 6.0;

			#if D_STAGE == 3
				LiveProbes[vCell + int3( 0, 0, nAxis * g_vProbeSize.z )] = float4( vLight, 1.0 );
			#endif
		}

		#if D_STAGE == 3
			LiveProbeStrengths[vCell] = sqrt( vStrengths );
		#else
			// Light from every direction, and each baked light's reach times the colour it gives fog
			const uint nIndices = OutIndices[nFirst];
			for ( uint c = 0; c < 4; c++ )
			{
				const uint nLight = ( nIndices >> ( c * 8 ) ) & 0xFFu;
				if ( nLight < (uint)g_nLightCount )
					vAround += BakeLights[nLight].Fog * vStrengths[c];
			}

			LiveFog[vCell] = float4( vAround, 1.0 );
		#endif
	}
	#endif

	// What each thread traced: its ray's glow, the albedo and luxel of what it hit, what the lights give that, and its
	// own lights' strengths. The first's alpha is 1 for a ray that went into the back of a surface
	groupshared float4 g_vTraced[64][4];

	// A ray into the back of a surface started inside something, and its dark would bleed out from under it. The luxel
	// takes what a luxel one or two along from it traced instead: a group's threads follow one another along a surface
	// Only along the row a luxel is in. Dilate the finished lightmap both ways if dark still shows above and below
	void TakeFromBeside( uint nThread, bool bInside, inout float3 vGlow, inout float3 vAlbedo, inout int nSlot, inout float3 vLit, inout float4 vStrengths )
	{
		g_vTraced[nThread][0] = float4( vGlow, bInside ? 1.0 : 0.0 );
		g_vTraced[nThread][1] = float4( vAlbedo, asfloat( nSlot ) );
		g_vTraced[nThread][2] = float4( vLit, 0.0 );
		g_vTraced[nThread][3] = vStrengths;
		GroupMemoryBarrierWithGroupSync();

		for ( int n = 0; n < 4 && bInside; n++ )
		{
			const int nOther = (int)nThread + ( n & 1 ? 1 : -1 ) * ( n / 2 + 1 );
			if ( nOther < 0 || nOther > 63 || g_vTraced[nOther][0].a > 0.0 )
				continue;

			vGlow = g_vTraced[nOther][0].rgb;
			vAlbedo = g_vTraced[nOther][1].rgb;
			nSlot = asint( g_vTraced[nOther][1].a );
			vLit = g_vTraced[nOther][2].rgb;
			vStrengths = g_vTraced[nOther][3];
			bInside = false;
		}
	}

	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 vThreadId : SV_DispatchThreadID, uint nThread : SV_GroupIndex )
	{
		const uint nLuxel = vThreadId.x + g_nLuxelOffset;

		// The luxels on surfaces come first, as many as Froms holds
		#if D_STAGE != 0
			uint nSurfaces, nStride;
			Froms.GetDimensions( nSurfaces, nStride );
			#if D_STAGE == 1
				if ( vThreadId.x < nSurfaces )
					Resolve( vThreadId.x );
			#elif D_STAGE == 2
				if ( nLuxel < (uint)g_nLuxelCount )
					Prepare( nLuxel, nLuxel < nSurfaces );
			#else
				if ( vThreadId.x < (uint)( g_vProbeSize.x * g_vProbeSize.y * g_vProbeSize.z ) )
					ResolveProbe( vThreadId.x );
			#endif
			return;
		#endif

		if ( nLuxel >= (uint)g_nLuxelCount )
			return;

		const Luxel luxel = Luxels[nLuxel];
		float3 vOrigin = luxel.Position + luxel.Normal * RAY_BIAS;
		uint nState = Hash( nLuxel ) ^ Hash( g_nPass * 7919u + 1u );

		// Lit from elsewhere in the luxel each pass, in the quarters of it Prepare found open
		const float2 vJitter = ( Spread( nLuxel, nState ) - 0.5 ) * ( 2 ); // Double jitter size to make hard shadows not fucking ugly
		if ( ( luxel.Open >> ( ( vJitter.x < 0.0 ? 1u : 0u ) | ( vJitter.y < 0.0 ? 2u : 0u ) ) ) & 1u )
			vOrigin += luxel.Du * vJitter.x + luxel.Dv * vJitter.y;

		// The lights Prepare gave its channels
		const uint nIndices = OutIndices[nLuxel];
		const bool bSurface = dot( luxel.Du, luxel.Du ) > 0.0;
		float4 vStrengths = float4( 0.0, 0.0, 0.0, 0.0 );

		for ( uint c = 0; c < 4; c++ )
		{
			const uint nLight = ( nIndices >> ( c * 8 ) ) & 0xFFu;
			if ( nLight >= (uint)g_nLightCount )
				continue;

			float3 vToLight;
			float flDistance;
			const float flStrength = Attenuation( nLight, vOrigin, vToLight, flDistance );

			// A surface with all of a light behind it is in its own shadow, so there's nothing to trace. A probe has no surface
			const float flSize = Lights[nLight].Type == LightType::LightTypeDirectional ? BakeLights[nLight].SunSize * flDistance : max( Lights[nLight].ShapeSize.x, 1.0 );
			if ( flStrength <= 0.0 || ( bSurface && dot( luxel.Normal, vToLight ) * flDistance < -2.0 * flSize ) )
				continue;

			// Across the light opposite to where in the luxel it's lit from, so the two spread out together over passes
			vStrengths[c] = flStrength * Visibility( nLight, vOrigin, vToLight, flDistance, -vJitter );
		}

		// Indirect light: one ray, the sky where it escapes and what the surface it hits gives off
		const float3 vFirst = SpreadDirection( luxel.Normal, Spread( nLuxel, 1u ) );
		float3 vGlow = float3( 0.0, 0.0, 0.0 );
		float3 vAlbedo = float3( 0.0, 0.0, 0.0 );
		float3 vLit = float3( 0.0, 0.0, 0.0 );
		float flOpen = 1.0;
		int nSlot = -1;
		bool bBack = false;

		Bvh::Hit hit;
		if ( !Bvh::Trace( vOrigin, vFirst, 1e30, hit ) )
		{
			vGlow = Sky( vFirst );
		}
		else
		{
			if ( hit.Distance < g_flOcclusionDistance )
				flOpen = hit.Distance / g_flOcclusionDistance;

			// A surface glows from its front
			const BakeTriangle tri = Triangles[hit.Id];
			const Surface surface = Surfaces[tri.Surface];
			bBack = dot( hit.Normal, vFirst ) > 0.0;
			const float3 vNormal = bBack ? -hit.Normal : hit.Normal;
			const float3 vPosition = vOrigin + vFirst * hit.Distance + vNormal * RAY_BIAS;

			vGlow = bBack ? float3( 0.0, 0.0, 0.0 ) : surface.Emission.rgb;
			vAlbedo = surface.Albedo.rgb;
			vLit = HitLight( tri, hit.Barycentrics, vPosition, vNormal, bBack, nState, nSlot );
		}

		TakeFromBeside( nThread, bBack && bSurface, vGlow, vAlbedo, nSlot, vLit, vStrengths );
		float3 vRadiance = vGlow;

		OutStrengths[nLuxel] += vStrengths;
		OutSpread[nLuxel] += vStrengths * vStrengths;

		// And what it gives back: its static lights, and the light that reached its luxel off one bounce fewer. Each
		// count of bounces is a layer, so the one ray adds a bounce to every layer and none go past g_nBounces
		for ( int b = 0; b < g_nBounces; b++ )
		{
			const uint nLayer = (uint)b * (uint)g_nLuxelCount;
			OutBounces[nLayer + nLuxel] += float4( vRadiance, 0.0 );
			vRadiance = vGlow + vAlbedo * ( vLit + ( nSlot >= 0 ? OutBounces[nLayer + nSlot].rgb / float( g_nPass ) : float3( 0.0, 0.0, 0.0 ) ) );
		}

		OutIrradiance[nLuxel] += float4( vRadiance, bSurface ? flOpen : float( bBack ) );
		const float flBrightness = dot( vRadiance, float3( 0.2126, 0.7152, 0.0722 ) );
		OutDirection[nLuxel] += float4( vFirst * flBrightness, flBrightness * flBrightness );
	}
}
