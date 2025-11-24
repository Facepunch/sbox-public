using Sandbox.Engine;

namespace Sandbox;

public static partial class Graphics
{
	/// <summary>
	/// Called by the engine during pipeline. This could be rendering the scene from any camera.
	/// That means you can't assume this is the game view. This might be a tools view, or another view
	/// </summary>
	internal static void OnLayer( int stageenum, ManagedRenderSetup_t setup )
	{
		//
		// Special circumstances for the game UI
		//
		if ( stageenum == -1 )
		{
			using ( new Graphics.Scope( in setup ) )
			{
				RenderUiOverlay();
			}

			return;
		}

		Rendering.Stage renderStage = (Rendering.Stage)stageenum;

		// find our cameralur
		var cameraId = setup.sceneView.m_ManagedCameraId;
		if ( cameraId == 0 )
			return;

		var currentCamera = IManagedCamera.FindById( cameraId );
		if ( currentCamera is null )
			return;

		// Log.Info( $"cameraReference: \"{currentCamera}\" -> {renderStage}" );

		using ( new Graphics.Scope( in setup ) )
		{
			currentCamera.OnRenderStage( renderStage );
		}
	}

	static void RenderUiOverlay()
	{
		using var _ = IMenuDll.Current?.PushScope();

		Graphics.Attributes.SetCombo( "D_WORLDPANEL", 0 );

		for ( int i = GlobalContext.Current.UISystem.RootPanels.Count() - 1; i >= 0; i-- )
		{
			if ( GlobalContext.Current.UISystem.RootPanels[i].RenderedManually || GlobalContext.Current.UISystem.RootPanels[i].IsWorldPanel ) continue;

			GlobalContext.Current.UISystem.RootPanels[i].Render( 1 );
		}
	}
}
