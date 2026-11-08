float3 g_vSourcePropAmbient0 < Default3( 0, 0, 0 ); >;
float3 g_vSourcePropAmbient1 < Default3( 0, 0, 0 ); >;
float3 g_vSourcePropAmbient2 < Default3( 0, 0, 0 ); >;
float3 g_vSourcePropAmbient3 < Default3( 0, 0, 0 ); >;
float3 g_vSourcePropAmbient4 < Default3( 0, 0, 0 ); >;
float3 g_vSourcePropAmbient5 < Default3( 0, 0, 0 ); >;

float4 g_vSourcePropLightPosition0 < Default4( 0, 0, 0, -1 ); >;
float4 g_vSourcePropLightPosition1 < Default4( 0, 0, 0, -1 ); >;
float4 g_vSourcePropLightPosition2 < Default4( 0, 0, 0, -1 ); >;
float4 g_vSourcePropLightPosition3 < Default4( 0, 0, 0, -1 ); >;
float4 g_vSourcePropLightColor0 < Default4( 0, 0, 0, 1 ); >;
float4 g_vSourcePropLightColor1 < Default4( 0, 0, 0, 1 ); >;
float4 g_vSourcePropLightColor2 < Default4( 0, 0, 0, 1 ); >;
float4 g_vSourcePropLightColor3 < Default4( 0, 0, 0, 1 ); >;
float4 g_vSourcePropLightDirection0 < Default4( 0, 0, 0, 0 ); >;
float4 g_vSourcePropLightDirection1 < Default4( 0, 0, 0, 0 ); >;
float4 g_vSourcePropLightDirection2 < Default4( 0, 0, 0, 0 ); >;
float4 g_vSourcePropLightDirection3 < Default4( 0, 0, 0, 0 ); >;
float4 g_vSourcePropLightAttenuation0 < Default4( 1, 0, 0, 0 ); >;
float4 g_vSourcePropLightAttenuation1 < Default4( 1, 0, 0, 0 ); >;
float4 g_vSourcePropLightAttenuation2 < Default4( 1, 0, 0, 0 ); >;
float4 g_vSourcePropLightAttenuation3 < Default4( 1, 0, 0, 0 ); >;

float3 SourcePropLocalLight( float3 worldPosition, float3 normal,
	float4 position, float4 color, float4 direction, float4 attenuation )
{
	if ( position.w < 0 )
		return 0;
	if ( position.w == 3 )
		return color.rgb * max( 0, dot( normal, -direction.xyz ) );
	float3 delta = position.xyz - worldPosition;
	float squared = dot( delta, delta );
	float distance = sqrt( squared );
	float3 toLight = delta / max( distance, 0.000001 );
	float diffuse = max( 0, dot( normal, toLight ) );
	float falloff = position.w == 0 ? rcp( max( squared, 0.000001 ) )
		: rcp( max( attenuation.x + attenuation.y * distance + attenuation.z * squared, 0.000001 ) );
	float coneDot = -dot( toLight, direction.xyz );
	if ( position.w == 0 )
		diffuse *= coneDot > 0 ? 1 : 0;
	else if ( position.w == 2 )
	{
		float cone = coneDot >= direction.w ? 1
			: coneDot <= attenuation.w ? 0
			: pow( saturate( (coneDot - attenuation.w) / (direction.w - attenuation.w) ), color.w );
		diffuse *= cone;
	}
	return color.rgb * (diffuse * falloff);
}

float3 SourcePropLighting( float3 worldPosition, float3 normal )
{
	float3 squared = normal * normal;
	float3 color = (normal.x < 0 ? g_vSourcePropAmbient1 : g_vSourcePropAmbient0) * squared.x
		+ (normal.y < 0 ? g_vSourcePropAmbient3 : g_vSourcePropAmbient2) * squared.y
		+ (normal.z < 0 ? g_vSourcePropAmbient5 : g_vSourcePropAmbient4) * squared.z;
	color += SourcePropLocalLight( worldPosition, normal, g_vSourcePropLightPosition0,
		g_vSourcePropLightColor0, g_vSourcePropLightDirection0, g_vSourcePropLightAttenuation0 );
	color += SourcePropLocalLight( worldPosition, normal, g_vSourcePropLightPosition1,
		g_vSourcePropLightColor1, g_vSourcePropLightDirection1, g_vSourcePropLightAttenuation1 );
	color += SourcePropLocalLight( worldPosition, normal, g_vSourcePropLightPosition2,
		g_vSourcePropLightColor2, g_vSourcePropLightDirection2, g_vSourcePropLightAttenuation2 );
	color += SourcePropLocalLight( worldPosition, normal, g_vSourcePropLightPosition3,
		g_vSourcePropLightColor3, g_vSourcePropLightDirection3, g_vSourcePropLightAttenuation3 );
	return color;
}
