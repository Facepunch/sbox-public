using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.SceneRenderer.Native;

/// <summary>
/// The view attributes and debug text native's pipelines used to set up before adding layers: the lighting lookup textures,
/// placeholders older shaders still bind, the lightmap warnings and the shader ID legend.
/// </summary>
internal static class ViewSetup
{
	static readonly Texture BlueNoise = Texture.Load( "textures/dev/blue_noise_256.vtex" );
	static readonly Texture BrdfLookup = Texture.Load( "textures/dev/ggx_integrate_brdf_lut_schlick.vtex" );
	static readonly Texture LtcAmplitude = Texture.Load( "textures/dev/ltc_amp.vtex" );
	static readonly Texture LtcMatrix = Texture.Load( "textures/dev/ltc_mat.vtex" );

	static readonly StringToken BlueNoiseToken = "BlueNoise";
	static readonly StringToken BrdfLookupToken = "BRDFLookup";
	static readonly StringToken LtcAmplitudeToken = "LtcAmplitudeLookup";
	static readonly StringToken LtcMatrixToken = "LtcMatrixLookup";
	static readonly StringToken FogVolume = "FogVolume";
	static readonly StringToken AmbientColor = "ambientColor";
	static readonly StringToken ToolsAmbientLighting = "ToolsAmbientLighting";
	static readonly StringToken DynamicAmbientOcclusion = "DynamicAmbientOcclusion";
	static readonly StringToken DynamicAmbientOcclusionDepth = "DynamicAmbientOcclusionDepth";
	static readonly StringToken ReflectionTextureColor = "ReflectionTextureColor";
	static readonly StringToken InvalidLightMaps = "invalidLightMaps";
	static readonly StringToken PreviewLightMaps = "previewLightMaps";
	static readonly StringToken RenderTime = "renderTime";
	static readonly StringToken DisableScreenSpaceEffects = "DisableScreenSpaceEffects";

	const string LightmapsOutOfDate = "Lightmaps are out of date !!! - Please re-bake. Rendering as FULLBRIGHT";
	const string PreviewLighting = "Preview Lighting - Not indicative of shipping quality or performance. Please bake lightmaps.";

	/// <summary>
	/// Shaders the shader ID legend leaves out: compute, debug, tools, UI and post processing.
	/// </summary>
	static readonly string[] LegendExcluded = ["cs", "visualize", "depth", "tools", "debug", "error", "ui", "post", "query", "texture"];

	/// <summary>
	/// The BRDF, blue noise and LTC lookups every lit shader reads.
	/// </summary>
	public static void SetLookups( CRenderAttributes viewAttributes )
	{
		viewAttributes.SetTextureValue( BlueNoiseToken, BlueNoise.native, -1 );
		viewAttributes.SetTextureValue( BrdfLookupToken, BrdfLookup.native, -1 );
		viewAttributes.SetTextureValue( LtcAmplitudeToken, LtcAmplitude.native, -1 );
		viewAttributes.SetTextureValue( LtcMatrixToken, LtcMatrix.native, -1 );
	}

	/// <summary>
	/// The main view's: lookups, the ambient colour tools shaders read, and placeholders for textures older shaders still bind.
	/// </summary>
	public static void SetStandard( CRenderPipelineSetup setup )
	{
		var viewAttributes = setup.m_pView.GetRenderAttributesPtr();
		SetLookups( viewAttributes );

		// AO and SSR slots are frame-global. Child views have different screen coordinates
		// and cannot consume the parent's results, even when their post processing is disabled.
		viewAttributes.SetBoolValue( DisableScreenSpaceEffects, setup.m_pView.GetParent().IsValid );

		var ambient = viewAttributes.GetVector4DValue( AmbientColor, setup.m_pAttributes.GetVector4DValue( AmbientColor, default ) );
		viewAttributes.SetVector4DValue( ToolsAmbientLighting, ambient );

		// Unused but referenced in old shaders
		viewAttributes.SetTextureValue( DynamicAmbientOcclusion, CSceneSystem.GetWellKnownTexture( SceneSystemWellKnownTextureObjectID.SCENE_TXTR_WHITE_1x1 ), -1 );
		viewAttributes.SetTextureValue( DynamicAmbientOcclusionDepth, CSceneSystem.GetWellKnownTexture( SceneSystemWellKnownTextureObjectID.SCENE_TXTR_ONE_16_BIT_1x1 ), -1 );
		viewAttributes.SetTextureValue( ReflectionTextureColor, CSceneSystem.GetWellKnownTexture( SceneSystemWellKnownTextureObjectID.SCENE_TXTR_TRANSPARENT_BLACK_1x1 ), -1 );
	}

