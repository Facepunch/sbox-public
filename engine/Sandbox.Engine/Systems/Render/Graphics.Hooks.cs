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
				DebugOverlay.Render();
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
		if ( Application.IsStandalone )
			return;

		using var _ = GlobalContext.MenuScope();
		GlobalContext.Current.UISystem.Render();
	}
}
