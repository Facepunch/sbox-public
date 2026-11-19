using Sandbox.Engine.Settings;
using System;

namespace Editor;

/// <summary>
/// Render a scene to a native widget. This replaces NativeRenderingWidget. 
/// </summary>
public class SceneRenderingWidget : Frame
{
	private static readonly HashSet<SceneRenderingWidget> All = new();

	bool renderingStopped;
	bool renderFailed;

	// The widget presents on Borealis.Gpu, a swap chain on its native window (docs/reference/borealis-gpu-path.md, the editor)
	BorealisGpuWindow borealis;

	// Qt changed the widget's native window id since the last frame (FollowNativeWindow)
	bool winIdChanged;

	/// <summary>The texture the widget's last frame on Borealis.Gpu was drawn into, as it was presented.</summary>
	internal Texture BorealisGpuTarget => borealis?.Target;

	/// <summary>
	/// The active scene that we're rendering
	/// </summary>
	public Scene Scene { get; set; }

	/// <summary>
	/// The camera to render from. We will fallback to Scene.Camera if this is null
	/// </summary>
	public CameraComponent Camera { get; set; }

	/// <summary>
	/// This widget manages it's own gizmo instance.
	/// </summary>
	public Gizmo.Instance GizmoInstance { get; private set; } = new();

	public bool EnableEngineOverlays { get; set; } = false;

	protected override void OnMousePress( MouseEvent e )
	{
		QueueGizmoPointerEvent( e, true );
		base.OnMousePress( e );
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		QueueGizmoPointerEvent( e, false );
		base.OnMouseReleased( e );
	}

	protected override void OnBlur( FocusChangeReason reason )
	{
		GizmoInstance.CancelPointerInput();
		base.OnBlur( reason );
	}

	void QueueGizmoPointerEvent( MouseEvent e, bool pressed )
	{
		if ( !e.LeftMouseButton || GetSceneCamera() is not { } camera ) return;
		var input = GizmoInstance.Input;
		input.Camera = camera;
		input.CursorPosition = e.LocalPosition;
		input.CursorRay = camera.GetRay( e.LocalPosition, Size );
		input.Modifiers = e.KeyboardModifiers;
		input.IsHovered = LocalRect.IsInside( e.LocalPosition );
		input.LeftMouse = pressed;
		input.DoubleClick = e.IsDoubleClick;
		GizmoInstance.QueuePointerEvent( input );
	}

	// Track if we've locked this widget's size for recording
	private bool _sizeLockedForRecording;
	private Vector2 _savedMinSize;
	private Vector2 _savedMaxSize;

	public SceneRenderingWidget( Widget parent = null ) : base( parent )
	{
		SetFlag( Flag.WA_NativeWindow, true );
		SetFlag( Flag.WA_PaintOnScreen, true );
		SetFlag( Flag.WA_NoSystemBackground, true );
		SetFlag( Flag.WA_OpaquePaintEvent, true );

		// On Linux/XWayland, Qt's software paint cycle fires on mouse-enter and click
		// expose events, briefly clearing the native window before the next swap chain
		// present and causing a visible flash. Since all rendering goes through the
		// swap chain, the Qt paint path serves no purpose and can be suppressed entirely.
		OnPaintOverride = () => true;

		borealis = BorealisGpuWindow.FromNativeWindow( (BorealisGpuRenderBackend)RenderBackend.Default, _widget.winId(), Size * DpiScale, vsync: false );
		if ( borealis is not null ) borealis.Samples = SampleCount( RenderSettings.Instance.AntiAliasQuality.ToEngine() );
		RenderSettings.Instance.OnVideoSettingsChanged += HandleVideoChanged;

		FocusMode = FocusMode.Click; // If we're focused we're probably accepting input, don't let tab blur us

		All.Add( this );
	}

	internal override void OnDestroyingLater()
	{
		ReleaseSwapChain();
		base.OnDestroyingLater();
	}

	internal override void NativeDestroying() => ReleaseSwapChain();

	/// <summary>
	/// Qt reset the widget's native window id or gave it one: it was reparented (a widget made without a parent, then put
	/// in a layout), docked or undocked. The id isn't asked for here: <c>winId()</c> makes the window again while Qt is
	/// between the two, sending this event again inside this one, and both made a swap chain on the one window. The next
	/// frame looks (<see cref="FollowNativeWindow"/>).
	/// </summary>
	internal override void OnWinIdChanged()
	{
		base.OnWinIdChanged();
		winIdChanged = true;
	}

