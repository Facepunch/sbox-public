using System.Text.Json.Serialization;

namespace Editor;

public partial class SceneViewportWidget
{
	private class SceneCookie
	{
		public Vector3 CameraPosition { get; set; }
		public Rotation CameraRotation { get; set; }
		public float? CameraOrthoHeight { get; set; }
		public bool CameraOrtho { get; set; }
	}

	public enum ViewMode
	{
		[Title( "3D" ), Icon( "view_in_ar" )]
		Perspective,
		[Title( "Top 2D" ), Icon( "roofing" )]
		Top2d,
		[Title( "Front 2D" ), Icon( "cottage" )]
		Front2d,
		[Title( "Side 2D" ), Icon( "gite" )]
		Side2d,
		[Title( "Flat 2D (Screen)" ), Icon( "grid_on" )]
		Flat2d
	}

	public enum Shading2D
	{
		[Icon( "square" )]
		Shaded,
		[Icon( "polyline" )]
		Wireframe
	}

	public class ViewportState
	{
		public Vector3 CameraPosition { get; set; }
		public Rotation CameraRotation { get; set; }

		/// <summary>
		/// View mode of this viewport
		/// </summary>
		public ViewMode View
		{
			get => _mode;
			set => SetViewmode( value );
		}
		private ViewMode _mode;

		[JsonIgnore] public bool Is2D => View != ViewMode.Perspective;

		/// <summary>
		/// Render mode to use for this viewport
		/// </summary>
		public SceneCameraDebugMode RenderMode { get; set; } = SceneCameraDebugMode.Normal;

		/// <summary>
		/// Should the scene render in wireframe mode
		/// </summary>
		public bool WireframeMode { get; set; } = false;

		[Title( "2D Shading" ), ShowIf( nameof( Is2D ), true )]
		public Shading2D Shading { get; set; } = Shading2D.Shaded;

		/// <summary>
		/// Should we render post processing effects?
		/// </summary>
		public bool EnablePostProcessing { get; set; } = true;

		/// <summary>
		/// Enable the default directional light when editing prefabs.
		/// </summary>
		public bool EnablePrefabLighting { get; set; } = true;

		/// <summary>
		/// Should the skybox be visible in 2D mode
		/// </summary>
		public bool ShowSkyIn2D
		{
			get => !Is2D || _showSky;
			set => _showSky = value;
		}
		private bool _showSky;

		/// <summary>
		/// Show the grid
		/// </summary>
		public bool ShowGrid { get; set; } = true;

		/// <summary>
		/// Should we fade the grid
		/// </summary>
		[Range( 0.0f, 1.0f )]
		public float GridOpacity { get; set; } = 0.2f;

		/// <summary>
		/// The plane the grid is shown on
		/// </summary>
		public Gizmo.GridAxis GridAxis { get; set; } = Gizmo.GridAxis.XY;

		/// <summary>
		/// The orthographic size for the camera
		/// </summary>
		[Title( "Orthographic Height" )]
		public float CameraOrthoHeight { get; set; } = 1000.0f;

		private void SetViewmode( ViewMode viewmode )
		{
			_mode = viewmode;

			switch ( viewmode )
			{
				case ViewMode.Top2d:
					CameraRotation = Rotation.LookAt( Vector3.Down, Vector3.Left );
					GridAxis = Gizmo.GridAxis.XY;
					break;

				case ViewMode.Front2d:
					CameraRotation = Rotation.LookAt( Vector3.Forward, Vector3.Up );
					GridAxis = Gizmo.GridAxis.YZ;
					break;

				case ViewMode.Side2d:
					CameraRotation = Rotation.LookAt( Vector3.Left, Vector3.Up );
					GridAxis = Gizmo.GridAxis.ZX;
					break;

				// X+ Right, Y+ Down (matches Vector2 coordinate convention)
				case ViewMode.Flat2d:
					CameraRotation = Rotation.LookAt( Vector3.Up, Vector3.Right );
					GridAxis = Gizmo.GridAxis.XY;
					break;

				default:
					GridAxis = Gizmo.GridAxis.XY;
					break;
			}
		}

