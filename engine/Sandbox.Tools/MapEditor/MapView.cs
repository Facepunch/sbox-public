using Editor.MapDoc;
using NativeEngine;
using NativeHammer;
using System;

namespace Editor.MapEditor;

/// <summary>
/// MapViews are owned by the MapViewMgr. They display the MapViewMgr's mapdoc.
///
/// The MapView provides either a 2d or 3d view of the provided map doc. The rendering mode
/// may be swapped between various 2d and 3d modes dynamically. In addition to basic display 
/// functionality the view also provides movement implementation for moving a camera within a 3d view
/// or panning a 2d view.
/// </summary>
public partial class MapView : IHandle
{
	#region IHandle
	//
	// A pointer to the actual native object
	//
	internal CMapView native;

	ToolSceneViews.HudSource hudSource;

	/// <summary>The view's tool HUD for Borealis.Gpu (<c>CMapView::GetManagedHudItems</c>).</summary>
	internal unsafe ToolSceneViews.HudSource HudSource => hudSource ??= ( items, maxItems, width, height ) => native.IsValid ? native.GetManagedHudItems( (IntPtr)items, maxItems, width, height ) : 0;

	//
	// IHandle implementation
	//
	void IHandle.HandleInit( IntPtr ptr ) => OnNativeInit( ptr );
	void IHandle.HandleDestroy() => OnNativeDestroy();
	bool IHandle.HandleValid() => !native.IsNull;
	#endregion

	internal MapView() { }
	internal MapView( HandleCreationData _ ) { }

	internal virtual void OnNativeInit( CMapView ptr )
	{
		native = ptr;
	}

	internal virtual void OnNativeDestroy()
	{
		native = IntPtr.Zero;
	}

	/// <summary>
	/// Read-only SceneCamera set automatically during rendering
	/// </summary>
	public SceneCamera SceneCamera { get; init; } = new SceneCamera( "MapView" );

	public Gizmo.Instance GizmoInstance { get; set; } = new();

	public MapDocument MapDoc => native.GetMapDoc();

	/// <summary>
	/// 
	/// </summary>
	public Vector2 MousePosition
	{
		get
		{
			native.GetMousePosition( out Vector2 mousePosition );
			return mousePosition;
		}
	}

	/// <summary>
	/// True for the 2D views (top, front, side), false for 3D perspective views.
	/// </summary>
	public bool IsOrthographic => native.GetCamera().IsOrthographic();

	/// <summary>The view's draw mode (native's <c>ViewDrawMode_t</c>: 0 wireframe, 1 fullbright, 5 all lighting...), as its pane's menu sets it.</summary>
	internal int DrawMode
	{
		get => native.GetViewDrawMode();
		set => native.SetViewDrawMode( value );
	}

	/// <summary>The view's type (native's <c>ViewType_t</c>: 0 top, 1 front, 2 side, 3 the 3D camera).</summary>
	internal int ViewType => native.GetViewTypeValue();

	/// <summary>Make the view another type, drawn in <paramref name="drawMode"/>, as its pane's menu does.</summary>
	internal void SetViewType( int viewType, int drawMode ) => native.SetViewTypeValue( viewType, drawMode );

	/// <summary>
	/// Where this view's camera is.
	/// </summary>
	public Vector3 CameraPosition
	{
		get => native.GetCamera().GetOrigin();
		set
		{
			native.GetCamera().SetOrigin( value );
			native.MarkHudDirty();
		}
	}

	/// <summary>
	/// Which way this view's camera is looking.
	/// </summary>
	public Angles CameraAngles
	{
		get => native.GetCamera().GetAngles();
		set
		{
			native.GetCamera().SetAngles( value );
			native.MarkHudDirty();
		}
	}

	/// <summary>
	/// The camera's field of view in degrees. Meaningless for orthographic views.
	/// </summary>
	public float FieldOfView => native.GetCamera().GetCameraFOV();

