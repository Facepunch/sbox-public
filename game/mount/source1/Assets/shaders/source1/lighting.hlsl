float3 g_vSourcePhongFresnelRanges < Default3( 0, 0.5, 1 ); >;
float g_flSourcePhongAlbedoBoost < Default( 1 ); >;
float g_flSourceEnvFresnel < Default( 0 ); >;
float g_flSourceFresnelReflection < Default( 1 ); >;
float g_flSourceLightmappedGeneric < Default( 0 ); >;

float SourceFresnel( float facing )
{
	float fresnel = (1 - saturate( facing )) * (1 - saturate( facing ));
	return fresnel < 0.5
		? lerp( g_vSourcePhongFresnelRanges.x, g_vSourcePhongFresnelRanges.y, 2 * fresnel )
		: lerp( g_vSourcePhongFresnelRanges.y, g_vSourcePhongFresnelRanges.z, 2 * fresnel - 1 );
}

void SourceBakedDynamicLighting( inout LightingTerms_t lighting, const CombinerInput combiner )
{
	lighting.vDiffuse = 0;
	lighting.vSpecular = 0;
	lighting.vTransmissive = 0;
	float3 receiverNormal = ComputeShadowReceiverNormal( combiner.vPositionWs );
	ClusterRange range = Cluster::Query( ClusterItemType_Light, combiner.vPositionSs );
	[loop]
	for ( uint index = 0; index < range.Count; index++ )
		ComputeDirectLightingForLight( combiner, receiverNormal,
			DynamicLightConstantByIndex( Cluster::LoadItem( range, index ) ),
			lighting.vDiffuse, lighting.vSpecular, lighting.vTransmissive );
}
