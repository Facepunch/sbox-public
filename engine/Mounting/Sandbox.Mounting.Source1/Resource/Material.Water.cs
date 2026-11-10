using System;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceMaterialBuilder
{
	private Material CreateEffect( bool water, VmtParameters parameters, TextureTransforms transforms, MaterialAlpha alpha, TextureAnimation? animation, string resourceName )
	{
		var material = CreateShaderMaterial( "shaders/source1/water.shader", resourceName );
		material.SetFeature( "F_SOURCE_WATER", water ? 1 : 0 );
		material.SetFeature( "F_RENDER_BACKFACES", parameters.Boolean( "$nocull" ) ? 1 : 0 );
		var aboveWater = parameters.Boolean( "$abovewater", true );
		var normal = parameters.String( "$normalmap", water ? "dev/water_normal" : parameters.String( "$bumpmap" ) );
		SurfaceTexture normalTexture = null;
		if ( !string.IsNullOrWhiteSpace( normal ) )
		{
			normalTexture = LoadSurfaceTexture( normal, parameters.Integer( "$bumpframe" ), animation );
			normalTexture.Bind( material, "g_tNormal" );
		}

		(animation ?? new TextureAnimation( 0 )).Bind( material, normalTexture );
		Bind( "$normalmap2", "$bumpframe2", "g_tNormal2", "g_flSourceHasNormal2" );
		Bind( "$refracttinttexture", "$refracttinttextureframe", "g_tSourceTint", "g_flSourceHasTint" );
		var environment = parameters.String( "$envmap", water && aboveWater ? "env_cubemap" : null );
		var sceneReflection = environment?.Equals( "env_cubemap", StringComparison.OrdinalIgnoreCase ) == true;
		if ( !string.IsNullOrWhiteSpace( environment ) && !sceneReflection )
			Bind( "$envmap", "$envmapframe", "g_tSourceEnvCube", "g_flSourceHasEnvCube" );
		material.Set( "g_flSourceSceneReflection", sceneReflection );
		SetColor( "g_vSourceRefractTint", parameters.Vector( "$refracttint", Vector3.One ) );
		SetColor( "g_vSourceReflectTint", parameters.Vector( "$reflecttint", parameters.Vector( "$envmaptint", Vector3.One ) ) );
		SetColor( "g_vSourceFogColor", parameters.Vector( "$fogcolor", water ? new Vector3( 1, 0, 0 ) : Vector3.Zero ) );
		material.Set( "g_flSourceRefractAmount", parameters.Float( "$refractamount", water ? 0.0f : 0.02f ) );
		material.Set( "g_flSourceReflectAmount", parameters.Float( "$reflectamount", water ? 0.8f : 1 ) );
		alpha.Bind( material );
		material.Set( "g_flSourceFogStart", parameters.Float( "$fogstart" ) );
		material.Set( "g_flSourceFogEnd", parameters.Float( "$fogend", 1000 ) );
		material.Set( "g_flSourceBlur", parameters.Float( "$bluramount" ) );
		material.Set( "g_flSourceEnvAlphaMask", parameters.Boolean( "$normalmapalphaenvmapmask" ) );
		material.Set( "g_flSourceFresnel", !parameters.Boolean( "$nofresnel" ) );
		material.Set( "g_flSourceAboveWater", aboveWater );
		material.Set( "g_flSourceCheapWater", water && parameters.Boolean( "$forcecheap" ) );
		material.Set( "g_flSourceWaterBlend", parameters.Float( "$reflectblendfactor", 1 ) );
		material.Set( "g_flSourceBlurRefract", water && parameters.Boolean( "$blurrefract" ) );
		if ( water )
		{
			Log.Warning( $"{FullPath}: water uses native framebuffer refraction and cubemap reflections; Source planar views and underwater volume fog are not supported by engine components." );
			foreach ( var key in new[]
			{
				"$flowmap",
				"$basetexture",
				"$lightmapwaterfog"
			}

			)
				if ( parameters.Contains( key ) )
					Log.Warning( $"{FullPath}: water {key} is not represented; rendering normal-map water without that input." );
		}

		transforms.Get( transforms.Contains( "$bumptransform" ) ? "$bumptransform" : "$normalmaptransform" ).Bind( material, "Normal" );
		var scroll1 = parameters.Pair( "$scroll1", Vector2.Zero );
		var scroll2 = parameters.Pair( "$scroll2", Vector2.Zero );
		material.Set( "g_flSourceWaterMultiTexture", water && scroll1.x != 0 && !parameters.Contains( "$basetexture" ) && !parameters.Contains( "$flowmap" ) );
		material.Set( "g_vSourceNormalLayerScroll", new Vector4( scroll1.x, scroll1.y, scroll2.x, scroll2.y ) );
		return material;
		void Bind( string key, string frameKey, string input, string enabled )
		{
			var path = parameters.String( key );
			if ( string.IsNullOrWhiteSpace( path ) )
				return;
			LoadSurfaceTexture( path, parameters.Integer( frameKey ) ).Bind( material, input );
			material.Set( enabled, true );
		}

		void SetColor( string input, Vector3 color )
		{
			if ( water )
				color = new Vector3( Linear( color.x ), Linear( color.y ), Linear( color.z ) );
			material.Set( input, new Vector3( color.x, color.y, color.z ) );
		}

		static float Linear( float value ) => value > 1 ? value : value >= 0.95f ? 1 : MathF.Pow( MathF.Round( Math.Max( value, 0 ) * 255 ) / 255, 2.2f );
	}
}
