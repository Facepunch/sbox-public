using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceMaterialBuilder
{
	private Material CreateRefract( bool core, string resourceName, bool aftershock = false )
	{
		var material = CreateShaderMaterial( $"shaders/source1/{shaderName}.shader", resourceName );
		material.SetFeature( "F_SOURCE_DEPTH_WRITE", !aftershock && !parameters.Boolean( "$nowritez" ) ? 1 : 0 );
		material.SetFeature( "F_RENDER_BACKFACES", parameters.Boolean( "$nocull" ) ? 1 : 0 );
		var normalPath = parameters.String( "$normalmap", parameters.String( "$bumpmap" ) );
		var normal = Load( normalPath, "$bumpframe", animation );
		normal?.Bind( material, "g_tNormal" );
		animation.GetValueOrDefault().Bind( material, normal );
		material.Set( "g_flSourceNormalAlpha", normal?.HasAlpha == true );
		material.Set( "g_nSourceNormalEncoding", normal?.Header.HiResFormat == VtfFormat.UV88 ? 1 : 0 );
		Bind( "$normalmap2", "$bumpframe2", "g_tNormal2", "g_flSourceHasNormal2" );
		Bind( "$refracttinttexture", "$refracttinttextureframe", "g_tSourceTint", "g_flSourceHasTint" );
		Bind( "$flowmap", "$flowmapframe", "g_tSourceFlow", "g_flSourceHasFlow" );
		Bind( "$corecolortexture", "$corecolortextureframe", "g_tSourceCoreColor", "g_flSourceHasCoreColor" );
		var basePath = parameters.String( "$basetexture" );
		if ( !string.IsNullOrWhiteSpace( basePath ) && !IsFramebufferTexture( basePath ) && !parameters.Boolean( "$localrefract" ) )
			Bind( "$basetexture", "$frame", "g_tColor", "g_flSourceHasBase" );
		var environment = parameters.String( "$envmap" );
		var sceneReflection = environment?.Trim().Equals( "env_cubemap", StringComparison.OrdinalIgnoreCase ) == true;
		if ( !string.IsNullOrWhiteSpace( environment ) && !sceneReflection )
		{
			var cube = LoadEnvironmentTexture( environment, parameters.Integer( "$envmapframe" ) );
			if ( !cube.IsCube )
				throw new InvalidDataException( $"{FullPath}: $envmap requires a cube texture: '{environment}'." );
			cube.Bind( material, "g_tEnvCube" );
			material.Set( "g_flSourceHasEnvCube", true );
		}
		material.Set( "g_flSourceSceneReflection", sceneReflection );
		material.Set( "g_vSourceRefractTint", parameters.Vector( "$refracttint", Vector3.One ) );
		material.Set( "g_vSourceEnvTint", parameters.Vector( "$envmaptint", Vector3.One ) );
		material.Set( "g_vSourceEnvContrast", parameters.Vector( "$envmapcontrast", Vector3.Zero ) );
		material.Set( "g_vSourceEnvSaturation", parameters.Vector( "$envmapsaturation", Vector3.One ) );
		material.Set( "g_flSourceRefractAmount", parameters.Float( "$refractamount", aftershock ? 0.1f : 2 ) );
		material.Set( "g_flSourceBlur", aftershock ? 0 : Math.Clamp( parameters.Integer( "$bluramount" ), 0, 1 ) );
		material.Set( "g_flSourceFadeSilhouette", parameters.Boolean( "$fadeoutonsilhouette" ) );
		material.Set( "g_flSourceVertexColor", parameters.Boolean( "$vertexcolormodulate" ) );
		alpha.Bind( material );
		transforms.Get( transforms.Contains( "$bumptransform" ) ? "$bumptransform" : "$normalmaptransform" ).Bind( material, "Normal" );
		transforms.Get( "$bumptransform2" ).Bind( material, "Normal2" );
		if ( parameters.Boolean( "$masked" ) )
			Log.Trace( $"{FullPath}: destination-alpha refraction masks are not represented by the native framebuffer copy." );
		if ( aftershock )
		{
			material.Set( "g_vSourceRefractTint", parameters.Vector( "$colortint", Vector3.One ) );
			material.Set( "g_vSourceSilhouetteColor", parameters.Vector( "$silhouettecolor", new Vector3( 0.3f, 0.3f, 0.5f ) ) );
			material.Set( "g_flSourceSilhouetteThickness", parameters.Float( "$silhouettethickness", 0.2f ) );
			material.Set( "g_vSourceGroundRange", new Vector2( parameters.Float( "$groundmin", -0.3f ), parameters.Float( "$groundmax", -0.1f ) ) );
			material.Set( "g_flSourceShockBlur", parameters.Float( "$bluramount", 0.01f ) );
			material.Set( "g_flSourceEffectTime", parameters.Float( "$time" ) );
			material.Set( "g_flSourceEffectRuntime", UsesCurrentTime() || parameters.Float( "$time" ) <= 0 );
		}
		if ( core )
		{
			material.Set( "g_vSourceCoreOrigin", parameters.Vector( "$entityorigin", new Vector3( 2688, 12139, 5170 ) ) );
			var diameter = parameters.Float( "$diameter", 430 );
			if ( diameter <= 0 )
				throw new InvalidDataException( $"{FullPath}: $diameter must be positive." );
			material.Set( "g_flSourceCoreDiameter", diameter );
			material.Set( "g_vSourceFlowScroll", parameters.Pair( "$flowmapscrollrate", Vector2.Zero ) );
			material.Set( "g_flSourceCoreOffset", parameters.Float( "$flowmaptexcoordoffset" ) );
			foreach ( var proxy in proxies?.Children ?? [] )
			{
				var inputs = new VmtParameters( proxy.Values, FullPath );
				if ( proxy.Name.Equals( "EntityOrigin", StringComparison.OrdinalIgnoreCase )
					&& inputs.String( "resultvar" )?.Equals( "$entityorigin", StringComparison.OrdinalIgnoreCase ) == true )
					material.Set( "g_flSourceCoreEntityOrigin", true );
				else if ( proxy.Name.Equals( "Sine", StringComparison.OrdinalIgnoreCase )
					&& inputs.String( "resultvar" )?.Equals( "$flowmaptexcoordoffset", StringComparison.OrdinalIgnoreCase ) == true )
				{
					var period = Scalar( "sineperiod", 1 );
					if ( period == 0 ) period = 1;
					var min = Scalar( "sinemin", 0 );
					var max = Scalar( "sinemax", 1 );
					if ( !float.IsFinite( (float)(Math.Tau / period) ) || !float.IsFinite( max - min ) )
						throw new InvalidDataException( $"{FullPath}: Core Sine parameters overflow shader evaluation." );
					material.Set( "g_vSourceCoreSine", new Vector4( period, Scalar( "timeoffset", 0 ), min, max ) );
					material.Set( "g_flSourceCoreSine", true );

					float Scalar( string key, float fallback ) => inputs.String( key ) is { } value && value.StartsWith( '$' )
						? parameters.Float( value ) : inputs.Float( key, fallback );
				}
			}
		}
		return material;

		SurfaceTexture Load( string path, string frameKey, TextureAnimation? playback = null )
		{
			if ( string.IsNullOrWhiteSpace( path ) ) return null;
			var texture = LoadSurfaceTexture( path, parameters.Integer( frameKey ), playback );
			if ( texture.IsCube )
				throw new InvalidDataException( $"{FullPath}: '{path}' requires a 2D texture." );
			return texture;
		}

		void Bind( string key, string frameKey, string input, string enabled )
		{
			var texture = Load( parameters.String( key ), frameKey );
			texture?.Bind( material, input );
			material.Set( enabled, texture is not null );
		}
	}

	private bool UsesCurrentTime() => proxies?.Children.Any( proxy => proxy.Name.Equals( "CurrentTime", StringComparison.OrdinalIgnoreCase )
		&& proxy.Values.TryGetValue( "resultvar", out var target ) && target.Equals( "$time", StringComparison.OrdinalIgnoreCase ) ) == true;
}
