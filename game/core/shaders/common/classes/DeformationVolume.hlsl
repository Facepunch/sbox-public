#ifndef COMMON_CLASSES_DEFORMATION_VOLUME_HLSL
#define COMMON_CLASSES_DEFORMATION_VOLUME_HLSL

// Matches SceneVolume.VolumeTypes and ModelDeformer.OperationType on the managed side.
static const uint DEFORMATION_SHAPE_SPHERE = 0;
static const uint DEFORMATION_SHAPE_BOX = 1;
static const uint DEFORMATION_SHAPE_CAPSULE = 2;
static const uint DEFORMATION_SHAPE_INFINITE = 1000;

static const uint DEFORMATION_OP_INFLATE = 0;
static const uint DEFORMATION_OP_SQUASH_STRETCH = 1;
static const uint DEFORMATION_OP_TRANSFORM = 2;
static const uint DEFORMATION_OP_SUCK = 3;
static const uint DEFORMATION_OP_PROJECT = 4;

static const uint DEFORMATION_NORMAL_ACCURATE = 0;
static const uint DEFORMATION_NORMAL_SMOOTH = 1;
static const uint DEFORMATION_NORMAL_PRESERVE = 2;

// GPU layout matches SceneDeformationVolumeData_t (208 bytes).
struct DeformationVolume
{
	float4 modelToVolume[3];
	float4 volumeToModel[3];
	float4 sizeRadius;
	float4 amountFalloff;
	float4 weightOperation;
	float4 centerShape;
	float4 operationTransform[3];

	// Negative inside. The derivative is analytic, for each shape.
	float Distance( float3 p, out float3 gradient )
	{
		float3 size = sizeRadius.xyz;
		float radius = sizeRadius.w;
		uint shape = uint( centerShape.w );

		if ( shape == DEFORMATION_SHAPE_INFINITE )
		{
			gradient = 0;
			return -1e20;
		}
		if ( shape == DEFORMATION_SHAPE_SPHERE || shape == DEFORMATION_SHAPE_CAPSULE )
		{
			float3 q = p;
			if ( shape == DEFORMATION_SHAPE_CAPSULE )
			{
				q -= size * clamp( dot( p, size ) / max( dot( size, size ), 0.00000001 ), -1, 1 );
			}
			float distance = length( q );
			gradient = q / max( distance, 0.000001 );
			return distance - radius;
		}

		float3 q = abs( p ) - size;
		float3 outside = max( q, 0 );
		float distance = length( outside );
		if ( distance > 0 )
		{
			gradient = sign( p ) * outside / distance;
		}
		else
		{
			// The SDF has a crease on equidistant interior planes; choose one subgradient.
			gradient = q.x >= q.y && q.x >= q.z ? float3( sign( p.x ), 0, 0 ) :
				(q.y >= q.z ? float3( 0, sign( p.y ), 0 ) : float3( 0, 0, sign( p.z ) ));
		}

		return distance + min( max( q.x, max( q.y, q.z ) ), 0 );
	}

