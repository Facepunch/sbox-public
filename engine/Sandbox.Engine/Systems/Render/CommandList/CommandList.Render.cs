namespace Sandbox.Rendering;


public sealed unsafe partial class CommandList
{
	/// <summary>
	/// Render a <see cref="Renderer"/> with the specified overrides.
	/// </summary>
	public void DrawRenderer( Renderer renderer, RendererSetup rendererSetup = default )
	{
		if ( !renderer.IsValid() )
			return;

		static void Execute( ref Entry entry, CommandList commandList )
		{
			var renderer = (Renderer)entry.Object1;
			if ( !renderer.IsValid() )
				return;

			// The engine's drawing of the renderer's scene object (no game code), so it replays on the render thread too
			renderer.RenderSceneObject( commandList.PlayedRendererSetup( entry ) );
		}

		AddEntry( &Execute, new Entry { Object1 = renderer }, RendererSetupStore, rendererSetup );
	}

	/// <summary>
	/// Renders the view from a camera to the specified render target.
	/// When present, the target's depth texture is written directly in the unflipped camera projection.
	/// </summary>
	/// <returns>Deferred metadata for this view, available to subsequent attribute commands.</returns>
	public ViewHandle DrawView( CameraComponent camera, RenderTargetHandle target, ViewSetup viewSetup = default )
	{
		var view = CreateViewHandle();

		static void Execute( ref Entry entry, CommandList commandList )
		{
			commandList.state.views[entry.ViewId] = default;

			var camera = (CameraComponent)entry.Object1;
			if ( !camera.IsValid() ) return;

			var rt = commandList.GetRenderTarget( (string)entry.Object5 );
			if ( rt == null ) return;

			camera.RenderToTexture( rt.ColorTarget, rt.DepthTarget, commandList.PlayedViewSetup( entry ), out var result );
			commandList.state.views[entry.ViewId] = result;
		}

		AddEntry( &Execute, new Entry { Object1 = camera, Object5 = target.Name, ViewId = view.Id }, ViewSetupStore, viewSetup );
		return view;
	}

	/// <summary>Renders a planar reflection into the specified render target.</summary>
	/// <remarks>A capture rejected by the renderer returns invalid metadata.</remarks>
	/// <returns>Deferred metadata for this view, available to subsequent attribute commands.</returns>
	public ViewHandle DrawReflection( CameraComponent camera, Plane plane, in RenderTargetHandle target, ReflectionSetup reflectionSetup = default )
	{
		var view = CreateViewHandle();

		static void Execute( ref Entry entry, CommandList commandList )
		{
			commandList.state.views[entry.ViewId] = default;

			var camera = (CameraComponent)entry.Object1;
			if ( !camera.IsValid() ) return;

			var setup = commandList.PlayedReflectionSetup( entry );
			var plane = new Plane( new Vector3( entry.Data1.x, entry.Data1.y, entry.Data1.z ), entry.Data2.x );

			var rt = commandList.GetRenderTarget( (string)entry.Object5 );
			if ( rt == null ) return;

			// Don't render if we're the other side!
			if ( !plane.IsInFront( Graphics.CameraTransform.Position ) && !setup.RenderBehind )
				return;

			var viewport = Graphics.Viewport;
			var reflection = ReflectionView( Graphics.CameraTransform, Graphics.FieldOfView, viewport.Width / viewport.Height,
				camera.ZNear, camera.ZFar, plane, setup );
			camera.SceneCamera.RenderToTexture( rt.ColorTarget, rt.DepthTarget, reflection, out var result );
			commandList.state.views[entry.ViewId] = result;
		}

		AddEntry( &Execute, new Entry { Object1 = camera, ViewId = view.Id, Object5 = target.Name, Data1 = new Vector4( plane.Normal.x, plane.Normal.y, plane.Normal.z, 0 ), Data2 = new Vector4( plane.Distance, 0, 0, 0 ) }, ReflectionSetupStore, reflectionSetup );
		return view;
	}

	/// <summary>
	/// Whether reflections render shadows. Off, they're lit but unshadowed, so they never wait on a shadow map and
	/// can render before every other view.
	/// </summary>
	[ConVar( "r.reflections.shadows", Help = "Render shadows in planar reflections. Off, reflections are lit but unshadowed and render before every other view." )]
	internal static bool ReflectionShadows { get; set; }