	/// <summary>
	/// After Qt changed the widget's native window id: keep the swap chain when the window is still the one it presents
	/// to (a reparent keeps it), or move it to the new one. False with nothing to present to.
	/// </summary>
	bool FollowNativeWindow()
	{
		if ( !winIdChanged || renderingStopped ) return borealis is not null;
		winIdChanged = false;

		var window = _widget.winId();
		if ( borealis is not null && borealis.NativeWindow == window ) return true;

		var samples = borealis?.Samples ?? SampleCount( RenderSettings.Instance.AntiAliasQuality.ToEngine() );
		borealis?.Dispose();
		borealis = null;
		borealis = BorealisGpuWindow.MoveToNativeWindow( (BorealisGpuRenderBackend)RenderBackend.Default, window, Size * DpiScale );
		if ( borealis is not null ) borealis.Samples = samples;
		return borealis is not null;
	}

	internal static void ShutdownRendering()
	{
		foreach ( var widget in All.ToArray() )
			widget.ReleaseSwapChain();
	}

	void ReleaseSwapChain()
	{
		if ( renderingStopped ) return;
		renderingStopped = true;

		if ( GameMode.IsPlayWidget( this ) )
			GameMode.ClearPlayMode();

		RenderSettings.Instance.OnVideoSettingsChanged -= HandleVideoChanged;

		// Destroying the swap chain waits for the device: the native window is still alive
		borealis?.Dispose();
		borealis = null;
	}

	internal override void NativeShutdown()
	{
		base.NativeShutdown();
		All.Remove( this );

		GizmoInstance?.Dispose();
		GizmoInstance = default;
	}

	/// <summary>
	/// Create a hidden scene editor camera, post processing will be copied from a main camera in the scene.
	/// </summary>
	public CameraComponent CreateSceneEditorCamera()
	{
		if ( Scene is null ) return null;

		using ( Scene.Push() )
		{
			var go = new GameObject( true, "editor_camera" );
			go.Flags = GameObjectFlags.Hidden | GameObjectFlags.NotSaved | GameObjectFlags.EditorOnly | GameObjectFlags.Absolute;
			var camera = go.AddComponent<CameraComponent>();
			camera.RenderExcludeTags.Add( "hidden" );
			camera.IsMainCamera = false;
			camera.IsSceneEditorCamera = true;
			return camera;
		}
	}

	void RenderScene()
	{
		if ( !this.IsValid() )
			return;

		if ( borealis is null ) return;

		var sceneCamera = GetSceneCamera();
		if ( sceneCamera is not null )
		{
			sceneCamera.EnableEngineOverlays = EnableEngineOverlays;
		}

		// Set the recording camera for video/screenshot recording (only if this widget has focus)
		if ( sceneCamera is not null && IsFocused )
		{
			SceneCamera.RecordingCamera = sceneCamera;
		}

		// Lock widget size during recording to prevent resolution changes
		if ( ScreenRecorder.IsRecording() && sceneCamera?.IsRecordingCamera == true && !_sizeLockedForRecording )
		{
			_savedMinSize = MinimumSize;
			_savedMaxSize = MaximumSize;
			MinimumSize = Size;
			MaximumSize = Size;
			_sizeLockedForRecording = true;
		}
		else if ( !ScreenRecorder.IsRecording() && _sizeLockedForRecording )
		{
			MinimumSize = _savedMinSize;
			MaximumSize = _savedMaxSize;
			_sizeLockedForRecording = false;
		}

		if ( Camera.IsValid() )
		{
			Camera.Scene?.PreCameraRender();
			Camera.AddToRenderList( default, Size * DpiScale );
		}
		else if ( Scene.IsValid() )
		{
			Scene.Render( default, Size * DpiScale );
		}
	}

	/// <inheritdoc cref="PreFrame"/>
	public event Action OnPreFrame;

	/// <summary>
	/// Called just before rendering.
	/// </summary>
	protected virtual void PreFrame()
	{
		OnPreFrame?.Invoke();
	}

