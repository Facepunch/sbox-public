// toolgun_screen: the toolgun's dot-matrix LCD, a surface. One rounded dot per pixel of what the weapon paints (the
// object's Emissive render target), each a small dome bumped into the glass's normal, with a scanline sweep and gaps
// between rows, lit as emission over black glass with the material's normal, roughness and occlusion. Was
// toolgun-screen.shader.
import sbox.surface;

[Material, Features( "Common" ), Vertex( "Common" )]
public struct ToolgunScreen : ISurface
{
	[Sampler( "Anisotropic", "Wrap" )] SamplerState ScreenFiltering;
	[Sampler( "Point", "Clamp" )] SamplerState ScreenPointClamp;

	[InputWith( "TextureAmbientOcclusion", "Material,10/Ambient Occlusion", "Linear; _ao; Default( 1.0 )" ), Pack( "R: AmbientOcclusion", "BC7" )] Texture2D Ao;
	[InputWith( "TextureRoughness", "Material,10/Roughness", "Linear; _rough; Default( 0.5 )" ), Pack( "R: Roughness", "BC7" )] Texture2D Roughness;
	[InputWith( "TextureNormal", "Material,10/Normal", "NormalizeNormals; _normal; Default3( 0.5, 0.5, 1.0 )" ), Pack( "RGB: Normal", "DXT5" )] Texture2D Normal;

	// What the weapon paints (ScreenWeapon)
	[Engine( "Emissive" )] Texture2D g_tSelfIllumMask;

	public Material Evaluate( SurfaceInput i )
	{
		float2 uv = i.Uv;

		// One dot per pixel of the screen texture
		float2 grid;
		g_tSelfIllumMask.GetDimensions( grid.x, grid.y );

		// Sample emissive content
		float3 emissive = g_tSelfIllumMask.SampleLevel( ScreenPointClamp, uv, 0 ).rgb;

		// Dot matrix cell coordinates
		float2 cell = frac( uv * grid );
		float2 cellCentered = cell - 0.5;

		float dotRadius = 0.5;
		float dist = length( cellCentered );
		float dotMask = 1.0 - smoothstep( dotRadius - 0.08, dotRadius + 0.08, dist );

		// Per-dot hemisphere normals in tangent space
		float2 nxy = cellCentered / dotRadius;
		float nzSq = saturate( 1.0 - dot( nxy, nxy ) );
		float3 domeNormalTs = normalize( float3( -nxy.x, nxy.y, sqrt( nzSq ) ) );

		float3 glassNormalTs = DecodeNormal( i.Sample( Normal, uv, ScreenFiltering ).rgb );
		float bump = dotMask * 0.025;
		float3 normalTs = normalize( float3( glassNormalTs.xy + domeNormalTs.xy * bump, glassNormalTs.z * lerp( 1.0, domeNormalTs.z, bump ) ) );

		// Dome shading falloff
		float dome = 1.0 - smoothstep( 0.0, dotRadius, dist ) * 0.25;

		// LCD emissive color
		float3 lcdColor = emissive * dotMask * dome * 5.0f;

		// Scanline refresh sweep
		float scanline = sin( ( uv.y - i.Time * 1.5 ) * 80.0 ) * 0.5 + 0.5;
		scanline = smoothstep( 0.3, 0.7, scanline );
		lcdColor *= lerp( 0.9, 1.0, scanline );

		// Horizontal row gaps
		float rowLine = smoothstep( 0.0, 0.06, abs( cellCentered.y ) );
		lcdColor *= lerp( 0.85, 1.0, rowLine );

		Material m = Material.Init( i );
		m.Albedo = 0.0f;
		m.AmbientOcclusion = i.Sample( Ao, uv, ScreenFiltering ).r;
		m.Roughness = min( i.Sample( Roughness, uv, ScreenFiltering ).r, lerp( 1.0, 0.25, dotMask ) );
		m.Normal = TransformNormal( normalTs, i.Normal, i.TangentU, i.TangentV );
		m.Emission = lcdColor;
		return m;
	}
}
