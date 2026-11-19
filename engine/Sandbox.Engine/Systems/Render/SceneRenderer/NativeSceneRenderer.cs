using NativeEngine;
using Sandbox.VR;

namespace Sandbox.Rendering;

/// <summary>
/// Native's scene system renders the view: a <see cref="CameraRenderer"/> per call, configured from the camera, into
/// native's pipeline (<see cref="RenderPipeline"/>). This is what <see cref="SceneCamera"/>'s entry points did before
/// <see cref="ISceneRenderer"/>, moved as it was.
/// </summary>
internal sealed class NativeSceneRenderer : ISceneRenderer
{
	public void Render( SceneCamera camera, SwapChainHandle_t swapChain, Vector2? size, in ViewSetup setup )
	{
		if ( camera.WantsToRenderInStereo() )
		{
			RenderStereo( camera, setup );
		}
		else
		{
			RenderToSwapChain( camera, swapChain, size, setup );
		}
	}

	static void RenderToSwapChain( SceneCamera camera, SwapChainHandle_t swapChain, Vector2? size, in ViewSetup config )
	{
		if ( swapChain.self == default )
			return;

		var renderSize = size ?? camera.Size;

		if ( renderSize.x <= 0 ) return;
		if ( renderSize.y <= 0 ) return;

		camera.OnPreRender( renderSize );

		using var setup = new CameraRenderer( "RenderToSwapChain", camera._cameraId );
		setup.Configure( camera, config );
		setup.Native.Render( swapChain );
	}

	public void RenderToTexture( SceneCamera camera, Texture texture, Vector2? size, in ViewSetup config )
	{
		if ( texture is null || texture.native.IsNull )
			return;

		var renderSize = size ?? texture.Size;

		if ( renderSize.x <= 0 ) return;
		if ( renderSize.y <= 0 ) return;

		camera.ConfigureView( in config );

		using var setup = new CameraRenderer( "RenderToTexture", camera._cameraId );
		setup.Configure( camera, config );
		setup.Native.RenderToTexture( texture.native, Graphics.SceneView, default );
	}

	public bool RenderToTexture( SceneCamera camera, Texture target, Texture depthTarget, ISceneView parent, in ViewSetup config, string name, out ViewData viewData )
	{
		viewData = default;

		using var setup = new CameraRenderer( name, camera._cameraId );

		// The lock only covers reading the scene camera into this call's renderer. A top-level render
		// waits for the whole frame inside the native call, and a child view rendered through this
		// camera from the render thread needs the lock during that wait.
		lock ( camera )
		{
			setup.Configure( camera, config );
		}

		//
		// Adds the views to the scene system
		//
		var rendered = setup.Native.RenderToTexture( target.native, parent, depthTarget?.native ?? default );

		if ( rendered )
		{
			var depthRange = setup.Native.RenderedDepthRange;
			viewData = new ViewData
			{
				IsValid = true,
				ProjectionToView = setup.Native.RenderedProjectionToView,
				Viewport = setup.Native.RenderedViewport,
				DepthRange = new Vector2( depthRange.x, depthRange.y ),
				DepthUvScaleBias = new Vector4(
					setup.Native.FlipX ? -1 : 1, setup.Native.FlipY ? -1 : 1,
					setup.Native.FlipX ? 1 : 0, setup.Native.FlipY ? 1 : 0 )
			};
		}

		setup.Native.ClearSceneWorlds();

		return rendered;
	}

	public void RenderToBitmap( SceneCamera camera, Bitmap bitmap, in ViewSetup config )
	{
		if ( !bitmap.IsValid() )
			return;

		var renderSize = bitmap.Size;

		if ( renderSize.x <= 1 ) return;
		if ( renderSize.y <= 1 ) return;

		camera.OnPreRender( renderSize );

		using var setup = new CameraRenderer( "RenderToBitmap", camera._cameraId );
		setup.Configure( camera, config );

		// Game code can free the bitmap while we render, so render into memory we own
		var width = bitmap.Width;
		var height = bitmap.Height;
		var bytesPerPixel = bitmap.BytesPerPixel;
		var length = bitmap.GetBuffer().Length;

		unsafe
		{
			var pixels = System.Runtime.InteropServices.NativeMemory.AllocZeroed( (nuint)length );
			try
			{
				setup.Native.RenderToBitmap( (IntPtr)pixels, width, height, bytesPerPixel );

				if ( !bitmap.IsValid() || bitmap.Width != width || bitmap.Height != height || bitmap.BytesPerPixel != bytesPerPixel )
					return;

				new ReadOnlySpan<byte>( pixels, length ).CopyTo( bitmap.GetBuffer() );
			}
			finally
			{
				System.Runtime.InteropServices.NativeMemory.Free( pixels );
			}
		}
	}