	/// <summary>
	/// The view's size in pixels.
	/// </summary>
	public Vector2 Size => new( native.GetCamera().GetWidth(), native.GetCamera().GetHeight() );

	/// <summary>
	/// Render the map from any camera into the bitmap, with this view's draw settings - the same
	/// rendering Hammer's screenshot dialog uses. Doesn't move this view's camera.
	/// </summary>
	public unsafe void RenderToBitmap( Bitmap bitmap, Vector3 position, Angles angles, float fieldOfView )
	{
		ArgumentNullException.ThrowIfNull( bitmap );
		if ( bitmap.IsFloatingPoint ) throw new ArgumentException( "Bitmap must be 8 bit RGBA" );

		var width = bitmap.Width;
		var height = bitmap.Height;
		var rgb = new byte[width * height * 3];

		fixed ( byte* pixels = rgb )
		{
			if ( !native.RenderToPixels( position, angles, fieldOfView, width, height, (IntPtr)pixels ) )
				throw new InvalidOperationException( "This view has no editor session to render with" );
		}

		WriteBitmap( bitmap, rgb );
	}

	/// <summary>
	/// Render the view from its own camera, a 2D view's too, into <paramref name="bitmap"/>, which is the view's size.
	/// </summary>
	internal unsafe void RenderViewToBitmap( Bitmap bitmap )
	{
		ArgumentNullException.ThrowIfNull( bitmap );
		if ( bitmap.IsFloatingPoint ) throw new ArgumentException( "Bitmap must be 8 bit RGBA" );

		var rgb = new byte[bitmap.Width * bitmap.Height * 3];
		fixed ( byte* pixels = rgb )
		{
			if ( !native.RenderViewToPixels( bitmap.Width, bitmap.Height, (IntPtr)pixels ) )
				throw new InvalidOperationException( "This view has no editor session to render with" );
		}

		WriteBitmap( bitmap, rgb );
	}

	static void WriteBitmap( Bitmap bitmap, byte[] rgb )
	{
		var width = bitmap.Width;
		var height = bitmap.Height;
		var rgba = new byte[width * height * 4];

		for ( int i = 0, j = 0; i < rgb.Length; i += 3, j += 4 )
		{
			rgba[j] = rgb[i];
			rgba[j + 1] = rgb[i + 1];
			rgba[j + 2] = rgb[i + 2];
			rgba[j + 3] = 255;
		}

		bitmap.SetPixelData( rgba, false );
	}

	/// <summary>
	/// Builds a ray from the mouse cursor
	/// </summary>
	public void BuildRay( out Vector3 startRay, out Vector3 endRay )
	{
		native.GetCamera().BuildRay( MousePosition, out startRay, out endRay );
	}

	readonly List<SceneWorld> _frameWorlds = new();

	/// <summary>
	/// The managed worlds this view draws on top of Hammer's own: the map's scene, its debug world and the gizmo worlds.
	/// </summary>
	internal List<SceneWorld> FrameWorlds()
	{
		_frameWorlds.Clear();

		var scene = MapDoc?.World?.Scene;
		if ( scene is null ) return _frameWorlds;

		_frameWorlds.Add( scene.SceneWorld );
		_frameWorlds.Add( scene.DebugSceneWorld );
		foreach ( var world in SceneCamera.Worlds )
		{
			if ( world.IsValid() ) _frameWorlds.Add( world );
		}

		return _frameWorlds;
	}

