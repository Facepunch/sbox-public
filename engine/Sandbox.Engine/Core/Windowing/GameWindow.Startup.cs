using NativeEngine;
using Sandbox.Utility;

namespace Sandbox.Engine;

internal sealed partial class GameWindow
{
	float startupProgress = -1;
	string startupStatus = string.Empty;
	bool startupFinished;
	internal void UpdateStartupProgress( float progress, string status )
	{
		ThreadSafe.AssertIsMainThread();
		if ( startupFinished || CommandLine.HasSwitch( "-nowindow" ) ) return;

		progress = Math.Clamp( progress, 0, 1 );
		status ??= startupStatus;
		if ( progress < startupProgress || (progress == startupProgress && status == startupStatus) ) return;

		startupProgress = progress;
		startupStatus = status;
		if ( startupWindow is { IsOpen: true } softwareSplash )
		{
			softwareSplash.UpdateProgress( progress, status );
			return;
		}

		try
		{
			DrawStartupImage( progress );
		}
		catch ( Exception e )
		{
			startupFinished = true;
			Log.Warning( e, "Couldn't draw startup image" );
		}
	}


	/// <summary>
	/// Paint the startup background and a small progress line without loading the UI system.
	/// </summary>
	void DrawStartupImage( float progress )
	{
		var size = window.SwapChainSize;
		var width = (int)size.x;
		var height = (int)size.y;
		if ( width <= 0 || height <= 0 )
			return;

		using var statusBitmap = GameStartupWindow.CreateStatusBitmap( startupStatus, width - 1, height / 1080.0f );
		using var statusTexture = statusBitmap?.ToTexture( mips: false );
		var context = g_pRenderDevice.CreateRenderContext( 0 );
		IMaterial material = default;
		ITexture texture = default;
		ITexture backdrop = default;
		try
		{
			material = MaterialSystem2.CreateRawMaterial( "_initial_window.vmat", "shaders/unlit.shader_c", true );
			texture = g_pResourceSystem.LoadTexture( "materials/startup_background.vtex" );
			if ( !Application.IsStandalone )
			{
				backdrop = g_pResourceSystem.LoadTexture( "materials/startup_backdrop.vtex" );
			}

			context.BindRenderTargets( window.SwapChain, true, false );
			context.SetViewport( 0, 0, width, height );
			context.Clear( Vector4.Zero, true, false, false );

			g_pResourceSystem.UpdateSimple();
			MaterialSystem2.FrameUpdate();

			var attributes = context.GetAttributesPtrForModify();

			if ( material.IsValid && backdrop.IsStrongHandleValid() && !backdrop.IsError() )
			{
				DrawStartupBackdrop( context, material, attributes, backdrop, width, height );
			}

			if ( material.IsValid && texture.IsStrongHandleValid() && !texture.IsError() )
			{
				DrawStartupLogo( context, material, attributes, texture, width, height );
			}

			if ( material.IsValid && statusBitmap is not null && statusTexture is { IsValid: true } )
			{
				DrawStartupStatus( context, material, attributes, statusTexture.native, statusBitmap.Width, statusBitmap.Height, width, height );
			}
			DrawStartupProgress( context, width, height, progress );

			context.Submit();
			window.Present();
		}
		finally
		{
			g_pRenderDevice.ReleaseRenderContext( context );

			if ( texture.IsValid )
				texture.DestroyStrongHandle();

			if ( backdrop.IsValid )
				backdrop.DestroyStrongHandle();

			if ( material.IsValid )
				material.DestroyStrongHandle();
		}
	}

