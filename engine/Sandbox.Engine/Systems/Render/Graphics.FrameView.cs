using NativeEngine;

namespace Sandbox;

public static partial class Graphics
{
	/// <summary>
	/// The frame a render block draws into: a native scene view and layer (<see cref="NativeFrameView"/>), or a frame the
	/// managed scene renderer draws (<see cref="ManagedView"/>). <see cref="Graphics"/>, command lists, post processing and
	/// UI ask it what they'd otherwise ask a native view or layer, so neither renderer has to look like the other. A
	/// standalone block (<see cref="Scope.Create"/>) has none.
	/// </summary>
	internal interface IFrameView
	{
		/// <summary>
		/// The camera, as the view's frustum.
		/// </summary>
		CFrustum Frustum { get; }

		/// <summary>
		/// Whether the view has post processing on (<c>ISceneView.GetPostProcessEnabled</c>).
		/// </summary>
		bool PostProcessEnabled { get; }

		/// <summary>
		/// The view's tools visualisation mode (<c>ISceneView.GetToolsVisMode</c>).
		/// </summary>
		int ToolsVisMode { get; }

		/// <summary>
		/// Whether the view is another's child - a reflection or a render to texture from inside a view, which don't render
		/// views of their own.
		/// </summary>
		bool IsChildView { get; }

		/// <summary>
		/// The colour being drawn into. When <paramref name="owned"/>, it's a strong handle the caller destroys.
		/// </summary>
		ITexture GetColorTarget( out bool owned );

		/// <summary>
		/// The depth being drawn into, like <see cref="GetColorTarget"/>.
		/// </summary>
		ITexture GetDepthTarget( out bool owned );

		/// <summary>
		/// The mode a material draws with here.
		/// </summary>
		IMaterialMode ModeFor( Material material );

		/// <summary>
		/// The shader mode <c>RenderTools.DrawModel</c> draws with when it has no native layer to take one from.
		/// </summary>
		StringToken DrawShaderMode { get; }

		/// <summary>
		/// Put an object's own lighting - its light probe volume - into attributes, as
		/// <c>CSceneSystem::SetupPerObjectLighting</c> does from a layer's view.
		/// </summary>
		void SetupLighting( SceneObject obj, RenderAttributes attributes );

		/// <summary>
		/// Draw a scene object with a transform, colour, material override and attributes into what's bound
		/// (<see cref="Graphics.Render(SceneObject, Transform?, Color?, Material)"/>).
		/// </summary>
		void RenderSceneObject( IRenderContext context, SceneObject obj, Transform transform, Color color, Material material, RenderAttributes attributes );

		/// <summary>
		/// Bind a render target's colour and depth in place of the frame's (<see cref="Graphics.RenderTarget"/>).
		/// </summary>
		void BindTarget( IRenderContext context, RenderTarget target );

		/// <summary>
		/// Go back to the frame's own targets and viewport, with the depth range the frame had before a render target was
		/// bound.
		/// </summary>
		void RestoreTargets( IRenderContext context, float minZ, float maxZ );
	}

	/// <summary>
	/// A native scene view and the layer being drawn - a hook layer (<c>CManagedRenderLayer</c>) or a scene object's
	/// draw inside one. Pooled: a scope takes one and returns it.
	/// </summary>
	internal sealed class NativeFrameView : IFrameView
	{
		/// <summary>
		/// The native view and layer.
		/// </summary>
		public ISceneView View { get; private set; }
		public ISceneLayer Layer { get; private set; }

		/// <summary>
		/// One from the pool, for a view and layer.
		/// </summary>
		public static NativeFrameView Get( ISceneView view, ISceneLayer layer )
		{
			var frame = ObjectPool<NativeFrameView>.Get();
			frame.View = view;
			frame.Layer = layer;
			return frame;
		}

		/// <summary>
		/// Back to the pool.
		/// </summary>
		public void Return()
		{
			View = default;
			Layer = default;
			ObjectPool<NativeFrameView>.Return( this );
		}

		public CFrustum Frustum => View.GetFrustum();
		public bool PostProcessEnabled => View.GetPostProcessEnabled();
		public int ToolsVisMode => View.GetToolsVisMode();
		public bool IsChildView => View.GetParent().IsValid;

