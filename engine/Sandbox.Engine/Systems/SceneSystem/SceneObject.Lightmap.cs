using NativeEngine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// The textures a <see cref="BakedLightmap"/> bake lights an object from.
/// </summary>
/// <param name="Indices">The bake index of the static light in each of a luxel's four channels.</param>
/// <param name="Strengths">How much of each of those lights reaches the luxel, square rooted.</param>
/// <param name="Irradiance">Indirect light at each luxel.</param>
/// <param name="Direction">Where that light comes from in the surface's tangent space, how much of it is ambient, and baked occlusion.</param>
public sealed record LightmapTextures( Texture Indices, Texture Strengths, Texture Irradiance, Texture Direction );

public partial class SceneObject
{
	// CWorldRendererBakedLightingInfo::Init
	[StructLayout( LayoutKind.Sequential )]
	unsafe struct BakedLightingConstants
	{
		public Vector4 UvScale;
		public fixed int Textures[8];
	}

	// What every object lit from the same textures shares: one constant buffer, and the attributes that bind it
	sealed unsafe class BakedLighting
	{
		public readonly RenderAttributes Attributes = new();
		readonly IntPtr _constants = RenderTools.CreateConstantBuffer( sizeof( BakedLightingConstants ) );

		public BakedLighting( Texture[] textures )
		{
			var constants = new BakedLightingConstants { UvScale = new Vector4( 1.0f / textures[2].Width, 1.0f / textures[2].Height, 0, 0 ) };
			for ( int i = 0; i < textures.Length; i++ )
				constants.Textures[i] = g_pRenderDevice.GetTextureViewIndex( textures[i].native, 0, RenderTextureDimension.RENDER_TEXTURE_DIMENSION_2D_ARRAY );

			var context = g_pRenderDevice.CreateRenderContext( 0 );
			RenderTools.SetConstantBufferData( context, Attributes.Get(), "BakedLightingConstantBuffer_t", _constants, (IntPtr)(&constants), sizeof( BakedLightingConstants ) );
			context.Submit();
			g_pRenderDevice.ReleaseRenderContext( context );

			Attributes.Set( "UsesBakedLightmaps", true );
			Attributes.SetCombo( "D_BAKED_LIGHTING_FROM_LIGHTMAP", true );
			Attributes.Set( "UsesBakedLightingFromProbe", false );
			Attributes.SetCombo( "D_BAKED_LIGHTING_FROM_PROBE", false );
		}

		~BakedLighting()
		{
			var constants = _constants;
			MainThread.Queue( () => RenderTools.DestroyConstantBuffer( constants ) );
		}
	}

	static readonly ConditionalWeakTable<LightmapTextures, BakedLighting> bakedLighting = new();

	// Its attributes bind this one's constant buffer, so it's kept while they do
	BakedLighting _lightmap;

	/// <summary>
	/// Light this object from lightmaps, as <c>RenderTools::SetSceneObjectLightmapAttributes</c> lights a map's. Its
	/// mesh needs lightmap coordinates in luxels. Pass nothing to light it normally again.
	/// </summary>
	internal void SetLightmap( LightmapTextures lightmap = null )
	{
		if ( native.IsNull ) return;

		Texture[] textures = lightmap is null ? [] : [lightmap.Indices, lightmap.Strengths, lightmap.Irradiance, lightmap.Direction];
		var lit = textures.Length > 0 && textures.All( x => x.IsValid() && !x.IsError );

		if ( !lit )
		{
			// Attributes of its own would stop an object that was never lightmapped batching
			if ( CreatedAttributes?.GetBool( "UsesBakedLightmaps" ) != true ) return;

			// Deleted, not turned off: left set they'd hide a probe volume's, which lights it again
			var attributes = Attributes.Get();
			attributes.DeleteBoolValue( "UsesBakedLightmaps" );
			attributes.DeleteComboValue( "D_BAKED_LIGHTING_FROM_LIGHTMAP" );
			attributes.DeleteBoolValue( "UsesBakedLightingFromProbe" );
			attributes.DeleteComboValue( "D_BAKED_LIGHTING_FROM_PROBE" );
			attributes.DeleteConstantBufferValue( "BakedLightingConstantBuffer_t" );
			_lightmap = null;
			UpdateFlagsBasedOnMaterial();
			return;
		}

		_lightmap = bakedLighting.GetValue( lightmap, _ => new BakedLighting( textures ) );
		_lightmap.Attributes.MergeTo( Attributes );

		// A probe volume around it would light it instead. Setting its model decides this again
		Flags.NeedsLightProbe = false;
	}
}
