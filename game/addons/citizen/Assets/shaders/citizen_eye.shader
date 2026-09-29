// Procedural citizen eyes. No authored colour, normal, mask or noise textures.
HEADER
{
	Description = "Citizen procedural eye";
	DevShader = true;
	Version = 13;
	CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

FEATURES
{
	Feature( F_SPECULAR_CUBE_MAP, 0..2( 0 = "None", 1 = "In-game Cube Map", 2 = "Artist Cube Map" ), "Specular" );
}

COMMON
{
	#include "system.fxc"
	#define S_SUBSURFACE_SCATTERING SUBSURFACE_SCATTERING_WRAP
	#define S_SPECULAR 1
	#include "vr_common.fxc"
}

struct VS_INPUT
{
	#include "vr_shared_standard_vs_input.fxc"
};

struct PS_INPUT
{
	#include "vr_shared_standard_ps_input.fxc"
	#if ( !S_MODE_DEPTH )
		float3 vEyeForwardWs : TEXCOORD14;
	#endif
};

VS
{
	#include "vr_shared_standard_vs_code.fxc"

	PS_INPUT MainVs( VS_INPUT i )
	{
		PS_INPUT o = VS_SharedStandardProcessing( i );

		#if ( !S_MODE_DEPTH )
			// Carry the eye's bind-pose +X axis through the existing tangent frame.
			// This follows skinning/gaze without bone indices or eye-centre parameters.
			float3 normalOs;
			float4 tangentOs;
			VS_DecodeObjectSpaceNormalAndTangent( i, normalOs, tangentOs );
			float3 bitangentOs = cross( normalOs, tangentOs.xyz ) * tangentOs.w;
			o.vEyeForwardWs = o.vTangentUWs.xyz * tangentOs.x
				+ o.vTangentVWs.xyz * bitangentOs.x + o.vNormalWs.xyz * normalOs.x;
		#endif

		return VS_CommonProcessing_Post( o );
	}
}

PS
{
	#include "vr_common_ps_code.fxc"
	StaticCombo( S_SPECULAR_CUBE_MAP, F_SPECULAR_CUBE_MAP, Sys( ALL ) );
	StaticComboRule( Allow1( S_MODE_DEPTH, S_SPECULAR_CUBE_MAP ) );
	DynamicCombo( D_OPAQUE_FADE, 0..1, Sys( ALL ) );

	// Size controls change the appearance on the existing mesh, not its geometry.
	float g_flIrisRadius < Default( 0.20 ); Range( 0.02, 0.48 ); UiGroup( "Eye Shape,10/10" ); >;
	float2 g_vIrisCenter < Default2( 0.5, 0.5 ); Range2( 0, 0, 1, 1 ); UiGroup( "Eye Shape,10/20" ); >;
	float g_flIrisAspect < Default( 1 ); Range( 0.25, 2 ); UiGroup( "Eye Shape,10/30" ); >;
	float g_flEdgeSoftness < Default( 0.012 ); Range( 0, 0.15 ); UiGroup( "Eye Shape,10/40" ); >;

	float3 g_vIrisColor < UiType( Color ); Default3( 0.28, 0.18, 0.09 ); UiGroup( "Iris,20/10" ); >;
	float3 g_vIrisTint < UiType( Color ); Default3( 1, 1, 1 ); UiGroup( "Iris,20/20" ); >;
	float g_flInnerColorRadius < Default( 0.55 ); Range( 0.05, 1 ); UiGroup( "Iris,20/40" ); >;
	float g_flIrisColorBlend < Default( 0.5 ); Range( 0, 1 ); UiGroup( "Iris,20/50" ); >;
	float g_flFiberStrength < Default( 0.25 ); Range( 0, 1 ); UiGroup( "Iris Detail,25/10" ); >;
	float g_flFiberCount < Default( 100 ); Range( 8, 256 ); UiGroup( "Iris Detail,25/20" ); >;
	float g_flFiberTwist < Default( 0.08 ); Range( -1, 1 ); UiGroup( "Iris Detail,25/30" ); >;
	float g_flRingStrength < Default( 0.08 ); Range( 0, 1 ); UiGroup( "Iris Detail,25/40" ); >;
	float g_flRingCount < Default( 12 ); Range( 1, 40 ); UiGroup( "Iris Detail,25/50" ); >;
	float g_flDetailSeed < Default( 1 ); Range( 0, 100 ); UiGroup( "Iris Detail,25/60" ); >;
	float g_flMicroFiberStrength < Default( 0.45 ); Range( 0, 1 ); UiGroup( "Iris Detail,25/70" ); >;
	float g_flCryptStrength < Default( 0.55 ); Range( 0, 1 ); UiGroup( "Iris Detail,25/80" ); >;
	float g_flCollaretteStrength < Default( 0.45 ); Range( 0, 1 ); UiGroup( "Iris Detail,25/90" ); >;
	float g_flPigmentVariation < Default( 0.3 ); Range( 0, 1 ); UiGroup( "Iris Detail,25/100" ); >;
	float g_flIrisRelief < Default( 0.35 ); Range( 0, 1 ); UiGroup( "Iris Detail,25/110" ); >;

	float g_flLimbalWidth < Default( 0.065 ); Range( 0, 0.4 ); UiGroup( "Limbal Ring,30/10" ); >;
	float g_flLimbalStrength < Default( 0.65 ); Range( 0, 1 ); UiGroup( "Limbal Ring,30/20" ); >;

	float g_flPupilSize < Default( 0.42 ); Range( 0.02, 0.95 ); UiGroup( "Pupil,40/10" ); >;
	float g_flPupilAspect < Default( 1 ); Range( 0.1, 2 ); UiGroup( "Pupil,40/20" ); >;
	float g_flPupilSoftness < Default( 0.015 ); Range( 0, 0.15 ); UiGroup( "Pupil,40/30" ); >;
	float3 g_vPupilColor < UiType( Color ); Default3( 0.005, 0.004, 0.003 ); UiGroup( "Pupil,40/40" ); >;
	float g_flPupilRimStrength < Default( 0.45 ); Range( 0, 1 ); UiGroup( "Pupil,40/50" ); >;

	float3 g_vScleraColor < UiType( Color ); Default3( 0.86, 0.82, 0.77 ); UiGroup( "Sclera,50/10" ); >;
	float3 g_vScleraEdgeColor < UiType( Color ); Default3( 0.55, 0.22, 0.19 ); UiGroup( "Sclera,50/20" ); >;
	float g_flScleraRedness < Default( 0.12 ); Range( 0, 1 ); UiGroup( "Sclera,50/30" ); >;
	float g_flScleraRednessStart < Default( 0.32 ); Range( 0.05, 0.7 ); UiGroup( "Sclera,50/40" ); >;
	float g_flScleraDiffuseWrap < Default( 0.3 ); Range( 0, 1 ); UiGroup( "Sclera,50/50" ); >;
	float g_flVeinStrength < Default( 0.12 ); Range( 0, 1 ); UiGroup( "Sclera,50/60" ); >;

	float g_flCorneaBulge < Default( 0.18 ); Range( 0, 1 ); UiGroup( "Surface,60/10" ); >;
	float g_flCorneaRoughness < Default( 0.08 ); Range( 0.02, 0.6 ); UiGroup( "Surface,60/20" ); >;
	float g_flScleraRoughness < Default( 0.23 ); Range( 0.02, 1 ); UiGroup( "Surface,60/30" ); >;
	float g_flCorneaReflectance < Default( 0.025 ); Range( 0, 0.12 ); UiGroup( "Surface,60/40" ); >;
	// Recess below the corneal rim, measured in iris radii.
	float g_flIrisDepth < Default( 0.04 ); Range( 0, 0.75 ); UiGroup( "Surface,60/50" ); >;
	float g_flIrisConcavity < Default( 0.15 ); Range( 0, 1 ); UiGroup( "Surface,60/60" ); >;
	float g_flCorneaIor < Default( 1.376 ); Range( 1, 1.6 ); UiGroup( "Surface,60/80" ); >;
	float g_flSphericalNormals < Default( 1 ); Range( 0, 1 ); UiGroup( "Surface,60/90" ); >;

	float g_flViewOcclusionStrength < Default( 0.75 ); Range( 0, 8 ); UiGroup( "View Occlusion,70/10" ); >;
	float g_flViewOcclusionStart < Default( 0.1 ); Range( 0, 0.95 ); UiGroup( "View Occlusion,70/20" ); >;
	float g_flViewOcclusionPower < Default( 1.5 ); Range( 0.01, 32 ); UiGroup( "View Occlusion,70/30" ); >;
	float g_flViewOcclusionHardness < Default( 0 ); Range( 0, 1 ); UiGroup( "View Occlusion,70/35" ); >;
	float3 g_vOcclusionTint < UiType( Color ); Default3( 0.18, 0.10, 0.08 ); UiGroup( "View Occlusion,70/40" ); >;
	float g_flDirectOcclusion < Default( 0.3 ); Range( 0, 1 ); UiGroup( "View Occlusion,70/50" ); >;
	float g_flReflectionOcclusion < Default( 0.6 ); Range( 0, 1 ); UiGroup( "View Occlusion,70/60" ); >;

	// Per-avatar controls compose with the authored material. Neutral defaults keep
	// material previews unchanged; colour alpha enables an explicit sRGB override.
	float4 g_vAvatarEyeColor < Attribute( "eye_color" ); Default4( 0, 0, 0, 0 ); >;
	float g_flAvatarEyeSize < Attribute( "eye_size" ); Default( 1 ); >;
	float2 g_vAvatarEyeAlign < Attribute( "eye_align" ); Default2( 0, 0 ); >;
	float g_flAvatarEyePupilSize < Attribute( "eye_pupil_size" ); Default( 1 ); >;

	#if ( D_OPAQUE_FADE ) && ( !S_MODE_DEPTH )
		RenderState( AlphaToCoverageEnable, true );
	#endif

	float EyeIrisRadius()
	{
		return clamp( g_flIrisRadius * g_flAvatarEyeSize, 0.02, 0.48 );
	}

	float EyePupilSize()
	{
		return clamp( g_flPupilSize * g_flAvatarEyePupilSize, 0.02, 0.95 );
	}

	float EyeDisc( float distance, float softness )
	{
		float width = max( fwidth( distance ), max( softness, 0.0001 ) );
		return 1 - smoothstep( -width, width, distance );
	}

	float EyeHash( float value )
	{
		return frac( sin( value * 127.1 + g_flDetailSeed * 311.7 ) * 43758.5453 );
	}

	// Periodic noise avoids an iris seam at atan2's wrap from pi to -pi.
	float EyeFiberNoise( float angle, float count )
	{
		count = max( floor( count ), 1 );
		float coordinate = frac( angle ) * count;
		float cell = floor( coordinate );
		float blend = frac( coordinate );
		blend = blend * blend * ( 3 - 2 * blend );
		return lerp( EyeHash( cell ), EyeHash( fmod( cell + 1, count ) ), blend );
	}

	float EyeHash2( float2 p )
	{
		float3 q = frac( float3( p.x, p.y, p.x ) * 0.1031 + g_flDetailSeed * 0.017 );
		q += dot( q, q.yzx + 33.33 );
		return frac( (q.x + q.y) * q.z );
	}

	// Wrap lattice indices, not the noise value: both sides of the polar seam agree.
	float EyePolarNoise( float2 p, float period )
	{
		float2 cell = floor( p );
		float2 blend = frac( p );
		blend = blend * blend * (3 - 2 * blend);
		float x0 = cell.x - period * floor( cell.x / period );
		float x1 = x0 + 1 - period * floor( (x0 + 1) / period );
		return lerp(
			lerp( EyeHash2( float2( x0, cell.y ) ), EyeHash2( float2( x1, cell.y ) ), blend.x ),
			lerp( EyeHash2( float2( x0, cell.y + 1 ) ), EyeHash2( float2( x1, cell.y + 1 ) ), blend.x ), blend.y );
	}

	float EyeDetailVisibility( float frequency, float footprint )
	{
		return 1 - smoothstep( 0.25, 0.9, frequency * footprint );
	}

	// Thin individual strands with independent width, waviness and radial length.
	// A second strand diverges from each trunk instead of tracing noise contours.
	float EyeFilaments( float angle, float radial, float count, float angularFootprint )
	{
		float coordinate = angle * count;
		float cell = floor( coordinate );
		float result = 0;
		float aa = max( angularFootprint * count, 0.008 );
		[unroll]
		for ( int neighbour = -1; neighbour <= 1; neighbour++ )
		{
			float index = cell + neighbour;
			float wrappedIndex = index - count * floor( index / count );
			float seed = EyeHash2( float2( wrappedIndex, 127 ) );
			float seed2 = EyeHash2( float2( wrappedIndex, 231 ) );
			float bend = sin( radial * (5 + seed * 7) + seed2 * M_2PI ) * 0.18;
			float distance = coordinate - index - (0.25 + seed * 0.5) + bend;
			float width = lerp( 0.025, 0.075, seed );
			float trunk = 1 - smoothstep( width, width + aa, abs( distance ) );
			float fork = distance + smoothstep( 0.15 + seed * 0.3, 0.85, radial ) * (seed2 - 0.5) * 0.8;
			float branch = (1 - smoothstep( width * 0.6, width * 0.6 + aa, abs( fork ) )) * 0.5;
			float envelope = smoothstep( seed * 0.25, seed * 0.25 + 0.12, radial )
				* (1 - smoothstep( 0.75 + seed2 * 0.2, 1.05, radial ));
			result += max( trunk, branch ) * envelope * (0.4 + seed2 * 0.6);
		}
		return result;
	}

	struct IrisTissue
	{
		float fibers;
		float microFibers;
		float crypts;
		float collarette;
		float furrows;
		float pigment;
		float pupilRim;
		float height;
	};

	float IrisPupilBoundary( float radius, float pupilRadius )
	{
		return EyePupilSize() * radius / max( pupilRadius, 0.0001 );
	}

	IrisTissue EvaluateIrisTissue( float2 point, float pupilRadius, float footprint )
	{
		IrisTissue tissue = (IrisTissue)0;
		float radius = length( point );
		float angle = atan2( point.y, point.x + 0.000001 ) / M_2PI;
		// Measure tissue from the elliptical pupil boundary to the circular limbus.
		// Using ellipse distance for the whole iris would flatten detail beside slit pupils.
		float pupilBoundary = IrisPupilBoundary( radius, pupilRadius );
		float span = max( 1 - pupilBoundary, 0.05 );
		float radial = saturate( (radius - pupilBoundary) / span );
		float angularFootprint = footprint / (M_2PI * max( radius, 0.05 ));
		float radialFootprint = max( footprint / span, fwidth( radial ) );
		float count = max( floor( g_flFiberCount ), 8 );

		// Large bundles wander and split into finer strands as they cross the iris.
		float wander = EyePolarNoise( float2( angle * 24, radial * 3 ), 24 ) - 0.5;
		float warped = angle + g_flFiberTwist * radial * 0.15 + wander * 0.018 * sin( radial * M_PI );
		float2 direction = float2( cos( warped * M_2PI ), sin( warped * M_2PI ) );
		angularFootprint = max( angularFootprint, max( length( ddx( direction ) ), length( ddy( direction ) ) ) / M_2PI );
		float bundle = EyePolarNoise( float2( warped * count, radial * 5 ), count );
		float strands = EyePolarNoise( float2( warped * count * 3 + bundle * 1.5, radial * 9 + 19 ), count * 3 );
		float fine = EyePolarNoise( float2( warped * count * 9 + strands, radial * 16 + 43 ), count * 9 );
		float bundleVisibility = EyeDetailVisibility( count, angularFootprint ) * EyeDetailVisibility( 5, radialFootprint );
		float strandVisibility = EyeDetailVisibility( count * 3, angularFootprint ) * EyeDetailVisibility( 9, radialFootprint );
		float fineVisibility = EyeDetailVisibility( count * 9, angularFootprint ) * EyeDetailVisibility( 16, radialFootprint );
		tissue.fibers = (bundle - 0.5) * 2 * bundleVisibility;
		float threads = EyeFilaments( warped, radial, count * 3, angularFootprint );
		tissue.microFibers = ((strands - 0.5) * 0.8 + (threads - 0.12) * 1.3) * strandVisibility
			+ (fine - 0.5) * 0.9 * fineVisibility;

		float collar = 0.27 + (EyeFiberNoise( angle + 0.31, 32 ) - 0.5) * 0.15;
		float collarDistance = abs( radial - collar );
		tissue.collarette = exp2( -collarDistance * collarDistance * 1800 ) * EyeDetailVisibility( 35, radialFootprint );

		// Irregular, elongated hollows around the collarette, with fine raised edges.
		// Two neighbours prevent cells popping as their centres cross a sector boundary.
		float cryptDistance = 10;
		float sector = floor( angle * 38 );
		[unroll]
		for ( int neighbour = 0; neighbour < 2; neighbour++ )
		{
			float index = sector + neighbour;
			float wrappedIndex = index - 38 * floor( index / 38 );
			float seed = EyeHash2( float2( wrappedIndex, 81 ) );
			float2 delta = float2( (angle * 38 - index) / lerp( 0.16, 0.38, seed ),
				(radial - collar - (seed - 0.5) * 0.18) / lerp( 0.055, 0.16, seed ) );
			cryptDistance = min( cryptDistance, length( delta ) );
		}
		float cryptVisibility = EyeDetailVisibility( 100, angularFootprint ) * EyeDetailVisibility( 25, radialFootprint );
		tissue.crypts = (1 - smoothstep( 0.25, 1, cryptDistance )) * cryptVisibility;
		float cryptRimDistance = cryptDistance - 1;
		float cryptRim = exp2( -cryptRimDistance * cryptRimDistance * 30 ) * cryptVisibility;
		tissue.collarette += cryptRim * 0.35;

		// Outer contraction furrows break up around the circumference instead of forming a bullseye.
		float ringPhase = radial * g_flRingCount + EyeFiberNoise( angle + 0.17, 27 ) * 0.65;
		float ringWave = 0.5 + 0.5 * cos( ringPhase * M_2PI );
		tissue.furrows = pow( ringWave, 8 ) * smoothstep( 0.45, 0.85, radial )
			* EyeDetailVisibility( g_flRingCount * 4, radialFootprint );
		tissue.pigment = (EyePolarNoise( float2( angle * 17, radial * 4 + 91 ), 17 ) - 0.5)
			* EyeDetailVisibility( 17, angularFootprint ) * EyeDetailVisibility( 4, radialFootprint );
		float rimWidth = 0.012 + 0.012 * EyeFiberNoise( angle, 71 );
		tissue.pupilRim = (1 - smoothstep( rimWidth, rimWidth + max( footprint, 0.003 ), pupilRadius - EyePupilSize() ))
			* EyeDetailVisibility( 71, angularFootprint );
		tissue.height = tissue.fibers * g_flFiberStrength * 0.4 + tissue.microFibers * g_flMicroFiberStrength * 0.2
			+ tissue.collarette * g_flCollaretteStrength * 0.4 - tissue.crypts * g_flCryptStrength * 0.5
			- tissue.furrows * g_flRingStrength * 0.2;
		return tissue;
	}

	// Recover the tissue's slope from screen derivatives; no extra noise evaluations.
	float2 IrisReliefSlope( float height, float2 point )
	{
		float2 dx = ddx( point );
		float2 dy = ddy( point );
		float determinant = dx.x * dy.y - dx.y * dy.x;
		float2 gradient = float2( dy.y * ddx( height ) - dx.y * ddy( height ),
			dx.x * ddy( height ) - dy.x * ddx( height ) ) * sign( determinant ) / max( abs( determinant ), 0.00000001 );
		return gradient * min( 0.012 * g_flIrisRelief, 0.5 / max( length( gradient ), 0.001 ) );
	}

	float EyeVeins( float2 uv )
	{
		float radius = length( uv );
		float angle = atan2( uv.y, uv.x + 0.000001 ) / M_2PI;
		float wander = EyePolarNoise( float2( angle * 22, radius * 24 ), 22 );
		float phase = angle * 22 + (wander - 0.5) * 0.9;
		float trunk = abs( frac( phase + 0.5 ) - 0.5 );
		float branch = abs( frac( phase + radius * 5 + 0.5 ) - 0.5 );
		float width = lerp( 0.008, 0.025, saturate( radius * 2 ) );
		float footprint = max( length( ddx( uv ) ), length( ddy( uv ) ) );
		float aa = max( footprint * 22 / (M_2PI * max( radius, 0.05 )), 0.001 );
		float veins = 1 - smoothstep( width, width + aa, trunk );
		veins = max( veins, (1 - smoothstep( width * 0.5, width * 0.5 + aa, branch )) * 0.4 );
		return veins * smoothstep( EyeIrisRadius() * 1.08, EyeIrisRadius() * 1.9, radius )
			* EyeDetailVisibility( 90, footprint ) * (0.35 + 0.65 * wander);
	}

	void EyeTangentFrame( float3 interpolatedNormal, float3 eyeForward, float3 meshTangentU, float3 meshTangentV,
		out float3 normal, out float3 tangentU, out float3 tangentV )
	{
		// Citizen's vertex normals are radial. Keep their linearly interpolated
		// lateral components and reconstruct sphere depth instead of normalizing
		// the flattened chord across each triangle. The mesh silhouette is unchanged.
		float3 meshNormal = normalize( interpolatedNormal );
		eyeForward = normalize( eyeForward );
		float axial = dot( interpolatedNormal, eyeForward );
		float3 lateral = interpolatedNormal - eyeForward * axial;
		float sphereDepth = sqrt( saturate( 1 - dot( lateral, lateral ) ) );
		float3 sphereNormal = lateral + eyeForward * sphereDepth * (axial < 0 ? -1 : 1);
		normal = normalize( lerp( meshNormal, sphereNormal, g_flSphericalNormals ) );
		tangentU = normalize( meshTangentU - normal * dot( normal, meshTangentU ) );
		float handedness = dot( cross( meshNormal, meshTangentU ), meshTangentV ) < 0 ? -1 : 1;
		tangentV = cross( normal, tangentU ) * handedness;
	}

	float3x3 EyeProjectionRotation()
	{
		const float uvRadius = 0.49101;
		float2 centre = (g_vIrisCenter + g_vAvatarEyeAlign - 0.5) / uvRadius;
		centre *= min( 1, 0.999 / max( length( centre ), 0.0001 ) );
		float forward = sqrt( saturate( 1 - dot( centre, centre ) ) );

		// Rotate both the surface position and viewing ray into the same iris frame.
		float2 tilt = centre / (1 + forward);
		return float3x3(
			float3( 1 - centre.x * tilt.x, -centre.x * tilt.y, -centre.x ),
			float3( -centre.y * tilt.x, 1 - centre.y * tilt.y, -centre.y ),
			float3( centre, forward ) );
	}

	float3 EyeProjection( float2 texcoords, float hemisphere, float3x3 rotation )
	{
		// Citizen UVs orthographically project a sphere. Keep all three coordinates
		// in UV units so ray intersections account for the height of the cornea.
		const float uvRadius = 0.49101;
		float2 disk = (texcoords - 0.5) / uvRadius;
		float3 sphere = float3( disk, sqrt( saturate( 1 - dot( disk, disk ) ) ) * hemisphere );
		return mul( rotation, sphere ) * uvRadius;
	}

	float3x3 EyeProjectionFrame( float3 eyeForward, float3 tangentU, float3 tangentV, float3x3 rotation )
	{
		// Remove the sphere's local tilt from the mesh tangent to recover the
		// fixed projection axes. UV Y runs opposite to the normal-map tangent Y.
		float3 forward = normalize( eyeForward );
		float3 right = tangentU - forward * dot( tangentU, forward );
		right /= max( length( right ), 0.0001 );
		float3 down = cross( forward, right );
		down *= dot( down, tangentV ) > 0 ? -1 : 1;
		return mul( rotation, float3x3( right, down, forward ) );
	}

	float2 IrisParallaxPoint( float3 surface, float3 ray, float irisRadius, float2 shapeScale )
	{
		const float uvRadius = 0.49101;
		float rimHeight = sqrt( max( uvRadius * uvRadius - irisRadius * irisRadius, 0 ) );
		float2 point = surface.xy * shapeScale;
		float2 slope = ray.xy * irisRadius * shapeScale / max( -ray.z, 0.05 );
		float curvature = g_flIrisDepth * g_flIrisConcavity;

		// Intersect the refracted ray with z = -depth + curvature * radius^2.
		// The iris is below the corneal rim, not wrapped around the eyeball surface.
		float height = max( (surface.z - rimHeight) / irisRadius + g_flIrisDepth
			- curvature * dot( point, point ), 0 );
		float a = curvature * dot( slope, slope );
		float b = 1 + 2 * curvature * dot( point, slope );
		float root = sqrt( b * b + 4 * a * height );

		// Use the stable quadratic root, including flat irises and head-on views.
		float distance = b >= 0 ? 2 * height / max( b + root, 0.0001 )
			: (root - b) / max( 2 * a, 0.0001 );
		return point + slope * distance;
	}

	float3 IrisColor( float radius, float pupilRadius, float pupilMask, IrisTissue tissue )
	{
		// Follow elliptical pupils and retain a soft transition when dilation passes
		// Inner Color Radius; otherwise the gradient collapses into a hard ring.
		float pupilBoundary = IrisPupilBoundary( radius, pupilRadius );
		float blendWidth = max( g_flInnerColorRadius - pupilBoundary, max( 1 - pupilBoundary, 0.01 ) * 0.15 );
		float innerBlend = 1 - smoothstep( 0, blendWidth, radius - pupilBoundary );

		// Derive every iris shade from one colour. Limit the inner brightness
		// uniformly so bright colours keep their hue rather than clipping channels.
		float3 baseIrisColor = SrgbGammaToLinear( lerp( g_vIrisColor, g_vAvatarEyeColor.rgb, saturate( g_vAvatarEyeColor.a ) ) );
		float brightestChannel = max( baseIrisColor.r, max( baseIrisColor.g, baseIrisColor.b ) );
		float3 innerColor = baseIrisColor * min( 2, 1 / max( brightestChannel, 0.0001 ) );
		float3 outerColor = baseIrisColor * 0.25;
		float3 irisColor = lerp( baseIrisColor, innerColor, innerBlend * g_flIrisColorBlend );
		irisColor = lerp( irisColor, outerColor, smoothstep( 0.65, 1, radius ) * g_flIrisColorBlend );

		// Broad pigment patches gently vary the iris colour's warmth.
		irisColor *= exp2( tissue.pigment * g_flPigmentVariation * float3( 1.1, 0.45, -0.35 ) );
		irisColor *= max( 0.08, 1 + tissue.fibers * g_flFiberStrength * 1.4
			+ tissue.microFibers * g_flMicroFiberStrength + tissue.collarette * g_flCollaretteStrength * 0.7 );
		irisColor *= 1 - tissue.crypts * g_flCryptStrength * 0.85;
		irisColor *= 1 - tissue.furrows * g_flRingStrength * 0.7;
		irisColor *= 1 - tissue.pupilRim * g_flPupilRimStrength * 0.85;

		float limbalMask = ( 1 - EyeDisc( radius - ( 1 - g_flLimbalWidth ), g_flEdgeSoftness ) ) * saturate( g_flLimbalWidth / 0.005 );
		irisColor = lerp( irisColor, baseIrisColor * 0.06, limbalMask * g_flLimbalStrength );
		irisColor *= SrgbGammaToLinear( g_vIrisTint );

		irisColor = lerp( irisColor, SrgbGammaToLinear( g_vPupilColor ), pupilMask );
		return irisColor;
	}

	float3 ScleraColor( float2 uv )
	{
		float redness = smoothstep( g_flScleraRednessStart, g_flScleraRednessStart + 0.25, length( uv ) ) * g_flScleraRedness;
		float3 scleraColor = lerp( SrgbGammaToLinear( g_vScleraColor ), SrgbGammaToLinear( g_vScleraEdgeColor ), redness );
		scleraColor = lerp( scleraColor, SrgbGammaToLinear( g_vScleraEdgeColor ) * 0.65, EyeVeins( uv ) * g_flVeinStrength );
		return scleraColor;
	}

	struct EyeVisibility
	{
		float3 ambient;
		float3 direct;
		float reflection;
	};

	EyeVisibility EvaluateEyeVisibility( float3 normal, float3 directionToCamera )
	{
		// Use the smooth eyeball surface, keeping iris relief out of the rim.
		float rim = 1 - saturate( dot( normal, directionToCamera ) );
		float edge = saturate( (rim - g_flViewOcclusionStart) / max( 1 - g_flViewOcclusionStart, 0.001 ) );
		float softAmount = pow( edge, max( g_flViewOcclusionPower, 0.01 ) ) * g_flViewOcclusionStrength;
		float aa = max( fwidth( rim ), 0.0001 );
		float hardCoverage = smoothstep( g_flViewOcclusionStart - aa * 0.5, g_flViewOcclusionStart + aa * 0.5, rim );
		float3 tintAbsorption = 1 - SrgbGammaToLinear( g_vOcclusionTint );

		// Clamp the fully shaded endpoint before applying hard-edge coverage.
		// This preserves antialiasing even when strength is overdriven to solid black.
		float3 softAmbient = saturate( 1 - softAmount * tintAbsorption );
		float3 hardAmbient = lerp( float3( 1, 1, 1 ), saturate( 1 - g_flViewOcclusionStrength * tintAbsorption ), hardCoverage );
		float softReflection = saturate( 1 - softAmount * g_flReflectionOcclusion );
		float hardReflection = lerp( 1, saturate( 1 - g_flViewOcclusionStrength * g_flReflectionOcclusion ), hardCoverage );

		EyeVisibility visibility;
		visibility.ambient = lerp( softAmbient, hardAmbient, g_flViewOcclusionHardness );
		visibility.direct = lerp( float3( 1, 1, 1 ), visibility.ambient, g_flDirectOcclusion );
		visibility.reflection = lerp( softReflection, hardReflection, g_flViewOcclusionHardness );
		return visibility;
	}

	#if ( S_MODE_DEPTH && !D_OPAQUE_FADE )
		#define MainPs Disabled
	#endif

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		PS_OUTPUT o = ( PS_OUTPUT )0;
		#if ( S_MODE_DEPTH )
		{
			#if ( D_OPAQUE_FADE )
				OpaqueFadeDepth( i.vVertexColor.a, i.vPositionSs.xy );
			#endif
			return o;
		}
		#else
		{
			float3 position = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
			float3 normal, tangentU, tangentV;
			EyeTangentFrame( i.vNormalWs.xyz, i.vEyeForwardWs, i.vTangentUWs.xyz, i.vTangentVWs.xyz, normal, tangentU, tangentV );
			float3 cameraToPosition = CalculateCameraToPositionDirWs( position );
			float hemisphere = dot( i.vNormalWs.xyz, i.vEyeForwardWs ) < 0 ? -1 : 1;
			float3x3 projectionRotation = EyeProjectionRotation();
			float3x3 projectionFrame = EyeProjectionFrame( i.vEyeForwardWs, i.vTangentUWs, i.vTangentVWs, projectionRotation );
			float3 projection = EyeProjection( i.vTextureCoords.xy, hemisphere, projectionRotation );
			float2 uv = projection.xy;
			float irisRadius = max( EyeIrisRadius(), 0.001 );
			float2 shapeScale = float2( 1 / max( g_flIrisAspect, 0.01 ), 1 ) / irisRadius;
			float2 surfacePoint = uv * shapeScale;
			float surfaceRadius = length( surfacePoint );
			float aperture = EyeDisc( surfaceRadius - 1, g_flEdgeSoftness );
			// An orthographic projection also has a matching circle on the back of the eye.
			float projectionAA = max( fwidth( projection.z ), 0.0001 );
			aperture *= smoothstep( -projectionAA, projectionAA, projection.z );

			// Refract through the cornea, then intersect the iris in its fixed frame.
			float2 corneaSlope = surfacePoint * float2( 1, -1 ) * g_flCorneaBulge * aperture;
			float3 corneaNormalTs = normalize( float3( corneaSlope, 1 ) );
			float3 corneaNormal = Vec3TsToWsNormalized( corneaNormalTs, normal, tangentU, tangentV );
			float3 refracted = refract( cameraToPosition, corneaNormal, 1 / max( g_flCorneaIor, 1 ) );
			float2 irisPoint = IrisParallaxPoint( projection, mul( projectionFrame, refracted ), irisRadius, shapeScale );
			float radius = length( irisPoint );
			// Keep the opening fixed as the recessed pattern moves beneath it.
			// Sampling past the tissue edge uses the dark limbal colour, not sclera.
			float irisMask = aperture;
			float pupilRadius = length( irisPoint * float2( 1 / max( g_flPupilAspect, 0.01 ), 1 ) );
			float pupilMask = EyeDisc( pupilRadius - EyePupilSize(), g_flPupilSoftness );

			float footprint = max( length( ddx( irisPoint ) ), length( ddy( irisPoint ) ) );
			IrisTissue tissue = EvaluateIrisTissue( irisPoint, pupilRadius, footprint );
			float3 irisColor = IrisColor( radius, pupilRadius, pupilMask, tissue );
			float3 scleraColor = ScleraColor( uv );
			float3 albedo = lerp( scleraColor, irisColor, irisMask ) * i.vVertexColor.rgb;

			// Shade the same bowl that the viewing ray intersects.
			float2 irisSlope = -2 * g_flIrisDepth * g_flIrisConcavity * irisPoint;
			irisSlope += IrisReliefSlope( tissue.height, irisPoint ) * irisMask * (1 - pupilMask);
			irisSlope *= irisRadius * shapeScale;
			float3 irisNormal = normalize( mul( float3( irisSlope, 1 ), projectionFrame ) );
			float roughness = lerp( g_flScleraRoughness, g_flCorneaRoughness, aperture );
			float diffuseWrap = g_flScleraDiffuseWrap * (1 - irisMask);

			EyeVisibility visibility = EvaluateEyeVisibility( normal, -cameraToPosition );

			// The shared wrap model supports separate diffuse and specular normals.
			// Evaluate lighting once: iris relief shades the tissue, while the cornea
			// stays smooth for reflections. DDGI/cubemap diffuse uses the cornea normal.
			FinalCombinerInput_t f = PS_InitFinalCombiner();
			f.vPositionWs = position;
			f.vPositionWithOffsetWs = i.vPositionWithOffsetWs.xyz;
			f.vPositionSs = i.vPositionSs;
			f.vNormalWs = corneaNormal;
			f.vNormalTs = corneaNormalTs;
			f.vSSSNormalWs = normalize( lerp( normal, irisNormal, irisMask ) );
			f.vTangentUWs = tangentU;
			f.vTangentVWs = tangentV;
			f.vRoughness = AdjustRoughnessByGeometricNormal( roughness.xx, normal );
			f.vAlbedo = albedo;
			f.vDiffuseColor = albedo;
			f.vTextureCoords = i.vTextureCoords.xy;
			f.vSpecularColor = g_flCorneaReflectance.xxx;
			f.vSSSWrapParameters = float4( diffuseWrap, 1, 1 / (1 + diffuseWrap), 1 / (1 + diffuseWrap) );

			// Keep lighting calls outside per-pixel branches: shadow receivers use derivatives.
			LightingTerms_t lighting = InitLightingTerms();
			ComputeDirectLighting( lighting, f );
			CalculateIndirectLighting( lighting, f );

			// Retain independent coloured rim occlusion for diffuse and reflections.
			float3 diffuseAO = CalculateDiffuseAmbientOcclusion( f, lighting );
			float3 specularAO = CalculateSpecularAmbientOcclusion( f, lighting );
			o.vColor = float4( albedo * (lighting.vDiffuse * visibility.direct
				+ lighting.vIndirectDiffuse * visibility.ambient * diffuseAO)
				+ (lighting.vSpecular + lighting.vIndirectSpecular * specularAO) * visibility.reflection, 1 );

			#if ( D_OPAQUE_FADE )
				o.vColor.a = OpaqueFade( i.vVertexColor.a, i.vPositionSs.xyzw );
			#endif

			f.flOpacity = o.vColor.a;
			return PS_FinalCombinerDoPostProcessing( f, lighting, o );
		}
		#endif
	}
}