	// Project to the surface, radially for boxes/spheres and from the nearest axis point for capsules.
	// Returning the Jacobian keeps normals aligned even when the result has collapsed onto a face.
	float3 ProjectToSurface( float3 p, out float3x3 jacobian )
	{
		float3x3 identity = float3x3( 1, 0, 0, 0, 1, 0, 0, 0, 1 );
		float3 size = sizeRadius.xyz;
		uint shape = uint( centerShape.w );
		if ( shape == DEFORMATION_SHAPE_BOX )
		{
			// Zero-sized boxes have no interior and are rejected before reaching this path.
			float3 relative = abs( p ) / size;
			float extent = max( relative.x, max( relative.y, relative.z ) );
			if ( extent < 0.000001 )
			{
				// The center has no radial direction. Pick a deterministic point on the surface.
				jacobian = 0;
				return float3( size.x, 0, 0 );
			}

			float3 gradient = relative.x >= relative.y && relative.x >= relative.z ? float3( sign( p.x ) / size.x, 0, 0 ) :
				(relative.y >= relative.z ? float3( 0, sign( p.y ) / size.y, 0 ) : float3( 0, 0, sign( p.z ) / size.z ));
			jacobian = identity / extent - float3x3( p.x * gradient, p.y * gradient, p.z * gradient ) / (extent * extent);
			return p / extent;
		}

		float3 axisPoint = 0;
		float3x3 axisDerivative = 0;
		float lengthSquared = dot( size, size );
		if ( shape == DEFORMATION_SHAPE_CAPSULE && lengthSquared > 0.00000001 )
		{
			float projection = dot( p, size ) / lengthSquared;
			axisPoint = size * clamp( projection, -1, 1 );
			if ( abs( projection ) < 1 )
			{
				axisDerivative = float3x3( size.x * size, size.y * size, size.z * size ) / lengthSquared;
			}
		}

		float3 radial = p - axisPoint;
		float radialLength = length( radial );
		if ( radialLength < 0.000001 )
		{
			float3 direction = float3( 1, 0, 0 );
			if ( shape == DEFORMATION_SHAPE_CAPSULE && lengthSquared > 0.00000001 )
			{
				float3 axis = size * rsqrt( lengthSquared );
				direction = normalize( cross( axis, abs( axis.z ) < 0.9 ? float3( 0, 0, 1 ) : float3( 0, 1, 0 ) ) );
			}

			jacobian = axisDerivative;
			return axisPoint + direction * sizeRadius.w;
		}

		float3 direction = radial / radialLength;
		float3x3 radialDerivative = identity - float3x3( direction.x * direction, direction.y * direction, direction.z * direction );
		jacobian = axisDerivative + (sizeRadius.w / radialLength) * mul( radialDerivative, identity - axisDerivative );
		return axisPoint + direction * sizeRadius.w;
	}

	// Every finite operation must keep affected vertices inside its own shape. Outside vertices
	// are excluded before evaluation; only an escaping result is clipped, along with its derivative.
	void ConstrainToShape( float3 p, inout float3 delta, inout float3x3 derivative )
	{
		uint shape = uint( centerShape.w );
		if ( shape == DEFORMATION_SHAPE_INFINITE )
		{
			return;
		}

		float3 candidate = p + delta;
		float3 gradient;
		if ( Distance( candidate, gradient ) <= 0 )
		{
			return;
		}

		float3x3 jacobian;
		float3 constrained;
		if ( shape == DEFORMATION_SHAPE_BOX )
		{
			constrained = clamp( candidate, -sizeRadius.xyz, sizeRadius.xyz );
			float3 freeAxis = float3( abs( candidate.x ) < sizeRadius.x, abs( candidate.y ) < sizeRadius.y, abs( candidate.z ) < sizeRadius.z );
			jacobian = float3x3( freeAxis.x, 0, 0, 0, freeAxis.y, 0, 0, 0, freeAxis.z );
		}
		else
		{
			constrained = ProjectToSurface( candidate, jacobian );
		}

		float3x3 identity = float3x3( 1, 0, 0, 0, 1, 0, 0, 0, 1 );
		derivative = mul( jacobian, identity + derivative ) - identity;
		delta = constrained - p;
	}