	/// <summary>Prepare the mirrored view, with an oblique near plane unless the caller supplies a projection.</summary>
	internal static ViewSetup ReflectionView( Transform eye, float fieldOfView, float aspect, float zNear, float zFar, Plane plane, ReflectionSetup setup )
	{
		var reflectedTransform = eye.Mirror( plane );
		if ( setup.ViewSetup.ProjectionMatrix is null )
		{
			var clipPlane = new Plane( plane.Position - setup.ClipOffset * plane.Normal, plane.Normal );
			setup.ViewSetup.ProjectionMatrix = ClippedProjection( reflectedTransform, clipPlane, fieldOfView, aspect, zNear, zFar, setup.ViewSetup.ClipSpaceBounds );
		}

		setup.ViewSetup.Transform ??= reflectedTransform;
		setup.ViewSetup.FieldOfView ??= fieldOfView;
		setup.ViewSetup.ZNear ??= zNear;
		setup.ViewSetup.ZFar ??= zFar;
		setup.ViewSetup.FlipX ??= true; // we're mirroring!
		setup.ViewSetup.Reflection = true;
		return setup.ViewSetup;
	}

	static Matrix ClippedProjection( in Transform eye, in Plane clip, float fieldOfView, float aspect, float zNear, float zFar, Vector4? bounds )
	{
		var projection = Matrix.CreateProjection( zNear, zFar, fieldOfView, aspect, bounds ?? new Vector4( -1, -1, 1, 1 ) );
		return Matrix.CreateObliqueProjection( in eye, in clip, in projection );
	}

	/// <summary>
	/// Render a planar refraction using the specified camera and the specified plane. This is for all intents and purposes a
	/// regular view with a plane clipping it. Usually used for rendering under water.
	/// When present, the target's depth texture receives the oblique camera depth before colour flips.
	/// </summary>
	/// <remarks>A capture rejected by the renderer returns invalid metadata.</remarks>
	/// <returns>Deferred metadata for this view, available to subsequent attribute commands.</returns>
	public ViewHandle DrawRefraction( CameraComponent camera, Plane plane, in RenderTargetHandle target, RefractionSetup refractionSetup = default )
	{
		var view = CreateViewHandle();

		static void Execute( ref Entry entry, CommandList commandList )
		{
			commandList.state.views[entry.ViewId] = default;

			var camera = (CameraComponent)entry.Object1;
			if ( !camera.IsValid() ) return;

			var setup = commandList.PlayedRefractionSetup( entry );
			var plane = new Plane( new Vector3( entry.Data1.x, entry.Data1.y, entry.Data1.z ), entry.Data2.x );

			var rt = commandList.GetRenderTarget( (string)entry.Object5 );
			if ( rt == null ) return;

			// Don't render if we're the other side!
			if ( !plane.IsInFront( Graphics.CameraTransform.Position ) && !setup.RenderBehind )
				return;

			var transform = Graphics.CameraTransform;
			if ( setup.ViewSetup.ProjectionMatrix is null )
			{
				var clipPlane = new Plane( plane.Position + setup.ClipOffset * plane.Normal, plane.Normal );
				var viewport = Graphics.Viewport;
				setup.ViewSetup.ProjectionMatrix = ClippedProjection( transform, clipPlane, Graphics.FieldOfView,
					viewport.Width / viewport.Height, camera.ZNear, camera.ZFar, setup.ViewSetup.ClipSpaceBounds );
				// The 3D skybox uses the same oblique plane; a caller's own projection has none.
				setup.ViewSetup.ClipPlane = clipPlane;
			}

			setup.ViewSetup.Transform ??= transform;
			setup.ViewSetup.FieldOfView ??= Graphics.FieldOfView;

			camera.RenderToTexture( rt.ColorTarget, rt.DepthTarget, setup.ViewSetup, out var result );
			commandList.state.views[entry.ViewId] = result;
		}

		AddEntry( &Execute, new Entry { Object1 = camera, ViewId = view.Id, Object5 = target.Name, Data1 = new Vector4( plane.Normal.x, plane.Normal.y, plane.Normal.z, 0 ), Data2 = new Vector4( plane.Distance, 0, 0, 0 ) }, RefractionSetupStore, refractionSetup );
		return view;
	}

}




