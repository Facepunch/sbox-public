using NativeEngine;

namespace Sandbox.Rendering;

/// <summary>
/// Gives each top-level view the planar reflection it needs from its world's <see cref="SceneReflectionPlane"/>s, as
/// the view's layers are set up. The reflection is another top-level view that depends on nothing else this frame:
/// it's unshadowed, so it issues before every view, starting its light setup and recording first, and submits to the
/// GPU just before its view. That bounds the cost whatever a level places: one reflection per view, for the plane
/// whose reflecting area covers the most of the screen (planes lying on one plane share it), and none for areas
/// outside the view, behind it, or under <see cref="MinScreenCoverage"/>. The view's attributes publish the reflection
/// (<c>HasPlanarReflection</c>, <c>PlanarReflectionPlane</c>, ...): materials on that plane sample it, everything
/// else falls back to its own reflections.
/// </summary>
internal static class PlanarReflections
{
	/// <summary>
	/// The smallest share of the screen a plane's reflecting area must fill before a reflection is worth rendering.
	/// </summary>
	const float MinScreenCoverage = 0.02f;

	[ThreadStatic] static List<SceneReflectionPlane.State> _planes;
	[ThreadStatic] static List<SceneReflectionPlane.State> _group;
	[ThreadStatic] static TagSet _exclusions;

	static int _captureCount;

	/// <summary>
	/// The view whose reflection is being set up on this thread, while it is.
	/// </summary>
	[ThreadStatic] static ISceneView _reflecting;

	/// <summary>
	/// Number of planar reflections rendered, for tests.
	/// </summary>
	internal static int CaptureCount => System.Threading.Volatile.Read( ref _captureCount );

	/// <summary>
	/// The planes that shared the last reflection rendered, for tests.
	/// </summary>
	internal static int LastSharedCount { get; private set; }

	/// <summary>
	/// The plane of the last reflection rendered, for tests.
	/// </summary>
	internal static Plane LastPlane { get; private set; }

	/// <summary>
	/// Plan and start the view's planar reflection, and publish it in the view's attributes. Called as the view's
	/// layers are set up, on the thread setting up views.
	/// </summary>
	internal static void AddToView( ISceneView view, SceneCamera camera )
	{
		var attributes = view.GetRenderAttributesPtr();
		bool reflection = attributes.GetBoolValue( "IsReflection", false );
		attributes.SetBoolValue( "HasPlanarReflection", false );

		// Our reflection depends on nothing else this frame: issue it before every view and submit it before its own
		if ( reflection && _reflecting.IsValid )
		{
			view.SetIssueSortIndex( int.MinValue );
			view.SetPriority( _reflecting.GetPriority() - 1 );
		}

		// Reflections and child views (shadows, monitors) never render reflections of their own
		if ( camera?.World is null || reflection || view.GetParent().IsValid )
			return;

		var frustum = view.GetFrustum();
		if ( frustum.IsOrthographic() )
			return;

		var planes = _planes ??= [];
		planes.Clear();
		camera.World.GetReflectionPlanes( planes );
		if ( planes.Count == 0 )
			return;

		var eye = new Transform( frustum.GetCameraPosition(), frustum.GetCameraAngles() );
		float fieldOfView = frustum.GetCameraFOV();
		float aspect = frustum.GetCameraAspect();
		try
		{
			// Planes the eye is beneath or can't see don't count
			for ( int i = planes.Count - 1; i >= 0; i-- )
			{
				var plane = planes[i];
				if ( Vector3.Dot( eye.Position - plane.Position, plane.Normal ) <= 0
					|| (!plane.Infinite && !frustum.BoundingVolumeIntersectsFrustum( Bounds( plane ) )) )
					planes.RemoveAt( i );
			}

			// Group planes lying on one plane, in registration order, and pick the group covering the most of the screen
			SceneReflectionPlane.State best = null;
			float bestCoverage = MinScreenCoverage;
			for ( int i = 0; i < planes.Count; i++ )
			{
				var anchor = planes[i];
				if ( anchor is null )
					continue;

				float coverage = 0;
				for ( int j = i; j < planes.Count; j++ )
				{
					if ( planes[j] is not { } plane || !Coplanar( anchor, plane ) )
						continue;

					coverage += ScreenCoverage( plane, eye, fieldOfView, aspect );
					if ( j > i ) planes[j] = null;
				}

				if ( coverage >= bestCoverage && (best is null || coverage > bestCoverage) )
				{
					best = anchor;
					bestCoverage = coverage;
				}
			}

			if ( best is null )
				return;

			Render( view, camera, best, eye, fieldOfView, aspect, frustum.GetCameraNearPlane(), frustum.GetCameraFarPlane() );
		}
		finally
		{
			planes.Clear();
		}
	}