	// Shared displacement evaluation. Unused analytic derivatives are eliminated for normal samples.
	void Evaluate( float3 localPosition,
		out float3 delta, out float3x3 derivative, out float distance )
	{
		delta = 0;
		derivative = 0;
		float3 p = localPosition - centerShape.xyz;
		float3 size = sizeRadius.xyz;
		uint shape = uint( centerShape.w );
		uint operation = uint( weightOperation.y );
		float radius = sizeRadius.w;
		float3 gradient;
		distance = Distance( p, gradient );
		if ( distance >= 0 || (operation == DEFORMATION_OP_PROJECT && shape == DEFORMATION_SHAPE_INFINITE) )
		{
			return;
		}

		// A full-strength core with a smooth boundary band. The width follows the shape's size.
		float falloffRadius = shape == DEFORMATION_SHAPE_INFINITE ? 0 : shape == DEFORMATION_SHAPE_SPHERE || shape == DEFORMATION_SHAPE_CAPSULE ? radius : min( size.x, min( size.y, size.z ) );
		float fadeWidth = falloffRadius * amountFalloff.w;
		float3 influenceGradient = 0;
		float weight = weightOperation.x;
		float influence = weight;
		if ( operation != DEFORMATION_OP_PROJECT && shape != DEFORMATION_SHAPE_INFINITE && fadeWidth > 0 )
		{
			float t = saturate( -distance / max( fadeWidth, 0.000001 ) );
			influence = weight * t * t * (3 - 2 * t);
			influenceGradient = -weight * 6 * t * (1 - t) / max( fadeWidth, 0.000001 ) * gradient;
		}

		if ( operation == DEFORMATION_OP_INFLATE )
		{
			float inflation = amountFalloff.y;
			float3 radial = p;
			derivative = float3x3( inflation, 0, 0, 0, inflation, 0, 0, 0, inflation );
			if ( shape == DEFORMATION_SHAPE_CAPSULE )
			{
				float lengthSquared = dot( size, size );
				float projection = dot( p, size ) / max( lengthSquared, 0.00000001 );
				radial -= size * clamp( projection, -1, 1 );
				if ( abs( projection ) < 1 && lengthSquared > 0.00000001 )
				{
					float3x3 axisDerivative = float3x3( size.x * size, size.y * size, size.z * size ) / lengthSquared;
					derivative -= mul( derivative, axisDerivative );
				}
			}
			delta = radial * inflation;

			// Smoothly approach the interior clearance instead of hard-clamping the target.
			// Differentiate the same bounded displacement so shading remains continuous.
			if ( shape != DEFORMATION_SHAPE_INFINITE )
			{
				float clearance = -distance;
				float lengthSquared = dot( delta, delta );
				float inverseLength = rsqrt( clearance * clearance + lengthSquared );
				float scale = clearance * inverseLength;
				float3x3 projection = float3x3( delta.x * delta, delta.y * delta, delta.z * delta ) * (inverseLength * inverseLength);
				float3x3 identity = float3x3( 1, 0, 0, 0, 1, 0, 0, 0, 1 );
				derivative = scale * mul( identity - projection, derivative )
					- float3x3( delta.x * gradient, delta.y * gradient, delta.z * gradient )
					* (lengthSquared * inverseLength * inverseLength * inverseLength);
				delta *= scale;
			}
		}
		else if ( operation == DEFORMATION_OP_SQUASH_STRETCH )
		{
			float stretch = clamp( amountFalloff.x, 0, 2 );
			// A flat endpoint is valid. Cap the compensating width before dividing by height.
			float width = rsqrt( max( 0.01, stretch ) );
			float3 scaleDelta = float3( width - 1, width - 1, stretch - 1 );
			delta = p * scaleDelta;
			derivative = float3x3( scaleDelta.x, 0, 0, 0, scaleDelta.y, 0, 0, 0, scaleDelta.z );
		}
		else if ( operation == DEFORMATION_OP_TRANSFORM )
		{
			float3x4 transform = float3x4( operationTransform[0], operationTransform[1], operationTransform[2] );
			delta = mul( transform, float4( p, 1 ) ) - p;
			derivative = (float3x3)transform - float3x3( 1, 0, 0, 0, 1, 0, 0, 0, 1 );
		}
		else if ( operation == DEFORMATION_OP_SUCK )
		{
			delta = -p;
			derivative = float3x3( -1, 0, 0, 0, -1, 0, 0, 0, -1 );
		}
		else if ( operation == DEFORMATION_OP_PROJECT )
		{
			// Project deliberately has no edge fade: Value 1 must reach the surface everywhere inside.
			delta = ProjectToSurface( p, derivative ) - p;
			derivative -= float3x3( 1, 0, 0, 0, 1, 0, 0, 0, 1 );
		}

		derivative = derivative * influence + float3x3( delta.x * influenceGradient, delta.y * influenceGradient, delta.z * influenceGradient );
		delta *= influence;
		ConstrainToShape( p, delta, derivative );
	}

	// Samples displacement without computing a shading frame.
	float3 SampleDisplacement( float3 position )
	{
		float3 delta;
		float3x3 derivative;
		float distance;
		Evaluate( position, delta, derivative, distance );
		return delta;
	}
};

#endif