	/// <summary>
	/// The frame's managed work before the view draws: the camera, gizmo input, the map scene's editor tick and gizmo draws,
	/// and the selection the gizmos changed. False without a map scene, when there's nothing of ours to draw.
	/// </summary>
	internal bool PrepareFrame()
	{
		CToolCamera toolCamera = native.GetCamera();

		// Readonly camera, we could probably hook this up with good reason
		SceneCamera.Position = toolCamera.GetOrigin();
		SceneCamera.Angles = toolCamera.GetAngles();
		SceneCamera.FieldOfView = toolCamera.GetCameraFOV();
		SceneCamera.Size = new Vector2( toolCamera.GetWidth(), toolCamera.GetHeight() );

		SceneCamera.Worlds.Add( GizmoInstance.World );

		GizmoInstance.Input.Camera = SceneCamera;
		GizmoInstance.Input.CursorPosition = MousePosition;
		GizmoInstance.Input.CursorRay = SceneCamera.GetRay( MousePosition );
		GizmoInstance.Input.LeftMouse = Application.MouseButtons.Contains( MouseButtons.Left );
		GizmoInstance.Input.Modifiers = Application.KeyboardModifiers;
		GizmoInstance.Input.IsHovered = native.IsActive();

		// Map current hammer manipulation mode to our gizmos
		GizmoInstance.Settings.EditMode = native.GetManipulationMode() switch
		{
			ManipulationMode_t.MANIPULATION_MODE_SELECT => "select",
			ManipulationMode_t.MANIPULATION_MODE_TRANSLATE => "position", // maybe should be translate
			ManipulationMode_t.MANIPULATION_MODE_ROTATE => "rotate",
			ManipulationMode_t.MANIPULATION_MODE_SCALE => "scale",
			ManipulationMode_t.MANIPULATION_MODE_PIVOT => "pivot",
			_ => "invalid"
		};

		var scene = MapDoc?.World?.Scene;

		if ( scene == null )
			return false;

		using var sceneScope = scene.PushScope();

		if ( !IsOrthographic )
			UpdateViewer( scene );

		{
			GizmoInstance.Selection.Clear();

			foreach ( var go in Selection.All.OfType<MapGameObject>() )
			{
				GizmoInstance.Selection.Add( go.GameObject );
			}
		}

		scene.EditorTick( RealTime.Now, RealTime.Delta );

		var hash = GizmoInstance.Selection.GetHashCode();

		// Draw gizmos
		using ( GizmoInstance.PushScope() )
		{
			if ( GizmoInstance.Input.IsHovered )
			{
				UpdateHovered();
			}

			scene.EditorDraw();
			EditorEvent.Run( "hammer.rendermapview", this );

			DrawSelection();

			MapViewDropTarget.CurrentDropTarget?.DrawGizmos( this );

			// Something changed
			if ( hash != GizmoInstance.Selection.GetHashCode() )
			{
				if ( !GizmoInstance.Input.Modifiers.HasFlag( KeyboardModifiers.Shift ) )
					Selection.Clear();

				// I bet this is expensive iterating the entire world
				foreach ( var node in MapDoc.World.Children.OfType<MapGameObject>().Where( x => GizmoInstance.Selection.OfType<GameObject>().Contains( x.GameObject ) ) )
				{
					Selection.Add( node );
				}

				// MapDoc.World.EditorSession
			}
		}

		// I forget what this is even doing
		// I think it lets gizmos stomp native traces, cool
		native.UpdateManagedGizmoState( GizmoInstance.current.HoveredPath != null, GizmoInstance.current.HitDistance );
		return true;
	}

	static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Scene, CameraComponent> _viewers = [];

	/// <summary>
	/// Keep an editor-only camera in the map's scene matching the 3D view about to render, like the scene editor's
	/// viewport camera. Components that follow the viewer, like an ocean's surface, use it.
	/// </summary>
	void UpdateViewer( Scene scene )
	{
		if ( !_viewers.TryGetValue( scene, out var viewer ) || !viewer.IsValid() )
		{
			var go = new GameObject( false, "Hammer View" );
			go.Flags = GameObjectFlags.EditorOnly | GameObjectFlags.NotSaved | GameObjectFlags.Hidden | GameObjectFlags.NotNetworked;
			viewer = go.Components.Create<CameraComponent>();
			viewer.IsMainCamera = false;
			go.Enabled = true;
			_viewers.AddOrUpdate( scene, viewer );
		}

		viewer.WorldPosition = SceneCamera.Position;
		viewer.WorldRotation = SceneCamera.Rotation;
		viewer.FieldOfView = SceneCamera.FieldOfView;
		viewer.CustomSize = SceneCamera.Size;
	}

