using NativeEngine;

namespace Sandbox;

public static partial class Graphics
{
	/// <summary>
	/// A frame drawn outside native's scene views, by the managed scene renderer (Sandbox.SceneRenderer, with
	/// <c>r_managed_scene</c>). A camera's command lists, post processing and UI run against it at native's stages, and ask
	/// it (<see cref="IFrameView"/>) what they'd ask a native view: the targets being drawn into, the viewport, the camera
	/// and its frustum, and whether post processing is on.
	/// </summary>
	internal sealed class ManagedView : IFrameView, IDisposable
	{
		/// <summary>
		/// The colour and depth the stage draws into - what a native hook layer's render target outputs are. Either can
		/// be null.
		/// </summary>
		public ITexture Color;
		public ITexture Depth;

		/// <summary>
		/// The array slice or cube face of <see cref="Depth"/> drawn into when there's no colour - a shadow view's.
		/// </summary>
		public int DepthSlice;

		/// <summary>
		/// Whether colour is written through an sRGB view, as a layer's colour space says for native.
		/// </summary>
		public bool SrgbWrite = true;

		/// <summary>
		/// The stage's viewport, in the targets' pixels, with its depth range.
		/// </summary>
		public Rect Viewport;
		public float MinZ;
		public float MaxZ = 1;

		/// <summary>
		/// The camera, as the view's frustum - set it up with <see cref="SetCamera"/>.
		/// </summary>
		public CFrustum Frustum { get; } = CFrustum.Create();

		/// <summary>
		/// The colour target's format and sample count, which <see cref="Graphics.IdealColorFormat"/> and
		/// <see cref="Graphics.IdealMsaaLevel"/> give temporary targets.
		/// </summary>
		public ImageFormat ColorFormat = ImageFormat.RGBA16161616F;
		public MultisampleAmount Msaa;

		/// <summary>
		/// The block records into an async compute context: compute only, so leave <see cref="Color"/> and <see cref="Depth"/>
		/// unset, and barriers name compute stages (<see cref="Graphics.OnComputeQueue"/>).
		/// </summary>
		public bool ComputeQueue;

		/// <summary>
		/// What <c>ISceneView.GetPostProcessEnabled</c> and <c>GetToolsVisMode</c> say for a native view.
		/// </summary>
		public bool PostProcessEnabled = true;
		public int ToolsVisMode;

		/// <summary>
		/// The kind of layer the stage is, which drawing code may check.
		/// </summary>
		public SceneLayerType LayerType = SceneLayerType.Translucent;

		/// <summary>
		/// The shader mode materials draw with, as a native layer's is: <c>Forward</c> for native's hook layers
		/// (<c>CManagedRenderPipeline::OnHook</c>) and its forward layers. A shader without it draws with its default mode.
		/// </summary>
		public StringToken ShaderMode = new( "Forward" );

		/// <summary>
		/// Put an object's own lighting into attributes - its light probe volume - as native's
		/// <c>CSceneSystem::SetupPerObjectLighting</c> does from its layer's view. Without one, it gets none of its own.
		/// </summary>
		public Action<SceneObject, RenderAttributes> SetupLighting;

		/// <summary>
		/// Draw a scene object with a transform, colour, material override and attributes into what's bound - what
		/// <see cref="Graphics.Render(SceneObject, Transform?, Color?, Material)"/> does, which native draws through its
		/// layer's view (<c>RenderTools::DrawSceneObject</c>). The managed renderer draws it from its own copy of the object.
		/// Without one, it draws nothing.
		/// </summary>
		public Action<SceneObject, Transform, Color, Material, RenderAttributes> RenderSceneObject;

		/// <summary>
		/// Point the frustum where a camera is, as <c>CCameraRenderer</c> sets up its view's.
		/// </summary>
		public void SetCamera( Vector3 position, Rotation rotation, float fieldOfView, float zNear, float zFar, float aspect, bool orthographic, float orthoWidth, float orthoHeight )
		{
			if ( orthographic ) Frustum.InitOrthoCamera( position, rotation.Angles(), zNear, zFar, orthoWidth, orthoHeight );
			else Frustum.InitCamera( position, rotation.Angles(), zNear, zFar, fieldOfView, aspect );
		}

		/// <summary>
		/// Bind the targets and set the viewport - what a stage starts with, and what setting
		/// <see cref="Graphics.RenderTarget"/> back to null returns to.
		/// </summary>
		internal void Bind( IRenderContext context )
		{
			if ( Color.IsNull && !Depth.IsNull ) RenderTools.BindDepthTarget( context, Depth, DepthSlice );
			else RenderTools.BindColorAndDepthTarget( context, Color, Depth, SrgbWrite );
			context.SetViewport( new RenderViewport( Viewport, MinZ, MaxZ ) );
		}

		public void Dispose()
		{
			Frustum.Delete();
		}

		bool IFrameView.PostProcessEnabled => PostProcessEnabled;
		int IFrameView.ToolsVisMode => ToolsVisMode;

		// A managed frame is always a camera's own
		bool IFrameView.IsChildView => false;

		// The renderer owns its targets; callers don't destroy them
		ITexture IFrameView.GetColorTarget( out bool owned )
		{
			owned = false;
			return Color;
		}

		ITexture IFrameView.GetDepthTarget( out bool owned )
		{
			owned = false;
			return Depth;
		}

		IMaterialMode IFrameView.ModeFor( Material material )
		{
			// A shader without the mode draws with its default one, as a native layer's lookup falls back
			var mode = material.native.GetMode( ShaderMode );
			return mode.IsNull ? material.native.GetMode() : mode;
		}

		StringToken IFrameView.DrawShaderMode => ShaderMode;

		void IFrameView.SetupLighting( SceneObject obj, RenderAttributes attributes ) => SetupLighting?.Invoke( obj, attributes );

		void IFrameView.RenderSceneObject( IRenderContext context, SceneObject obj, Transform transform, Color color, Material material, RenderAttributes attributes )
		{
			RenderSceneObject?.Invoke( obj, transform, color, material, attributes );
		}

		void IFrameView.BindTarget( IRenderContext context, RenderTarget target )
		{
			// A target of only depth draws into the frame's colour, as native's layer binding assumes (BindRenderTargets)
			var color = target.ColorTarget?.native ?? Color;
			RenderTools.BindColorAndDepthTarget( context, color, target.DepthTarget?.native ?? default, SrgbWrite );
		}

		// Its own depth range, not the one saved before the target was bound
		void IFrameView.RestoreTargets( IRenderContext context, float minZ, float maxZ ) => Bind( context );
	}
}