	/// <summary>
	/// Render the reflection for every plane lying on the anchor's into a temporary target, and publish it.
	/// </summary>
	static void Render( ISceneView view, SceneCamera camera, SceneReflectionPlane.State anchor, Transform eye, float fieldOfView, float aspect, float zNear, float zFar )
	{
		var group = _group ??= [];
		group.Clear();
		camera.World.GetReflectionPlanes( group );

		var exclusions = _exclusions ??= new TagSet();
		exclusions.RemoveAll();
		float clipOffset = 0;
		int downsample = 8;
		int shared = 0;
		foreach ( var plane in group )
		{
			if ( !Coplanar( anchor, plane ) )
				continue;

			if ( !string.IsNullOrEmpty( plane.ExcludeTag ) )
				exclusions.Add( plane.ExcludeTag );
			clipOffset = MathF.Max( clipOffset, plane.ClipOffset );
			downsample = Math.Min( downsample, plane.Downsample );
			shared++;
		}
		group.Clear();

		var normal = anchor.Normal;
		var reflectionPlane = new Plane( anchor.Position, normal );
		float eyeHeight = Vector3.Dot( eye.Position - anchor.Position, normal );

		// Up close the oblique clip plane is unstable, and an ordinary projection clips nothing that matters
		bool unclipped = eyeHeight <= MathF.Max( 0.5f, clipOffset ) + zNear * 2;

		var viewport = view.GetMainViewport().Rect;
		var (width, height) = RenderTarget.ScaleDownSize( new Vector2( viewport.Width, viewport.Height ), downsample );
		var target = RenderTarget.GetTemporary( width, height, ImageFormat.RGBA16161616F, ImageFormat.Default, numMips: 1, targetName: "Planar reflection" );

		var setup = new ReflectionSetup
		{
			ClipOffset = clipOffset + 0.5f,
			RenderBehind = true,
			ViewSetup = new ViewSetup
			{
				ProjectionMatrix = unclipped ? Matrix.CreateProjection( zNear, zFar, fieldOfView, aspect, new Vector4( -1, -1, 1, 1 ) ) : null,
				FlipX = true,
				EnablePostprocessing = false,
				EnableUI = false,
				EnableDebugOverlays = false,
				ViewHash = HashCode.Combine( "Planar reflection", anchor.ExcludeTag ),
				ExcludeTags = exclusions
			}
		};

		ViewData data;
		_reflecting = view;
		try
		{
			data = CommandList.RenderReflection( camera, default, eye, fieldOfView, aspect, zNear, zFar, reflectionPlane, target, setup, "Planar reflection" );
		}
		finally
		{
			_reflecting = default;
		}

		if ( !data.IsValid )
			return;

		System.Threading.Interlocked.Increment( ref _captureCount );
		LastSharedCount = shared;
		LastPlane = reflectionPlane;

		var attributes = view.GetRenderAttributesPtr();
		attributes.SetTextureValue( "PlanarReflectionTexture", target.ColorTarget.native, -1 );
		attributes.SetTextureValue( "PlanarReflectionDepthTexture", target.DepthTarget.native, -1 );
		attributes.SetVMatrixValue( "PlanarReflectionProjectionToView", data.ProjectionToView );
		attributes.SetVector4DValue( "PlanarReflectionDepthUvScaleBias", data.DepthUvScaleBias );
		attributes.SetVector4DValue( "PlanarReflectionViewport", data.Viewport );
		attributes.SetVector2DValue( "PlanarReflectionDepthRange", data.DepthRange );
		attributes.SetVector4DValue( "PlanarReflectionPlane", new Vector4( normal, reflectionPlane.Distance ) );
		attributes.SetBoolValue( "HasPlanarReflection", true );
	}

	/// <summary>
	/// Whether the plane lies on the anchor's, facing the same way, within an inch.
	/// </summary>
	static bool Coplanar( SceneReflectionPlane.State anchor, SceneReflectionPlane.State plane )
	{
		var normal = anchor.Normal;
		return Vector3.Dot( plane.Normal, normal ) > 0.9999f
			&& MathF.Abs( Vector3.Dot( plane.Position - anchor.Position, normal ) ) <= 1;
	}

	static Vector3 Corner( SceneReflectionPlane.State plane, int i )
	{
		var half = plane.Size * 0.5f;
		return plane.Transform.Position + plane.Transform.Rotation * new Vector3( (i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, 0 );
	}

	/// <summary>
	/// The reflecting area, including how far its surface moves.
	/// </summary>
	static BBox Bounds( SceneReflectionPlane.State plane )
	{
		var rise = plane.Normal * MathF.Max( 1, plane.ClipOffset );
		var bounds = new BBox( Corner( plane, 0 ) - rise, Corner( plane, 0 ) + rise );
		for ( int i = 1; i < 4; i++ )
		{
			bounds = bounds.AddPoint( Corner( plane, i ) - rise );
			bounds = bounds.AddPoint( Corner( plane, i ) + rise );
		}
		return bounds;
	}

	/// <summary>
	/// Rough share of the screen the reflecting area covers, from its on-screen bounds.
	/// An infinite plane, or an area partly behind the eye, counts as the whole screen.
	/// </summary>
	static float ScreenCoverage( SceneReflectionPlane.State plane, Transform eye, float fieldOfView, float aspect )
	{
		if ( plane.Infinite )
			return 1;

		float tanX = MathF.Tan( fieldOfView.DegreeToRadian() * 0.5f );
		float tanY = tanX / aspect;
		var min = new Vector2( float.MaxValue );
		var max = new Vector2( float.MinValue );
		int behind = 0;
		for ( int i = 0; i < 4; i++ )
		{
			// Forward, left and up in the eye's space
			var local = eye.PointToLocal( Corner( plane, i ) );
			if ( local.x <= 0.01f )
			{
				behind++;
				continue;
			}

			var screen = new Vector2( -local.y / (local.x * tanX), local.z / (local.x * tanY) ) * 0.5f + 0.5f;
			min = Vector2.Min( min, screen );
			max = Vector2.Max( max, screen );
		}

		if ( behind == 4 )
			return 0;
		if ( behind > 0 )
			return 1;

		min = Vector2.Max( min, Vector2.Zero );
		max = Vector2.Min( max, Vector2.One );
		return MathF.Max( 0, max.x - min.x ) * MathF.Max( 0, max.y - min.y );
	}
}
