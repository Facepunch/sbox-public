using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceMaterialBuilder
{
	private Material CreateTwoTexture( VmtParameters parameters, TextureTransforms transforms, TextureAnimation? animation, string resourceName )
	{
		var name = resourceName ?? $"{FullPath}.vmat";
		var cloakEnabled = parameters.Boolean( "$cloakpassenabled" );
		if ( cloakEnabled && parameters.Float( "$cloakfactor" ) > 0 )
			throw new NotSupportedException( $"{name}: UnlitTwoTexture $cloakpassenabled requires a runtime cloak/refraction second pass." );

		var color = Load( "$basetexture", "$frame" );
		var color2 = Load( "$texture2", "$frame2" );
		var alpha = parameters.Boolean( "$noalphamod" ) ? 1 : Math.Clamp( parameters.Float( "$alpha", 1 ), 0, 1 );
		var alphaModulating = alpha < 1 || this.alpha.CanBeTranslucent;
		var alphaTest = parameters.Boolean( "$alphatest" );
		var textureAlpha = !parameters.Boolean( "$opaquetexture" )
			&& !parameters.Boolean( "$selfillum" ) && !parameters.Boolean( "$basealphaenvmapmask" )
			&& (parameters.Boolean( "$translucent" ) || alphaTest);
		var blended = alphaModulating || parameters.Boolean( "$translucent" )
			|| (textureAlpha && (color.HasAlpha || color2.HasAlpha));
		var additive = parameters.Boolean( "$additive" );
		// Destination alpha ownership uses the first texture, independently of the actual blend state.
		var writeAlpha = !alphaTest && !alphaModulating && !parameters.Boolean( "$vertexalpha" )
			&& !(textureAlpha && color.HasAlpha);
		var tint = parameters.Vector( "$color", Vector3.One ) * parameters.Vector( "$color2", Vector3.One );
		tint = ToLinear( tint, preserveHdr: true );
		if ( !float.IsFinite( tint.x ) || !float.IsFinite( tint.y ) || !float.IsFinite( tint.z ) )
			throw new InvalidDataException( $"{name}: UnlitTwoTexture color modulation overflows." );

		var material = CreateShaderMaterial( "shaders/source1/unlittwotexture.shader", resourceName );
		animation.GetValueOrDefault().Bind( material, color );
		material.SetFeature( "F_TRANSLUCENT", blended || additive ? 1 : 0 );
		material.SetFeature( "F_SOURCE_ALPHA_BLEND", blended ? 1 : 0 );
		material.SetFeature( "F_ADDITIVE_BLEND", additive ? 1 : 0 );
		material.SetFeature( "F_SOURCE_ALPHA_WRITE", writeAlpha ? 1 : 0 );
		material.SetFeature( "F_SOURCE_IGNORE_Z", parameters.Boolean( "$ignorez" ) ? 1 : 0 );
		material.SetFeature( "F_RENDER_BACKFACES", parameters.Boolean( "$nocull" ) ? 1 : 0 );
		color.Bind( material, "g_tColor" );
		color2.Bind( material, "g_tTexture2" );
		material.Set( "g_vSourceColorTint", tint );
		material.Set( "g_flSourceAlpha", alpha );
		transforms.Get( "$basetexturetransform" ).Bind( material, "Base" );
		transforms.Get( "$texture2transform" ).Bind( material, "Texture2" );

		if ( alphaTest || parameters.Boolean( "$vertexalpha" ) )
			Log.Trace( $"{name}: the Source DX9 UnlitTwoTexture pass does not clip pixels or consume vertex alpha; $alphatest and $vertexalpha only affect render-state selection." );
		if ( parameters.Boolean( "$vertexcolor" ) )
			Log.Trace( $"{name}: UnlitTwoTexture does not consume vertex color; only static $color and $color2 modulation is used." );
		if ( cloakEnabled )
			Log.Trace( $"{name}: runtime cloaking is not implemented; rendering the inactive UnlitTwoTexture base pass." );
		foreach ( var key in new[] { "$basetexture2", "$basetexturetransform2", "$cloakfactor", "$cloakcolortint", "$refractamount",
			"$alphatestreference", "$srgbtint", "$znearer", "$decal", "$wireframe" } )
		{
			if ( parameters.Contains( key ) )
				Log.Trace( $"{name}: {key} is not implemented by the UnlitTwoTexture base pass." );
		}
		return material;

		SurfaceTexture Load( string key, string frameKey )
		{
			var path = parameters.String( key );
			if ( string.IsNullOrWhiteSpace( path ) )
				throw new InvalidDataException( $"{name}: UnlitTwoTexture requires {key}." );
			var loaded = LoadSurfaceTexture( path, parameters.Integer( frameKey ), key == "$basetexture" ? animation : null );
			if ( loaded.IsCube )
				throw new InvalidDataException( $"{name}: {key} requires a 2D texture: '{path}'." );
			return loaded;
		}
	}
}
