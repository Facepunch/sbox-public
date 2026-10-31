using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceMaterialBuilder
{
	private void BindWorldTransition( Material material, SurfaceTexture color, SurfaceTexture normal )
	{
		var path = parameters.String( "$basetexture2" );
		if ( string.IsNullOrWhiteSpace( path ) )
			throw new InvalidDataException( $"{FullPath}: WorldVertexTransition requires $basetexture2." );
		var color2 = LoadSurfaceTexture( path, parameters.Integer( "$frame2" ) );
		var normal2 = Load( "$bumpmap2", "$bumpframe2" );
		var blend = Load( "$blendmodulatetexture" );
		color2.Bind( material, "g_tColor2" );
		normal2?.Bind( material, "g_tNormal2" );
		blend?.Bind( material, "g_tBlendModulate" );
		material.Set( "g_flSourceHasNormal2", normal2 is not null );
		material.Set( "g_flSourceHasBlendModulate", blend is not null );
		material.Set( "g_nSourceNormal2Encoding", normal2?.Header.HiResFormat == VtfFormat.UV88 ? 1 : 0 );
		material.Set( "g_flSourceSsBump2", parameters.Boolean( "$ssbump2", parameters.Boolean( "$ssbump" ) ) || IsSsBump( normal2 ) );
		material.Set( "g_flSourceNormal2Scale", parameters.Float( "$bumpstrength2", parameters.Float( "$bumpstrength", 1 ) ) );
		material.Set( "g_flSourceBlendFactor", parameters.Float( "$blendfactor", 1 ) );
		var alphaTest = material.GetFeature( "F_ALPHA_TEST" ) != 0;
		material.Set( "g_vSourceTextureAlphaPresence", new Vector4(
			alphaTest ? 1 : Alpha( color ), alphaTest ? 1 : Alpha( color2 ), Alpha( normal ), Alpha( normal2 ) ) );
		var baseTransform = transforms.Get( "$basetexturetransform" );
		transforms.Get( "$basetexturetransform2", baseTransform ).Bind( material, "Base2" );
		transforms.Get( "$bumptransform2", transforms.Get( "$bumptransform", baseTransform ) ).Bind( material, "Normal2" );
		transforms.Get( "$blendmasktransform", baseTransform ).Bind( material, "Blend" );

		SurfaceTexture Load( string key, string frame = null )
		{
			var texture = parameters.String( key );
			return string.IsNullOrWhiteSpace( texture ) ? null
				: LoadSurfaceTexture( texture, frame is null ? 0 : parameters.Integer( frame ) );
		}
	}
}