		internal static Gizmo.GridAxis GridAxisForDirection( Vector3 axisDir )
		{
			float[] a = [MathF.Abs( axisDir.x ), MathF.Abs( axisDir.y ), MathF.Abs( axisDir.z )];
			return Array.IndexOf( a, a.Max() ) switch
			{
				0 => Gizmo.GridAxis.YZ,
				1 => Gizmo.GridAxis.ZX,
				_ => Gizmo.GridAxis.XY
			};
		}
	}
	public ViewportState State { get; init; }

	private void InitializeCamera()
	{
		Session.MakeActive();

		using var scope = SceneEditorSession.Scope();

		Scene scene = Session.Scene;
		if ( !scene.IsValid() )
			return;

		// 1. load last camera position from cookies if possible
		if ( scene.Source is not null &&
			ProjectCookie.Get<SceneCookie>( $"{scene.Source.ResourcePath}.Viewport{Id}", null ) is SceneCookie cookie )
		{
			State.CameraPosition = cookie.CameraPosition;
			if ( cookie.CameraOrthoHeight.HasValue )
				State.CameraOrthoHeight = cookie.CameraOrthoHeight.Value;
			if ( !State.Is2D )
				State.CameraRotation = cookie.CameraRotation;

			// Restore an axis-aligned ortho view
			if ( cookie.CameraOrtho && !State.Is2D )
			{
				_gizmoOrthoActive = true;
				_gizmoOrthoSnap = true;
			}
			return;
		}

		//
		// 2. Place camera where a Camera component is
		//
		var cc = scene.Camera;
		if ( cc.IsValid() )
		{
			State.CameraPosition = cc.WorldPosition;
			if ( !State.Is2D )
				State.CameraRotation = cc.WorldRotation;
			return;
		}

		//
		// 3. BBox frame the scene
		//
		if ( !State.Is2D )
			State.CameraRotation = Rotation.From( 45, 45, 0 );

		var fieldOfView = 80.0f;
		var bounds = scene.GetBounds();
		var distance = MathX.SphereCameraDistance( bounds.Size.Length * 0.5f, fieldOfView ) * 1.0f;
		State.CameraPosition = bounds.Center + distance * State.CameraRotation.Backward;
	}

	[Event( "scene.session.save" )]
	public void SaveState()
	{
		if ( ProjectCookie is null ) return;

		Scene scene = Session.Scene;
		if ( !scene.IsValid() )
			return;

		if ( scene.Source is not null )
		{
			// TODO: this should still store something for non-resource scenes?
			ProjectCookie.Set( $"{scene.Source.ResourcePath}.Viewport{Id}", new SceneCookie()
			{
				CameraPosition = State.CameraPosition,
				CameraRotation = State.CameraRotation,
				CameraOrthoHeight = State.CameraOrthoHeight,
				CameraOrtho = _gizmoOrthoActive,
			} );
		}

		ProjectCookie.Set( $"SceneView.Viewport{Id}.Settings", State );
	}

	[Shortcut( "scene.cycle-viewmode", "CTRL+SPACE" )]
	public void CycleViewmode()
	{
		ViewMode newMode = (ViewMode)(((int)State.View + 1) % ((int)ViewMode.Flat2d + 1));
		if ( newMode == ViewMode.Perspective )
			EnterPerspectiveView();
		else
			State.View = newMode;

		Vector3 center = Vector3.Zero;
		int count = 0;

		if ( Tools.CurrentSubTool is { } tool && tool.TryGetSelectionOrigin( out var origin ) )
		{
			center = origin;
			count = 1;
		}
		else
		{
			foreach ( var selected in Session.Selection.OfType<GameObject>() )
			{
				center += selected.WorldPosition;
				count++;
			}
		}

		if ( count > 0 )
		{
			center /= count;
			var forward = State.CameraRotation.Forward;
			var distance = State.Is2D ? forward.Dot( center - State.CameraPosition ) : 400;
			State.CameraPosition = center - forward * distance;
		}

		cameraTargetPosition = null;
		GizmoInstance.SetValue<Vector3?>( "CameraTarget", null );
		GizmoInstance.SetValue<Vector3>( "CameraVelocity", Vector3.Zero );
	}
}