	void UpdateHovered()
	{
		var scene = MapDoc?.World?.Scene;
		if ( scene is null ) return;

		var tr = scene.Trace.Ray( Gizmo.CurrentRay, Gizmo.RayDepth )
			.UseRenderMeshes( true )
			.UsePhysicsWorld( false )
			.Run();

		// We want to also trace native here
		float n = native.HitDistanceAtMouse();
		if ( tr.Distance > n )
			return;

		if ( tr.Hit && tr.Component.IsValid() )
		{
			using ( Gizmo.ObjectScope( tr.GameObject, tr.GameObject.WorldTransform ) )
			{
				Gizmo.Hitbox.DepthBias = 1;
				Gizmo.Hitbox.TrySetHovered( tr.Distance );

				if ( tr.Component is ModelRenderer mr && mr.Model is not null ) // TODO Is this selected
				{
					Gizmo.Draw.Color = Gizmo.Colors.Active.WithAlpha( MathF.Sin( RealTime.Now * 20.0f ).Remap( -1, 1, 0.3f, 0.8f ) );
					Gizmo.Draw.LineBBox( mr.Model.Bounds );
				}
			}
		}
	}

	void DrawSelection()
	{
		var scene = MapDoc?.World.Scene;
		if ( scene is null ) return;

		foreach ( var modelRenderer in GizmoInstance.Selection.OfType<GameObject>().SelectMany( x => x.Components.GetAll<ModelRenderer>() ) )
		{
			if ( modelRenderer.Model is null ) continue;

			using ( Gizmo.ObjectScope( modelRenderer.GameObject, default ) )
			{
				Gizmo.Transform = modelRenderer.GameObject.WorldTransform;
				Gizmo.Draw.Color = Gizmo.Colors.Selected;
				Gizmo.Draw.LineBBox( modelRenderer.Model.Bounds );
			}
		}
	}

	// There is access to the Qt IMapViewWindow via GetMapViewWindow() could cast to Widget ( Would be ever want that? )
}

internal static class MapViewRender
{
	/// <summary>
	/// Draw a view, 3D or 2D, on Borealis.Gpu (<c>CMapView::RenderManaged</c>): Hammer's worlds come in
	/// the request (a <c>ToolSceneManagedRender_t</c>, drawn as a native tool view is), the map scene and gizmos from here.
	/// </summary>
	internal static void RenderManaged( MapView view, IntPtr request )
	{
		if ( view is null || request == IntPtr.Zero ) return;

		List<SceneWorld> worlds = null;
		try
		{
			if ( view.PrepareFrame() ) worlds = view.FrameWorlds();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Hammer view: {e.Message}" );
		}

		ToolSceneViews.Render( request, worlds, view.HudSource );
	}

	/// <summary>
	/// A view's window that managed code presented in is going, or Source 2 is about to draw in it: its swap chain goes.
	/// </summary>
	internal static void ReleaseManaged( IntPtr window ) => ToolSceneViews.Release( window );

	/// <summary>
	/// Called from CMapView::ObjectsAt giving managed an opportunity to add a trace to the list.
	/// These then get sorted by distance and selected.
	/// </summary>
	internal static bool TraceManagedGizmos( MapView view, Vector2 mousePos, ref NativeHammer.HitInfo_t hitInfo )
	{
		var frame = view.GizmoInstance.current;

		if ( string.IsNullOrEmpty( frame.HoveredPath ) )
		{
			return false;
		}

		hitInfo.m_flTraceT = frame.HitDistance;

		return true;
	}
}