	public void RenderToCube( SceneCamera camera, Texture texture, in ViewSetup config, CommandList afterFaces = null )
	{
		if ( texture is null || texture.native.IsNull )
			return;

		// Faces record in parallel but submit in array order, so the work after them goes in the last face's view, after its
		// post processing
		Action<Stage, SceneCamera> hook = null;
		if ( afterFaces is not null )
		{
			hook = ( stage, _ ) =>
			{
				if ( stage == Stage.AfterPostProcess && Graphics.SceneView.GetRenderTargetArrayIndex() == 5 )
					afterFaces.ExecuteOnRenderThread();
			};
			camera.OnRenderStageHook += hook;
		}

		try
		{
			RenderFaces( camera, texture, config );
		}
		finally
		{
			if ( hook is not null ) camera.OnRenderStageHook -= hook;
		}
	}

	static void RenderFaces( SceneCamera camera, Texture texture, in ViewSetup config )
	{
		if ( texture.Depth != 6 ) throw new Exception( "Expected a texture with 6 depth slices for RenderToCubeTexture" );

		var renderSize = texture.Size;

		if ( renderSize.x <= 0 ) return;
		if ( renderSize.y <= 0 ) return;

		camera.OnPreRender( renderSize );

		// Its faces are Source 2's even where the process draws on another device (an editor bake): read back from there
		if ( RenderBackend.Default is not Source2RenderBackend ) texture.PixelsOnSource2 = true;

		//
		// Adds the views to the scene system
		//
		using var setup = new CameraRenderer( "RenderToCubeTexture", camera._cameraId );
		setup.Configure( camera, config );
		setup.Native.EnableUI = false;

		// All six faces share one scene submission and one bindless descriptor set.
		// Join an existing frame bracket when the caller already has one open.
		bool ownsBatch = !CSceneSystem.IsRenderingViews();
		// Standalone captures preserve the main frame's performance stats.
		if ( ownsBatch ) CSceneSystem.BeginRenderingViews( false );
		try
		{
			for ( int i = 0; i < SceneCamera.CubeRotations.Length; i++ )
			{
				setup.Native.CameraRotation = (camera.Rotation * SceneCamera.CubeRotations[i]).Angles();
				setup.Native.RenderToCubeTexture( texture.native, i );
			}
		}
		finally
		{
			if ( ownsBatch )
			{
				CSceneSystem.FinishRenderingViews();
				CSceneSystem.WaitForRenderingToComplete();
			}
		}
	}

	static void RenderStereo( SceneCamera camera, in ViewSetup config )
	{
		if ( !camera.TargetEye.Contains( StereoTargetEye.LeftEye ) && !camera.TargetEye.Contains( StereoTargetEye.RightEye ) )
		{
			Log.Warning( $"Called {nameof( RenderStereo )} but neither eyes were present in {nameof( SceneCamera.TargetEye )}?" );
			return;
		}

		using var setup = new CameraRenderer( "RenderStereo", camera._cameraId );
		setup.Configure( camera, config );

		var n = setup.Native;

		for ( int iEye = 0; iEye < 2; ++iEye )
		{
			bool isLeftEye = iEye == 0;
			var stereoTargetEye = isLeftEye ? StereoTargetEye.LeftEye : StereoTargetEye.RightEye;
			var eye = isLeftEye ? VREye.Left : VREye.Right;

			// Check flags to see if we should render using this eye...
			if ( !camera.TargetEye.Contains( stereoTargetEye ) )
				continue;

			// PreRender
			camera.OnPreRender( VRSystem.EyeRenderTargetSize );

			// Save off clip planes, used for depth submit
			VRSystem.ClipPlanes.ZNear = camera.ZNear;
			VRSystem.ClipPlanes.ZFar = camera.ZFar;

			// Grab overrides for this eye
			var transform = VRSystem.GetTransformForEye( n.CameraPosition, n.CameraRotation, eye );
			n.CameraPosition = transform.Position;
			n.CameraRotation = transform.Rotation;

			// Save off middle eye position for things like skybox
			n.MiddleEyePosition = camera.Position;
			n.MiddleEyeRotation = camera.Rotation.Angles();

			n.OverrideProjection = VRSystem.GetProjectionMatrix( camera.ZNear, camera.ZFar, eye );
			n.HasOverrideProjection = true;

			n.FieldOfView = 0f; // Let clip bounds drive projection
			n.ClipSpaceBounds = VRSystem.GetClipForEye( eye );

			// Render
			var submitThisEye = camera.WantsStereoSubmit && eye == VREye.Right;
			n.RenderStereo( iEye, (int)VRSystem.EyeRenderTargetSize.x, (int)VRSystem.EyeRenderTargetSize.y, submitThisEye );
		}

		VRSystem.IsRendering = camera.WantsStereoSubmit;

		// Done here, clean up / reset overrides
		n.HasOverrideProjection = false;
		n.ClearSceneWorlds();
	}

	// Native keeps nothing per camera or world past a call: each one makes and deletes its own CCameraRenderer
	public void Forget( SceneCamera camera ) { }
	public void Forget( SceneWorld world ) { }
	public void Shutdown() { }
	public void WarmUp() { }
}