	/// <summary>
	/// A 3D skybox view's: lookups, and the parent's fog volume.
	/// </summary>
	public static void SetSkybox3D( CRenderPipelineSetup setup )
	{
		var viewAttributes = setup.m_pView.GetRenderAttributesPtr();
		SetLookups( viewAttributes );
		viewAttributes.SetBoolValue( DisableScreenSpaceEffects, true );

		var black = CSceneSystem.GetWellKnownTexture( SceneSystemWellKnownTextureObjectID.SCENE_TXTR_OPAQUE_BLACK_2x2x2 );
		viewAttributes.SetTextureValue( FogVolume, setup.m_pAttributes.GetTextureValue( FogVolume, black ), -1 );
	}

	/// <summary>
	/// Warn across the top of the view when its lightmaps are out of date (flashing red) or it's previewing lighting.
	/// </summary>
	public static void AddLightmapWarnings( CRenderPipelineSetup setup )
	{
		var view = setup.m_pView;
		var attributes = setup.m_pAttributes;
		var width = (int)setup.m_skyViewport.Rect.Width;

		if ( attributes.GetBoolValue( InvalidLightMaps, false ) )
		{
			var degrees = attributes.GetFloatValue( RenderTime, 1.0f ) * 5.0f;
			var red = (int)(MathF.Sin( degrees * MathF.PI / 180.0f ) * 63 + 192);
			PrintWithBorder( view, LightmapsOutOfDate, CentredX( width, LightmapsOutOfDate ), 30, red, 0, 0 );
		}
		else if ( attributes.GetBoolValue( PreviewLightMaps, false ) )
		{
			PrintWithBorder( view, PreviewLighting, CentredX( width, PreviewLighting ), 30, 255, 255, 0 );
			view.GetRenderAttributesPtr().SetBoolValue( PreviewLightMaps, true );
		}
	}

	/// <summary>
	/// The shader ID visualization's legend: every loaded shader's name in its colour, on a black panel at the left.
	/// </summary>
	public static void AddShaderIDLegend( CRenderPipelineSetup setup, SceneViewRenderTargetHandle color )
	{
		var view = setup.m_pView;
		var startY = (int)setup.m_skyViewport.Rect.Height / 10;
		var y = startY;

		var count = RenderPipelineTools.CollectLoadedShaderNames();
		for ( int i = 0; i < count; i++ )
		{
			var name = RenderPipelineTools.GetLoadedShaderName( i );
			if ( IsExcludedFromLegend( name ) ) continue;

			var shaderColor = RenderPipelineTools.GetShaderIDColor( name );
			PrintWithBorder( view, name, 16, y, (int)(shaderColor.x * 255), (int)(shaderColor.y * 255), (int)(shaderColor.z * 255) );
			y += 12;
		}

		var black = CSceneSystem.GetWellKnownTexture( SceneSystemWellKnownTextureObjectID.SCENE_TXTR_BLACK_1x1 );
		RenderPipelineTools.AddTextureDebugLayer( view, "BackgroundForShaderIDs", color, new RenderViewport( 6, startY - 6, 270, y - startY + 6 ), black );
	}

	static bool IsExcludedFromLegend( string shaderName )
	{
		foreach ( var token in LegendExcluded )
		{
			if ( shaderName.Contains( token, StringComparison.Ordinal ) ) return true;
		}

		return false;
	}

	/// <summary>
	/// Text 7 pixels a character wide (and its terminator, as native counted it), centred.
	/// </summary>
	static int CentredX( int width, string text ) => (width - (text.Length + 1) * 7) / 2;

	/// <summary>
	/// Text with a one pixel black border, readable over anything.
	/// </summary>
	static void PrintWithBorder( ISceneView view, string text, int x, int y, int r, int g, int b )
	{
		for ( int i = 0; i < 9; i++ )
		{
			if ( i == 4 ) continue;
			RenderPipelineTools.ScreenText( view, x + i % 3, y + i / 3, text, 0, 0, 0, 255 );
		}

		RenderPipelineTools.ScreenText( view, x + 1, y + 1, text, r, g, b, 255 );
	}
}
