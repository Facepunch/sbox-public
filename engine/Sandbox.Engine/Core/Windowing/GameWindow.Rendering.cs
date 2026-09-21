using NativeEngine;
using Sandbox.Utility;

namespace Sandbox.Engine;

internal sealed partial class GameWindow
{
	static readonly Superluminal gameRender = new( "Game Render", "#3a6e4d" );
	static readonly Superluminal menuRender = new( "Menu Render", "#6e3a6e" );

	// Scene views derive their dimensions and MSAA from this swapchain; scratch textures are pooled on demand.
	internal void InitializeRendering()
	{
		CSceneSystem.SetMainSwapChain( window.SwapChain );
		renderingInitialized = true;
	}

	internal static bool Present() => Current is not { } game || game.window.Present();

	internal void Render()
	{
		// All views join one batch so scene jobs overlap and we wait once at the end.
		CSceneSystem.BeginRenderingViews( true );
		try
		{
			Sandbox.UI.ScenePanel.RenderPending();

			using ( gameRender.Start() )
				IGameInstanceDll.Current?.OnRender( window.SwapChain );
			using ( menuRender.Start() )
				IMenuDll.Current?.OnRender( window.SwapChain );
		}
		finally
		{
			CSceneSystem.FinishRenderingViews();
			CSceneSystem.WaitForRenderingToComplete();
		}
	}

	/// <summary>
	/// Paint the startup background before showing the window or loading the game scene.
	/// </summary>
	void DrawStartupImage()
	{
		var size = window.SwapChainSize;
		var width = (int)size.x;
		var height = (int)size.y;
		var context = g_pRenderDevice.CreateRenderContext( 0 );
		IMaterial material = default;
		ITexture texture = default;

		try
		{
			material = MaterialSystem2.CreateRawMaterial( "_initial_window.vmat", "shaders/unlit.shader_c", true );
			texture = g_pResourceSystem.LoadTexture( "materials/startup_background.vtex" );

			context.BindRenderTargets( window.SwapChain, true, false );
			context.Clear( Vector4.Zero, true, false, false );
			context.SetViewport( 0, 0, width, height );

			g_pResourceSystem.UpdateSimple();
			MaterialSystem2.FrameUpdate();

			if ( material.IsValid && texture.IsStrongHandleValid() && !texture.IsError() )
			{
				material.Set( "g_tColor", texture );
				var desc = g_pRenderDevice.GetTextureDesc( texture );
				if ( desc.m_nWidth > 0 && desc.m_nHeight > 0 )
				{
					// The image is designed for a 1080p screen, centered at its original aspect ratio.
					var imageHeight = (int)(height * Math.Clamp( desc.m_nHeight / 1080.0f, 0, 1 ) + 0.5f);
					var imageWidth = imageHeight * desc.m_nWidth / desc.m_nHeight;
					MaterialSystem2Utils.DrawScreenSpaceRectangle( context, material, context.GetAttributesPtrForModify(),
						(width - imageWidth) / 2, (height - imageHeight) / 2, imageWidth, imageHeight,
						0, 0, desc.m_nWidth, desc.m_nHeight, desc.m_nWidth, desc.m_nHeight );
				}
			}

			context.Submit();
			window.Present();
		}
		finally
		{
			g_pRenderDevice.ReleaseRenderContext( context );
			if ( texture.IsValid ) texture.DestroyStrongHandle();
			if ( material.IsValid ) material.DestroyStrongHandle();
		}
	}
}
