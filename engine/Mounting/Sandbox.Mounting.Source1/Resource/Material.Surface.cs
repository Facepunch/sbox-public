using System;
using System.IO;
using VtfFlags = Sandbox.Mounting.Source1.VtfFlags;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceMaterialBuilder
{
	private Material CreateSurface( string shader, VmtParameters parameters, TextureTransforms transforms, MaterialAlpha alpha, TextureAnimation? animation, string resourceName, bool mapSurface = false )
	{
		var name = resourceName ?? $"{FullPath}.vmat";
		var warp = shader == "vortwarp";
		var worldBlend = shader == "worldtwotextureblend";
		var unlit = shader == "unlitgeneric" || (warp && parameters.Boolean( "$unlit" ));

		var modelSurface = shader is "vertexlitgeneric" or "unlitgeneric" or "vortwarp";
		var skin = shader == "vertexlitgeneric" && parameters.Boolean( "$phong" );
		var framebuffer = IsFramebufferTexture( parameters.String( "$basetexture" ) );
		if ( framebuffer && (parameters.Integer( "$frame" ) != 0 || animation is { FrameRate: > 0 }) )
			throw new NotSupportedException( $"{FullPath}: framebuffer textures do not support frames or animation." );
		var normal = Load( "$bumpmap", "$bumpframe" );
		var albedo = worldBlend && normal != null && !parameters.Boolean( "$nodiffusebumplighting" )
			&& !string.IsNullOrWhiteSpace( parameters.String( "$basetexture" ) )
			&& !string.IsNullOrWhiteSpace( parameters.String( "$albedo" ) );
		var color = framebuffer ? null : Load( albedo ? "$albedo" : "$basetexture", "$frame" );
		var detail = Load( "$detail", "$detailframe" );
		var seamless = shader == "lightmappedgeneric" && shaderName != "monitorscreen"
			? parameters.Float( "$seamless_scale" ) : 0;
		if ( seamless != 0 && detail != null )
			throw new NotSupportedException( $"{name}: Source's seamless LightmappedGeneric shader does not have a detail-texture combination." );
		if ( worldBlend && parameters.Float( "$seamless_scale" ) != 0 )
			throw new NotSupportedException( $"{name}: $seamless_scale is not implemented for WorldTwoTextureBlend." );
		var detailMask = worldBlend && parameters.Boolean( "$detail_alpha_mask_base_texture" );
		if ( worldBlend && normal != null && detail != null && !detailMask )
			throw new NotSupportedException( $"{name}: Source's WorldTwoTextureBlend requires $detail_alpha_mask_base_texture to combine detail and bump textures." );
		if ( worldBlend && normal != null && parameters.Boolean( "$vertexcolor" ) )
			throw new NotSupportedException( $"{name}: Source's WorldTwoTextureBlend does not have a vertex-color and bump-texture combination." );
		var selfIllumMask = !worldBlend && parameters.Boolean( "$selfillum" ) ? Load( "$selfillummask" ) : null;
		var exponent = skin ? Load( "$phongexponenttexture" ) : null;
		var warpFlow = warp ? Load( "$flowmap" ) : null;
		var warpEmission = warp ? Load( "$selfillummap" ) : null;

		var normalEnvMask = parameters.Boolean( "$normalmapalphaenvmapmask" ) && (normal != null || skin);
		var baseEnvMask = !normalEnvMask && parameters.Boolean( "$basealphaenvmapmask" ) && color?.HasAlpha == true;
		var envMask = !worldBlend && !skin && !normalEnvMask && !baseEnvMask ? Load( "$envmapmask", "$envmapmaskframe" ) : null;
		var environmentPath = worldBlend ? null : parameters.String( "$envmap" );
		var environment = !string.IsNullOrWhiteSpace( environmentPath );
		if ( !skin && normal != null && !normalEnvMask && (baseEnvMask || envMask != null) )
		{
			Log.Trace( $"{name}: a generic bumped surface needs $normalmapalphaenvmapmask; disabling conflicting environment reflection." );
			environment = false;
			envMask = null;
		}
		var sceneEnvironment = environment && environmentPath.Trim().Equals( "env_cubemap", StringComparison.OrdinalIgnoreCase );
		var envTexture = environment && !sceneEnvironment
			? LoadEnvironmentTexture( environmentPath, parameters.Integer( "$envmapframe" ) ) : null;
		var sphere = envTexture is { IsCube: false };
		if ( envTexture is { IsCube: true } && parameters.Boolean( "$envmapsphere" ) )
			throw new InvalidDataException( $"{name}: $envmapsphere requires a 2D environment texture." );
		if ( sphere && !parameters.Boolean( "$envmapsphere" ) )
			Log.Trace( $"{name}: using legacy 2D environment mapping for '{environmentPath}'." );
		var envMaskMode = normalEnvMask ? 2 : baseEnvMask ? 1 : 0;
		var phongMask = skin && (parameters.Boolean( "$basemapalphaphongmask" ) || normal == null) ? 1 : 2;
		if ( skin && phongMask != 1 && parameters.Boolean( "$basemapluminancephongmask" ) )
			phongMask = 3;
		var selfIllum = parameters.Boolean( "$selfillum" ) && (selfIllumMask != null || color?.HasAlpha == true);
		if ( parameters.Boolean( "$selfillum" ) && !selfIllum )
			Log.Trace( $"{name}: $selfillum has neither a mask nor authored base alpha; disabling self illumination." );
		var blendTint = modelSurface && parameters.Boolean( "$blendtintbybasealpha" );
		if ( skin && selfIllum )
			blendTint = false;
		if ( blendTint && !skin && (selfIllum || (baseEnvMask && normal == null)) )
			throw new InvalidDataException( $"{name}: generic $blendtintbybasealpha cannot share alpha with $selfillum or $basealphaenvmapmask." );

		// Keep opacity ownership consistent with the shader's independent mask selection.
		var ownsAlpha = selfIllum || (worldBlend && baseEnvMask) || (skin && phongMask == 1)
			|| (environment && (skin ? envMaskMode != 2 || phongMask == 1 : envMaskMode == 1));
		var additive = parameters.Boolean( "$additive" );
		var detailMode = worldBlend ? detailMask ? 12 : 13 : parameters.Integer( "$detailblendmode" );
		if ( !worldBlend && detail != null && (detail.Header.Flags & VtfFlags.SSBUMP) != 0 )
			detailMode = normal != null ? 10 : 11;
		if ( !worldBlend && (detailMode is < 0 or > 11) )
			throw new NotSupportedException( $"{name}: unsupported $detailblendmode {detailMode}." );
		var detailAlpha = detail?.HasAlpha == true && detailMode is 3 or 4 or 8 or 9;
		var hasOpacity = !ownsAlpha && ((!blendTint && (color?.HasAlpha == true))
			|| (detailAlpha && (!blendTint || !skin)));
		var vertexAlpha = parameters.Boolean( "$vertexalpha" );
		var alphaTest = parameters.Boolean( "$alphatest" ) && !ownsAlpha;
		var translucent = framebuffer || additive || alpha.CanBeTranslucent || vertexAlpha
			|| (parameters.Boolean( "$translucent" ) && hasOpacity);

		var material = CreateShaderMaterial( $"shaders/source1/{shaderName}.shader", resourceName );
		material.Set( "g_flSourceReceiveDecals", !parameters.Boolean( "$nodecal" ) );
		material.Set( "g_flSourceSeamlessScale", seamless );
		animation.GetValueOrDefault().Bind( material, color );
		if ( warp )
			material.SetFeature( "F_SOURCE_UNLIT", unlit ? 1 : 0 );
		material.SetFeature( "F_TRANSLUCENT", translucent ? 1 : 0 );
		material.SetFeature( "F_ADDITIVE_BLEND", additive ? 1 : 0 );
		material.SetFeature( "F_ALPHA_TEST", alphaTest ? 1 : 0 );
		material.SetFeature( "F_RENDER_BACKFACES", parameters.Boolean( "$nocull" ) ? 1 : 0 );
		material.SetFeature( "F_SOURCE_FRAMEBUFFER", framebuffer ? 1 : 0 );

		Bind( color, "g_tColor" );
		Bind( normal, "g_tNormal", "g_flSourceHasNormal" );
		Bind( detail, "g_tDetail", "g_flSourceHasDetail" );
		Bind( selfIllumMask, "g_tSelfIllumMask", "g_flSourceHasSelfIllumMask" );
		Bind( exponent, "g_tPhongExponent", "g_flSourceHasPhongExponent" );
		Bind( envMask, "g_tEnvMask", "g_flSourceHasEnvMask" );
		Bind( sphere ? null : envTexture, "g_tEnvCube", "g_flSourceHasEnvCube" );
		Bind( sphere ? envTexture : null, "g_tEnvSphere", "g_flSourceHasEnvSphere" );
		Bind( warpFlow, "g_tSourceWarpFlow" );
		Bind( warpEmission, "g_tSourceWarpEmission" );
		material.Set( "g_flSourceWarpEmission", warpFlow != null && warpEmission != null );
		material.Set( "g_flSourceWarpParam", parameters.Float( "$warpparam" ) );
		material.Set( "g_vSourceWarpOrigin", parameters.Vector( "$entityorigin", Vector3.Zero ) );
		material.Set( "g_flSourceWarpEntityOrigin", warp && proxies?.Children.Any( proxy =>
			proxy.Name.Equals( "EntityOrigin", StringComparison.OrdinalIgnoreCase )
			&& proxy.Values.TryGetValue( "resultvar", out var target )
			&& target.Equals( "$entityorigin", StringComparison.OrdinalIgnoreCase ) ) == true );

		material.Set( "g_vSourceTextureAlphaPresence", new Vector4(
			alphaTest ? 1 : Alpha( color ), 0, Alpha( normal ), 0 ) );
		material.Set( "g_flSourceDetailAlphaPresent", Alpha( detail ) );
		alpha.Bind( material );
		material.Set( "g_flSourceAlphaTestReference", Math.Clamp( parameters.Float( "$alphatestreference", 0.5f ), 0, 1 ) );
		material.Set( "g_flSourceUseBaseAlpha", !ownsAlpha && !blendTint );
		material.Set( "g_flSourceBlendTintByBaseAlpha", blendTint );
		SetFloat( "g_flSourceBlendTintColorOverBase", "$blendtintcoloroverbase", 0 );
		material.Set( "g_flSourceVertexColor", parameters.Boolean( "$vertexcolor" ) );
		material.Set( "g_flSourceVertexAlpha", vertexAlpha );
		material.Set( "g_nSourceNormalEncoding", normal?.Header.HiResFormat == VtfFormat.UV88 ? 1 : 0 );
		material.Set( "g_flSourceDiffuseBumpLighting", !parameters.Boolean( "$nodiffusebumplighting" ) );
		material.Set( "g_flSourceSsBump", !worldBlend && (parameters.Boolean( "$ssbump" ) || IsSsBump( normal )) );
		material.Set( "g_flSourceNormalScale", worldBlend ? 1 : parameters.Float( "$bumpstrength", 1 ) );
		material.Set( "g_flSourceSkin", skin );
		material.Set( "g_flSourcePhong", skin );
		material.Set( "g_nSourcePhongMask", phongMask );
		material.Set( "g_flSourcePhongMaskInvert", skin && parameters.Boolean( "$invertphongmask" ) );
		SetFloat( "g_flSourcePhongExponent", "$phongexponent", 0 );
		SetFloat( "g_flSourcePhongExponentFactor", "$phongexponentfactor", 0 );
		SetFloat( "g_flSourcePhongBoost", "$phongboost", 1 );
		SetVector( "g_vSourcePhongTint", "$phongtint", Vector3.Zero );
		material.Set( "g_flSourcePhongAlbedoTint", parameters.Boolean( "$phongalbedotint" ) );
		SetFloat( "g_flSourcePhongAlbedoBoost", "$phongalbedoboost", 1 );
		SetVector( "g_vSourcePhongFresnelRanges", "$phongfresnelranges", new Vector3( 0, 0.5f, 1 ) );
		SetFloat( "g_flSourceEnvFresnel", "$envmapfresnel", 0 );
		SetFloat( "g_flSourceFresnelReflection", "$fresnelreflection", 1 );
		material.Set( "g_flSourceLightmappedGeneric", !modelSurface && shaderName != "monitorscreen" );
		material.Set( "g_flSourceSelfIllum", selfIllum );
		SetVector( "g_vSourceSelfIllumTint", "$selfillumtint", Vector3.One, linearize: worldBlend );
		material.Set( "g_flSourceSelfIllumFresnel", !worldBlend && selfIllum && parameters.Boolean( "$selfillumfresnel" ) );
		SetVector( "g_vSourceSelfIllumFresnel", "$selfillumfresnelminmaxexp", new Vector3( 0, 1, 1 ) );
		material.Set( "g_nSourceDetailMode", detailMode );
		SetFloat( "g_flSourceDetailBlendFactor", "$detailblendfactor", 1 );
		if ( worldBlend )
			material.Set( "g_vSourceDetailTint", Vector3.One );
		else
			SetVector( "g_vSourceDetailTint", "$detailtint", Vector3.One, linearize: true );
		material.Set( "g_flSourceDetailSrgb", detailMode is > 0 and < 10 );
		material.Set( "g_flSourceEnvMap", environment );
		material.Set( "g_nSourceEnvMask", envMaskMode );
		SetVector( "g_vSourceEnvTint", "$envmaptint", Vector3.One, linearize: !skin );
		SetVector( "g_vSourceEnvContrast", "$envmapcontrast", Vector3.Zero );
		SetVector( "g_vSourceEnvSaturation", "$envmapsaturation", Vector3.One );
		var tint = ToLinear( parameters.Vector( "$color", Vector3.One ) * parameters.Vector( "$color2", Vector3.One ),
			preserveHdr: modelSurface );
		SetColor( "g_vSourceColorTint", tint );

		var baseTransform = transforms.Get( "$basetexturetransform" );
		var bumpTransform = transforms.Get( "$bumptransform", baseTransform );
		var detailTransform = worldBlend ? baseTransform : transforms.Get( "$detailtexturetransform", baseTransform );
		var detailScale = parameters.Float( "$detailscale", 4 );
		baseTransform.Bind( material, "Base" );
		if ( worldBlend && detail != null )
			detailTransform.Bind( material, "Normal", detailScale );
		else
			bumpTransform.Bind( material, "Normal" );
		detailTransform.Bind( material, "Detail", detailScale );
		transforms.Get( "$envmapmasktransform", baseTransform ).Bind( material, "EnvMask" );
		if ( shaderName == "worldvertextransition" )
			BindWorldTransition( material, color, normal );
		if ( parameters.Boolean( "$distancealpha" ) )
			Log.Trace( $"{name}: $distancealpha is not implemented by the surface shader." );
		if ( parameters.Float( "$seamless_scale" ) != 0 && seamless == 0 )
			Log.Trace( $"{name}: $seamless_scale is not implemented by '{shaderName}'." );
		if ( !string.IsNullOrWhiteSpace( parameters.String( "$parallaxmap" ) ) )
			Log.Trace( $"{name}: $parallaxmap is not implemented by the surface shader." );
		if ( !skin && parameters.Boolean( "$phong" ) )
			Log.Trace( $"{name}: $phong is only implemented for VertexLitGeneric." );
		if ( !modelSurface && parameters.Boolean( "$blendtintbybasealpha" ) )
			Log.Trace( $"{name}: $blendtintbybasealpha is only implemented for VertexLitGeneric and UnlitGeneric." );
		if ( parameters.Boolean( "$halflambert" ) || parameters.Boolean( "$rimlight" )
			|| !string.IsNullOrWhiteSpace( parameters.String( "$lightwarptexture" ) )
			|| !string.IsNullOrWhiteSpace( parameters.String( "$phongwarptexture" ) ) )
			Log.Trace( $"{name}: native s&box lighting replaces Source Half-Lambert, light/specular warps and rim lighting." );
		return material;

		SurfaceTexture Load( string key, string frameKey = null, bool cube = false )
		{
			var path = parameters.String( key );
			if ( string.IsNullOrWhiteSpace( path ) ) return null;
			var loaded = LoadSurfaceTexture( path, frameKey == null ? 0 : parameters.Integer( frameKey ),
				key is "$basetexture" or "$albedo" ? animation : null );
			if ( loaded.IsCube != cube )
				throw new InvalidDataException( $"{name}: {key} requires a {(cube ? "cube" : "2D")} texture: '{path}'." );
			return loaded;
		}

		void Bind( SurfaceTexture texture, string input, string enabled = null )
		{
			if ( enabled != null ) material.Set( enabled, texture != null );
			texture?.Bind( material, input );
		}
		void SetFloat( string input, string key, float fallback ) => material.Set( input, parameters.Float( key, fallback ) );
		void SetVector( string input, string key, Vector3 fallback, bool linearize = false )
		{
			var vector = parameters.Vector( key, fallback );
			SetColor( input, linearize ? ToLinear( vector ) : vector );
		}
		void SetColor( string input, Vector3 value )
		{
			if ( !float.IsFinite( value.x ) || !float.IsFinite( value.y ) || !float.IsFinite( value.z ) )
				throw new InvalidDataException( $"{name}: {input} color conversion overflows." );
			material.Set( input, value );
		}
	}

	private static float Alpha( SurfaceTexture texture ) => texture?.HasAlpha == true ? 1 : 0;
	private static bool IsSsBump( SurfaceTexture texture ) => texture != null && (texture.Header.Flags & VtfFlags.SSBUMP) != 0;
}
