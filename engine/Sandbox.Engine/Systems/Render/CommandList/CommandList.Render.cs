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
	/// </summary>
	public void DrawView( CameraComponent camera, RenderTargetHandle target, ViewSetup viewSetup = default )
	{
		if ( !camera.IsValid() ) return;

		static void Execute( ref Entry entry, CommandList commandList )
		{
			var camera = (CameraComponent)entry.Object1;
			if ( !camera.IsValid() ) return;

			var rt = commandList.GetRenderTarget( (string)entry.Object5 );
			if ( rt == null ) return;

			// This is a secondary view, don't draw reflections in it
			// or we'll end up with an infinite loop!
			if ( Graphics.IsChildView ) return;

			camera.RenderToTexture( rt.ColorTarget, (ViewSetup)entry.Object2 );
		}

		AddEntry( &Execute, new Entry { Object1 = camera, Object5 = target.Name, Object2 = viewSetup } );
	}

	/// <summary>
	/// Render a planar reflection using the specified camera and the specified plane.
	/// </summary>
	public void DrawReflection( CameraComponent camera, Plane plane, in RenderTargetHandle target, ReflectionSetup reflectionSetup = default )
	{
		if ( !camera.IsValid() ) return;

		static void Execute( ref Entry entry, CommandList commandList )
		{
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

			var reflectedTransform = Graphics.CameraTransform.Mirror( plane );
			var projection = Matrix.CreateProjection( camera.ZNear, camera.ZFar, Graphics.FieldOfView, Graphics.Viewport.Width / Graphics.Viewport.Height, setup.ViewSetup.ClipSpaceBounds ?? new Vector4( -1, -1, 1, 1 ) );

			var eyeDistance = plane.GetDistance( reflectedTransform.Position );
			var clipOffset = setup.ClipOffset;
			var clipPlane = new Plane( plane.Position - clipOffset * plane.Normal, plane.Normal );

			var oblique = Matrix.CreateObliqueProjection( in reflectedTransform, in clipPlane, in projection );

			setup.ViewSetup.Transform ??= reflectedTransform;
			setup.ViewSetup.FieldOfView ??= Graphics.FieldOfView;
			setup.ViewSetup.ProjectionMatrix ??= oblique;
			setup.ViewSetup.FlipX ??= true; // we're mirroring!

			camera.RenderToTexture( rt.ColorTarget, setup.ViewSetup );
		}

		AddEntry( &Execute, new Entry { Object1 = camera, Object2 = reflectionSetup, Object5 = target.Name, Data1 = new Vector4( plane.Normal.x, plane.Normal.y, plane.Normal.z, 0 ), Data2 = new Vector4( plane.Distance, 0, 0, 0 ) } );
	}

	/// <summary>
	/// Render a planar refraction using the specified camera and the specified plane. This is for all intents and purposes a
	/// regular view with a plane clipping it. Usually used for rendering under water.
	/// </summary>
	public void DrawRefraction( CameraComponent camera, Plane plane, in RenderTargetHandle target, RefractionSetup refractionSetup = default )
	{
		if ( !camera.IsValid() ) return;

		static void Execute( ref Entry entry, CommandList commandList )
		{
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

			camera.RenderToTexture( rt.ColorTarget, setup.ViewSetup );
		}

		AddEntry( &Execute, new Entry { Object1 = camera, Object2 = refractionSetup, Object5 = target.Name, Data1 = new Vector4( plane.Normal.x, plane.Normal.y, plane.Normal.z, 0 ), Data2 = new Vector4( plane.Distance, 0, 0, 0 ) } );
	}

}