	/// <summary>
	/// Update common inputs for gizmo.
	/// </summary>
	public void UpdateGizmoInputs( bool hasMouseFocus = true )
	{
		var camera = GetSceneCamera();
		if ( camera is null ) return;

		var component = Camera.IsValid() ? Camera : Scene.Camera;
		component.UpdateSceneCameraTransform( camera );

		UpdateGizmoInputs( ref GizmoInstance.Input, camera, hasMouseFocus );
		if ( Gizmo.Active == GizmoInstance )
			GizmoInstance.RefreshInput();
	}

	void Render()
	{
		if ( !Scene.IsValid() ) return;
		if ( !Visible ) return;

		if ( FollowNativeWindow() ) RenderOnBorealisGpu();
	}

	/// <summary>
	/// A frame on Borealis.Gpu, as the game window draws one: the camera (or the scene's) draws into the widget's target
	/// (<see cref="BorealisGpuWindow.Drawing"/>), then the play widget's overlay, in the block that copies it into the
	/// acquired image, which is presented straight after. A widget with no pixels draws nothing.
	/// </summary>
	void RenderOnBorealisGpu()
	{
		using var _ = FrameTiming.Scope();
		if ( !borealis.Update( Size * DpiScale ) ) return;

		var play = GameMode.IsPlayWidget( this );
		using ( Scene.PushScope() )
		{
			using ( GizmoInstance.PushScope() )
			{
				PreFrame();
				if ( !borealis.BeginFrame( Color.Black ) ) return;

				try
				{
					RenderScene();
				}
				finally
				{
					borealis.EndFrame( play ? Graphics.RenderOverlay : NoOverlay, screenshots: play );
				}
			}
		}

		borealis.PresentFrame();
	}

	static readonly Action NoOverlay = () => { };

	// Where a widget's frame on Borealis.Gpu goes, in the dev tools' timings
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings FrameTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu widget" );

	/// <summary>Samples per pixel the widget's scenes render with on Borealis.Gpu: the setting, as the device allows it.</summary>
	static int SampleCount( NativeEngine.RenderMultisampleType multisample ) => Graphics.RenderMultiSampleToNum( NativeEngine.RenderDeviceManager.GetBestMultisampleType( multisample ) );

	private void UpdateGizmoInputs( ref Gizmo.Inputs input, SceneCamera camera, bool hasMouseFocus )
	{
		ArgumentNullException.ThrowIfNull( camera );

		input.Camera = camera;
		input.Modifiers = Application.KeyboardModifiers;

		if ( !hasMouseFocus )
		{
			GizmoInstance.CancelPointerInput();
			input.LeftMouse = false;
			input.RightMouse = false;
			input.CursorRay = new Ray();
			return;
		}

		input.CursorPosition = Application.CursorPosition;
		input.LeftMouse = Application.MouseButtons.HasFlag( MouseButtons.Left );
		input.RightMouse = Application.MouseButtons.HasFlag( MouseButtons.Right );

		input.CursorPosition -= ScreenPosition;
		input.CursorRay = camera.GetRay( input.CursorPosition, Size );

	}

	private SceneCamera GetSceneCamera()
	{
		if ( Camera.IsValid() )
			return Camera.SceneCamera;

		if ( !Scene.IsValid() )
			return null;

		if ( !Scene.Camera.IsValid() )
			return null;

		return Scene.Camera.SceneCamera;
	}

	/// <summary>
	/// Return a ray for the current cursor position
	/// </summary>
	public Ray CursorRay
	{
		get => GetRay( Application.CursorPosition - ScreenPosition );
	}

	/// <summary>
	/// Given a local widget position, return a Ray
	/// </summary>
	public Ray GetRay( Vector2 localPosition )
	{
		var camera = GetSceneCamera();
		if ( camera is null )
			return default;

		return camera.GetRay( localPosition, Size );
	}

	internal void HandleVideoChanged()
	{
		if ( renderingStopped ) return;
		var aa = RenderSettings.Instance.AntiAliasQuality.ToEngine();

		if ( borealis is not null ) borealis.Samples = SampleCount( aa );
	}

	internal static void RenderAll()
	{
		foreach ( var widget in All )
		{
			if ( !widget.Visible ) continue;

			try
			{
				widget.Render();
			}
			catch ( Exception e )
			{
				if ( widget.renderFailed ) continue;
				widget.renderFailed = true;
				Log.Error( e, $"Exception rendering {widget.Scene}" );
			}
		}
	}
}
