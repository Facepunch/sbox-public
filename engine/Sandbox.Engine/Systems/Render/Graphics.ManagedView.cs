using NativeEngine;

namespace Sandbox;

public static partial class Graphics
{
	/// <summary>
	/// A frame drawn outside native's scene views, by the managed scene renderer (Basalt, with
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
		/// Whether a render target bound in place of the frame's (<see cref="Graphics.RenderTarget"/>) is written through an
		/// sRGB view: the layer's colour space, as native binds one (<c>IRenderContext.BindRenderTargets</c> with a layer).
		/// Native's layers are all sRGB but the gamma-space UI ones, whatever the format of the frame's own target.
		/// </summary>
		public bool TargetSrgbWrite = true;

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
		/// (the native pipeline's hook layers, <c>Sandbox.Rendering.Pipeline.HookLayers</c>) and its forward layers. A shader without it draws with its default mode.
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
			RenderTools.BindColorAndDepthTarget( context, color, target.DepthTarget?.native ?? default, target.ColorTarget is null ? SrgbWrite : TargetSrgbWrite );
		}

		// Its own depth range, not the one saved before the target was bound
		void IFrameView.RestoreTargets( IRenderContext context, float minZ, float maxZ ) => Bind( context );

		static readonly StringToken TransformBufferName = new( "g_TransformBuffer" );

		// Instance transforms drawn from the CPU this frame, and buffers outgrown this frame that earlier draws still read
		static GpuBuffer<Rendering.GpuInstanceTransform> s_instanceTransforms;
		static readonly List<GpuBuffer> s_outgrownTransforms = new();
		static Rendering.GpuInstanceTransform[] s_transformScratch = new Rendering.GpuInstanceTransform[64];
		static RenderAttributes s_instanceAttributes;
		static int s_instanceTransformCount;
		static ulong s_instanceTransformFrame = ulong.MaxValue;

		// Native's sequential instance ids (TRANSFORM_ENTRY_COUNT), which index the buffer
		const int MaxInstanceTransforms = 1024 * 1024;

		/// <summary>
		/// Draw instances of a model at transforms from the CPU (<see cref="Graphics.DrawModelInstanced(Model, Span{Transform}, int, RenderAttributes)"/>).
		/// Native writes them into the scene system's frame transforms, which it uploads at the end of the frame
		/// (<c>CSceneSystem::SubmitAllDisplayLists</c>), after a managed frame has been submitted, and shaders read them
		/// through <c>g_TransformBuffer</c>, which a managed frame points at its own. So upload them on the context into a
		/// buffer of their own, and draw that with native's sequential instance ids. Main thread only, like custom objects.
		/// </summary>
		internal static unsafe void DrawModelInstanced( IRenderContext context, Model model, ReadOnlySpan<Transform> transforms, int lodLevel, RenderAttributes attributes )
		{
			ThreadSafe.AssertIsMainThread();

			var count = transforms.Length;
			if ( count > MaxInstanceTransforms ) return;

			// Uploads are ordered on the GPU, so a new frame starts the buffer over
			if ( s_instanceTransformFrame != Application.FrameCount || s_instanceTransformCount + count > MaxInstanceTransforms )
			{
				s_instanceTransformFrame = Application.FrameCount;
				s_instanceTransformCount = 0;

				foreach ( var buffer in s_outgrownTransforms ) buffer.Dispose();
				s_outgrownTransforms.Clear();
			}

			var first = s_instanceTransformCount;
			if ( s_instanceTransforms is null || s_instanceTransforms.ElementCount < first + count )
			{
				if ( s_instanceTransforms is not null ) s_outgrownTransforms.Add( s_instanceTransforms );
				var size = Math.Min( MaxInstanceTransforms, Math.Max( first + count, Math.Max( 256, (s_instanceTransforms?.ElementCount ?? 0) * 2 ) ) );
				s_instanceTransforms = new GpuBuffer<Rendering.GpuInstanceTransform>( size, GpuBuffer.UsageFlags.Structured, "Managed frame instance transforms" );
			}

			if ( s_transformScratch.Length < count ) s_transformScratch = new Rendering.GpuInstanceTransform[Math.Max( count, s_transformScratch.Length * 2 )];
			for ( int i = 0; i < count; i++ )
				s_transformScratch[i] = Rendering.GpuInstanceTransform.From( transforms[i] );

			var stride = sizeof( Rendering.GpuInstanceTransform );
			fixed ( Rendering.GpuInstanceTransform* data = s_transformScratch )
			{
				RenderTools.SetGPUBufferData( context, s_instanceTransforms.native, (IntPtr)data, (uint)(count * stride), (uint)(first * stride) );
			}

			s_instanceTransformCount += count;

			// The caller's attributes, with the transforms in place of the frame's
			s_instanceAttributes ??= new RenderAttributes();
			s_instanceAttributes.Set( TransformBufferName, s_instanceTransforms );
			var drawAttributes = s_instanceAttributes.Get();
			drawAttributes.SetParent( attributes.Get() );

			try
			{
				CSceneSystem.BindTransformSlot( context, 1, first );
				RenderTools.DrawModel( context, default, Graphics.DrawShaderMode, model.native, IntPtr.Zero, count, drawAttributes, lodLevel );
			}
			finally
			{
				drawAttributes.SetParent( default );
			}
		}
	}
}