		public ITexture GetColorTarget( out bool owned )
		{
			owned = true;
			return Layer.GetColorTarget();
		}

		public ITexture GetDepthTarget( out bool owned )
		{
			owned = true;
			return Layer.GetDepthTarget();
		}

		public IMaterialMode ModeFor( Material material ) => material.native.GetMode( Layer );

		// The layer gives the mode
		public StringToken DrawShaderMode => default;

		public void SetupLighting( SceneObject obj, RenderAttributes attributes )
		{
			CSceneSystem.SetupPerObjectLighting( attributes.Get(), obj, Layer );
		}

		public void RenderSceneObject( IRenderContext context, SceneObject obj, Transform transform, Color color, Material material, RenderAttributes attributes )
		{
			RenderTools.DrawSceneObject( context, Layer, obj, transform, color, material?.native ?? default, attributes.Get() );
		}

		public void BindTarget( IRenderContext context, RenderTarget target )
		{
			context.BindRenderTargets( target.ColorTarget?.native ?? default, target.DepthTarget?.native ?? default, Layer );
		}

		public void RestoreTargets( IRenderContext context, float minZ, float maxZ )
		{
			// alex: if we don't restore min/max Z values properly when setting back to
			// the default render target, we get a lot of weird depth issues.
			// This mainly only applies to things like worldpanels when we render filtered
			// elements, but could probably happen in other places too?
			context.SetViewport( new RenderViewport( Layer.m_viewport.Rect, minZ, maxZ ) );
			context.RestoreRenderTargets( Layer );
		}
	}

	/// <summary>
	/// The frame the current render block draws into, or null in a standalone one.
	/// </summary>
	internal static IFrameView CurrentView => _state.view;

	/// <summary>
	/// The frame, for what only a view can answer - which a standalone render block has none of.
	/// </summary>
	static IFrameView View => _state.view ?? throw new InvalidOperationException( "A standalone render block has no view." );

	/// <summary>
	/// The managed frame being drawn, or null in a native view.
	/// </summary>
	internal static ManagedView CurrentManagedView => _state.view as ManagedView;

	/// <summary>
	/// The native layer being drawn, or null outside a native view - for native calls that take one.
	/// </summary>
	internal static ISceneLayer SceneLayer => _state.view is NativeFrameView native ? native.Layer : default;

	/// <summary>
	/// The native view being drawn, or null outside one - for native calls that take one, such as a child view's parent.
	/// </summary>
	internal static ISceneView SceneView => _state.view is NativeFrameView native ? native.View : default;

	/// <summary>
	/// A material's mode for what's being drawn: the frame's, or its default in a standalone render block.
	/// </summary>
	internal static IMaterialMode ModeFor( Material material ) => _state.view?.ModeFor( material ) ?? material.native.GetMode();

	/// <summary>
	/// The shader mode <c>RenderTools.DrawModel</c> draws with when there's no native layer to take it from: a managed
	/// frame's, or none (a material's default) in a standalone render block.
	/// </summary>
	internal static StringToken DrawShaderMode => _state.view?.DrawShaderMode ?? default;

	/// <summary>
	/// The current view's frustum.
	/// </summary>
	internal static CFrustum ViewFrustum => View.Frustum;

	/// <summary>
	/// Whether the current view has post processing on.
	/// </summary>
	internal static bool PostProcessEnabled => View.PostProcessEnabled;

	/// <summary>
	/// The current view's tools visualisation mode.
	/// </summary>
	internal static int ToolsVisMode => View.ToolsVisMode;

	/// <summary>
	/// Whether the current view is another's child. A managed frame is always a camera's own.
	/// </summary>
	internal static bool IsChildView => View.IsChildView;

	/// <summary>
	/// The current frame's colour target. When <paramref name="owned"/>, it's a strong handle the caller destroys.
	/// </summary>
	internal static ITexture GetColorTarget( out bool owned ) => View.GetColorTarget( out owned );

	/// <summary>
	/// The current frame's depth target, like <see cref="GetColorTarget"/>.
	/// </summary>
	internal static ITexture GetDepthTarget( out bool owned ) => View.GetDepthTarget( out owned );
}
