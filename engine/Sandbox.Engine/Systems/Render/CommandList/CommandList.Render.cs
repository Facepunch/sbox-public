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

			renderer.RenderSceneObject( (RendererSetup)entry.Object2 );
		}

		AddEntry( &Execute, new Entry { Object1 = renderer, Object2 = rendererSetup } );
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

			// This is a secondary view, don't draw reflections in it
			// or we'll end up with an infinite loop!
			if ( Graphics.IsChildView ) return;

			camera.RenderToTexture( rt.ColorTarget, rt.DepthTarget, (ViewSetup)entry.Object2, out var result );
			commandList.state.views[entry.ViewId] = result;
		}

		AddEntry( &Execute, new Entry { Object1 = camera, Object5 = target.Name, Object2 = viewSetup, ViewId = view.Id } );
		return view;
	}

	/// <summary>
	/// Render a planar reflection using the specified camera and the specified plane.
	/// When present, the target's depth texture receives the oblique camera depth before colour flips.
	/// </summary>
	/// <returns>Deferred metadata for this view, available to subsequent attribute commands.</returns>
	public ViewHandle DrawReflection( CameraComponent camera, Plane plane, in RenderTargetHandle target, ReflectionSetup reflectionSetup = default )
	{
		var view = CreateViewHandle();

		static void Execute( ref Entry entry, CommandList commandList )
		{
			commandList.state.views[entry.ViewId] = default;

			var camera = (CameraComponent)entry.Object1;
			if ( !camera.IsValid() ) return;

			var setup = (ReflectionSetup)entry.Object2;
			var plane = new Plane( new Vector3( entry.Data1.x, entry.Data1.y, entry.Data1.z ), entry.Data2.x );

			var rt = commandList.GetRenderTarget( (string)entry.Object5 );
			if ( rt == null ) return;

			// Don't render if we're the other side!
			if ( !plane.IsInFront( Graphics.CameraTransform.Position ) && !setup.RenderBehind )
				return;

			// This is a secondary view, don't draw reflections in it
			// or we'll end up with an infinite loop!
			if ( Graphics.IsChildView )
			{
				if ( setup.FallbackColor is { } clearColor )
				{
					Graphics.RenderTarget = rt;
					Graphics.Clear( clearColor );
					Graphics.RenderTarget = null;
				}
				return;
			}

			commandList.state.views[entry.ViewId] = RenderReflection( camera, plane, rt, setup );
		}

		AddEntry( &Execute, new Entry { Object1 = camera, Object2 = reflectionSetup, ViewId = view.Id, Object5 = target.Name, Data1 = new Vector4( plane.Normal.x, plane.Normal.y, plane.Normal.z, 0 ), Data2 = new Vector4( plane.Distance, 0, 0, 0 ) } );
		return view;
	}

	/// <summary>
	/// Whether reflections render shadows. Off, they're lit but unshadowed, so they never wait on a shadow map and
	/// can render before every other view.
	/// </summary>
	[ConVar( "r.reflections.shadows", Help = "Render shadows in planar reflections. Off, reflections are lit but unshadowed and render before every other view." )]
	internal static bool ReflectionShadows { get; set; }

	/// <summary>
	/// Render the current view mirrored in a plane into a render target, clipped to the plane's far side.
	/// Called during playback, outside a child view.
	/// </summary>
	internal static ViewData RenderReflection( CameraComponent camera, Plane plane, RenderTarget rt, ReflectionSetup setup )
	{
		var viewport = Graphics.Viewport;
		return RenderReflection( camera.SceneCamera, Graphics.SceneView, Graphics.CameraTransform, Graphics.FieldOfView, viewport.Width / viewport.Height,
			camera.ZNear, camera.ZFar, plane, rt, setup, $"{camera.GameObject.Name}.Reflection" );
	}

	/// <summary>
	/// Render a view seen from <paramref name="eye"/> mirrored in a plane into a render target, clipped to the plane's
	/// far side, as a child of <paramref name="parent"/>, or a top-level view when it isn't valid.
	/// </summary>
	internal static ViewData RenderReflection( SceneCamera camera, NativeEngine.ISceneView parent, Transform eye, float fieldOfView, float aspect,
		float zNear, float zFar, Plane plane, RenderTarget rt, ReflectionSetup setup, string name )
	{
		var reflectedTransform = eye.Mirror( plane );
		var projection = Matrix.CreateProjection( zNear, zFar, fieldOfView, aspect, setup.ViewSetup.ClipSpaceBounds ?? new Vector4( -1, -1, 1, 1 ) );

		var clipOffset = setup.ClipOffset;
		var clipPlane = new Plane( plane.Position - clipOffset * plane.Normal, plane.Normal );

		var oblique = Matrix.CreateObliqueProjection( in reflectedTransform, in clipPlane, in projection );

		setup.ViewSetup.Transform ??= reflectedTransform;
		setup.ViewSetup.FieldOfView ??= fieldOfView;
		setup.ViewSetup.ZNear ??= zNear;
		setup.ViewSetup.ZFar ??= zFar;
		setup.ViewSetup.ProjectionMatrix ??= oblique;
		setup.ViewSetup.FlipX ??= true; // we're mirroring!
		setup.ViewSetup.Reflection = true;

		camera.RenderToTexture( rt.ColorTarget, rt.DepthTarget, parent, setup.ViewSetup, name, out var result );
		return result;
	}

	/// <summary>
	/// Render a planar refraction using the specified camera and the specified plane. This is for all intents and purposes a
	/// regular view with a plane clipping it. Usually used for rendering under water.
	/// When present, the target's depth texture receives the oblique camera depth before colour flips.
	/// </summary>
	/// <returns>Deferred metadata for this view, available to subsequent attribute commands.</returns>
	public ViewHandle DrawRefraction( CameraComponent camera, Plane plane, in RenderTargetHandle target, RefractionSetup refractionSetup = default )
	{
		var view = CreateViewHandle();

		static void Execute( ref Entry entry, CommandList commandList )
		{
			commandList.state.views[entry.ViewId] = default;

			var camera = (CameraComponent)entry.Object1;
			if ( !camera.IsValid() ) return;

			var setup = (RefractionSetup)entry.Object2;
			var plane = new Plane( new Vector3( entry.Data1.x, entry.Data1.y, entry.Data1.z ), entry.Data2.x );

			var rt = commandList.GetRenderTarget( (string)entry.Object5 );
			if ( rt == null ) return;

			// Don't render if we're the other side!
			if ( !plane.IsInFront( Graphics.CameraTransform.Position ) && !setup.RenderBehind )
				return;

			// This is a secondary view, don't draw reflections in it
			// or we'll end up with an infinite loop!
			if ( Graphics.IsChildView )
			{
				if ( setup.FallbackColor is { } clearColor )
				{
					Graphics.RenderTarget = rt;
					Graphics.Clear( clearColor );
					Graphics.RenderTarget = null;
				}
				return;
			}

			var transform = Graphics.CameraTransform;
			var projection = Matrix.CreateProjection( camera.ZNear, camera.ZFar, Graphics.FieldOfView, Graphics.Viewport.Width / Graphics.Viewport.Height, setup.ViewSetup.ClipSpaceBounds ?? new Vector4( -1, -1, 1, 1 ) );

			var eyeDistance = plane.GetDistance( transform.Position );
			var clipOffset = setup.ClipOffset;

			var clipPlane = new Plane( plane.Position + clipOffset * plane.Normal, plane.Normal );

			var oblique = Matrix.CreateObliqueProjection( in transform, in clipPlane, in projection );

			setup.ViewSetup.Transform ??= transform;
			setup.ViewSetup.FieldOfView ??= Graphics.FieldOfView;
			setup.ViewSetup.ProjectionMatrix ??= oblique;

			camera.RenderToTexture( rt.ColorTarget, rt.DepthTarget, setup.ViewSetup, out var result );
			commandList.state.views[entry.ViewId] = result;
		}

		AddEntry( &Execute, new Entry { Object1 = camera, Object2 = refractionSetup, ViewId = view.Id, Object5 = target.Name, Data1 = new Vector4( plane.Normal.x, plane.Normal.y, plane.Normal.z, 0 ), Data2 = new Vector4( plane.Distance, 0, 0, 0 ) } );
		return view;
	}

}




