using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceMaterialBuilder
{
	private Material CreateModulate( VmtParameters parameters, TextureTransforms transforms, MaterialAlpha alpha, TextureAnimation? animation, string resourceName )
	{
		var name = resourceName ?? $"{FullPath}.vmat";
		var cloakEnabled = parameters.Boolean( "$cloakpassenabled" );
		if ( cloakEnabled && parameters.Float( "$cloakfactor" ) > 0 )
			throw new NotSupportedException( $"{name}: Modulate requires an unsupported runtime cloak/refraction pass while $cloakfactor is positive." );

		SurfaceTexture color = null;
		var path = parameters.String( "$basetexture" );
		if ( !string.IsNullOrWhiteSpace( path ) )
		{
			color = LoadSurfaceTexture( path, parameters.Integer( "$frame" ), animation );
			if ( color.IsCube )
				throw new InvalidDataException( $"{name}: $basetexture requires a 2D texture: '{path}'." );
		}

		var tint = parameters.Vector( "$color", Vector3.One ) * parameters.Vector( "$color2", Vector3.One );
		if ( !float.IsFinite( tint.x ) || !float.IsFinite( tint.y ) || !float.IsFinite( tint.z ) )
			throw new InvalidDataException( $"{name}: material modulation overflows." );
		var material = CreateShaderMaterial( "shaders/source1/modulate.shader", resourceName );
		animation.GetValueOrDefault().Bind( material, color );
		material.SetFeature( "F_SOURCE_MOD2X", parameters.Boolean( "$mod2x" ) ? 1 : 0 );
		material.SetFeature( "F_SOURCE_WRITEZ", parameters.Boolean( "$writez" ) ? 1 : 0 );
		material.SetFeature( "F_RENDER_BACKFACES", parameters.Boolean( "$nocull" ) ? 1 : 0 );
		material.SetFeature( "F_TRANSLUCENT", 1 );
		if ( color != null )
			color.Bind( material, "g_tColor" );
		else
			material.Set( "g_tColor", Texture.White );
		material.Set( "g_vSourceColorTint", tint );
		alpha.Bind( material );
		// Either flag enables the entire RGBA vertex modulation in this shader family.
		material.Set( "g_flSourceVertexColor", parameters.Boolean( "$vertexcolor" ) || parameters.Boolean( "$vertexalpha" ) );
		material.Set( "g_flSourceTextureAlphaPresent", color == null || color.HasAlpha );
		transforms.Get( "$basetexturetransform" ).Bind( material, "Base" );

		if ( cloakEnabled )
			Log.Trace( $"{name}: runtime cloaking is not implemented; rendering the inactive Modulate base pass." );
		return material;
	}
}