	/// <summary>
	/// Center the logo at its native pixel size, shrinking it only when needed to fit the window.
	/// Standalone games retain resolution-based scaling for their custom splash images.
	/// </summary>
	static void DrawStartupLogo( IRenderContext context, IMaterial material, CRenderAttributes attributes, ITexture texture, int width, int height )
	{
		var desc = g_pRenderDevice.GetTextureDesc( texture );
		if ( desc.m_nWidth <= 0 || desc.m_nHeight <= 0 )
			return;

		// Exclude the padding added by the texture compiler for mipmaps.
		var sourceWidth = desc.m_nDisplayRectWidth > 0 ? desc.m_nDisplayRectWidth : desc.m_nWidth;
		var sourceHeight = desc.m_nDisplayRectHeight > 0 ? desc.m_nDisplayRectHeight : desc.m_nHeight;
		var scale = Application.IsStandalone ? height / 1080.0f : 1.0f;
		scale = Math.Min( scale, Math.Min( width / (float)sourceWidth, height / (float)sourceHeight ) );
		var imageWidth = Math.Max( 1, (int)(sourceWidth * scale + 0.5f) );
		var imageHeight = Math.Max( 1, (int)(sourceHeight * scale + 0.5f) );

		material.Set( "g_tColor", texture );
		MaterialSystem2Utils.DrawScreenSpaceRectangle( context, material, attributes,
			(width - imageWidth) / 2, (height - imageHeight) / 2, imageWidth, imageHeight,
			0, 0, sourceWidth - 1, sourceHeight - 1, desc.m_nWidth, desc.m_nHeight );
	}
	static void DrawStartupStatus( IRenderContext context, IMaterial material, CRenderAttributes attributes, ITexture texture, int labelWidth, int labelHeight, int width, int height )
	{
		var scale = height / 1080.0f;
		var barHeight = Math.Clamp( (int)(4 * scale), 1, height );
		var barY = Math.Max( 0, height - (int)(48 * scale) - barHeight );
		var x = (width - labelWidth) / 2;
		var y = Math.Max( 0, barY - labelHeight - Math.Max( 4, (int)(10 * scale) ) );

		attributes.SetComboValue( "F_TRANSLUCENT", (byte)1 );
		material.Set( "g_tColor", texture );
		MaterialSystem2Utils.DrawScreenSpaceRectangle( context, material, attributes,
			x, y, labelWidth, labelHeight,
			0, 0, labelWidth - 1, labelHeight - 1, labelWidth, labelHeight );
	}


	/// <summary>
	/// Draw a simple progress track and solid red fill using viewport clears.
	/// </summary>
	static void DrawStartupProgress( IRenderContext context, int width, int height, float progress )
	{
		var scale = height / 1080.0f;
		var barWidth = Math.Clamp( (int)(320 * scale), 1, width );
		var barHeight = Math.Clamp( (int)(4 * scale), 1, height );
		var barX = (width - barWidth) / 2;
		var barY = Math.Max( 0, height - (int)(48 * scale) - barHeight );

		// Clear is restricted to the viewport, so the bar needs no material or texture.
		context.SetViewport( barX, barY, barWidth, barHeight );
		context.Clear( new Vector4( 0.05f, 0.065f, 0.09f, 1 ), true, false, false );

		var filledWidth = (int)(barWidth * progress);
		if ( filledWidth > 0 )
		{
			context.SetViewport( barX, barY, filledWidth, barHeight );
			context.Clear( new Vector4( 0.88f, 0.009f, 0.015f, 1 ), true, false, false );
		}

		context.SetViewport( 0, 0, width, height );
	}

	/// <summary>
	/// Fill the window with the backdrop, cropping from the center to preserve its aspect ratio.
	/// </summary>
	static void DrawStartupBackdrop( IRenderContext context, IMaterial material, CRenderAttributes attributes, ITexture texture, int width, int height )
	{
		var desc = g_pRenderDevice.GetTextureDesc( texture );
		if ( desc.m_nWidth <= 0 || desc.m_nHeight <= 0 )
			return;

		var sourceWidth = desc.m_nDisplayRectWidth > 0 ? desc.m_nDisplayRectWidth : desc.m_nWidth;
		var sourceHeight = desc.m_nDisplayRectHeight > 0 ? desc.m_nDisplayRectHeight : desc.m_nHeight;
		var scale = Math.Max( width / (float)sourceWidth, height / (float)sourceHeight );
		var sourceX = (sourceWidth - width / scale) * 0.5f;
		var sourceY = (sourceHeight - height / scale) * 0.5f;

		material.Set( "g_tColor", texture );
		MaterialSystem2Utils.DrawScreenSpaceRectangle( context, material, attributes,
			0, 0, width, height,
			sourceX, sourceY, sourceWidth - sourceX - 1, sourceHeight - sourceY - 1, desc.m_nWidth, desc.m_nHeight );
	}
}
