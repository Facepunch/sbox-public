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
		PlayWidgetInput.For( this )?.OnMouseButton( e.Button, e.LocalPosition, true );
		base.OnMousePress( e );
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		QueueGizmoPointerEvent( e, false );
		PlayWidgetInput.For( this )?.OnMouseButton( e.Button, e.LocalPosition, false );
		base.OnMouseReleased( e );
	}

	protected override void OnMouseWheel( WheelEvent e )
	{
		PlayWidgetInput.For( this )?.OnMouseWheel( e );
		base.OnMouseWheel( e );
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		if ( PlayWidgetInput.For( this )?.OnKey( true, e ) == true ) return;
		base.OnKeyPress( e );
	}

	protected override void OnKeyRelease( KeyEvent e )
	{
		if ( PlayWidgetInput.For( this )?.OnKey( false, e ) == true ) return;
		base.OnKeyRelease( e );
	}

	// Qt moves focus on Tab before the press reaches OnKeyPress, unless the widget says it keeps focus:
	// while the game has the play widget's keys, Tab is the game's
	protected override bool FocusNext() => PlayWidgetInput.For( this ) is not null && PlayWidgetInput.HasGameFocus || base.FocusNext();

	protected override bool FocusPrevious() => PlayWidgetInput.For( this ) is not null && PlayWidgetInput.HasGameFocus || base.FocusPrevious();

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
		DisposeWindow();
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
		DisposeWindow();
	}

	/// <summary>
	/// Let go of the window's swap chain: once the render thread has drawn the frame handed to it (which may be this window's),
	/// with an image it acquired ahead for the window presented empty, so the swap chain goes with none acquired.
	/// </summary>
	void DisposeWindow()
	{
		if ( borealis is null ) return;
		BorealisGpuRenderBackend.JoinRenderThread();
		borealis.PresentAhead();
		borealis.Dispose();
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

		PrepareSceneCamera();

		if ( Camera.IsValid() )
		{
			Camera.Scene?.PrepareSceneForRendering();
			Camera.AddToRenderList( default, Size * DpiScale );
		}
		else if ( Scene.IsValid() )
		{
			Scene.Render( default, Size * DpiScale );
		}
	}

	/// <summary>
	/// The scene camera's engine overlays, the recording camera and the widget's size while recording, before the frame draws.
	/// </summary>
	void PrepareSceneCamera()
	{
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
				if ( play && DrawsGame() )
				{
					DrawGameView();
					return;
				}

				if ( this == handOver && DrawSceneView() ) return;

				if ( !borealis.BeginFrame( Color.Black ) ) return;

				try
				{
					try
					{
						RenderScene();
					}
					finally
					{
						borealis.EndFrame( play ? Graphics.RenderOverlay : NoOverlay, screenshots: play );
					}
				}
				finally
				{
					// The acquired image has to be presented whatever the scene threw, as the game window's is: left unpresented,
					// every later frame fails to acquire and the widget keeps showing its last frame
					borealis.PresentFrame();
				}
			}
		}
	}

	static readonly Action NoOverlay = () => { };

	// The widget whose frame goes to the render thread this frame while no game plays in a play widget (RenderAll)
	static SceneRenderingWidget handOver;

	/// <summary>
	/// The editor's own view of a scene (the scene view, an ejected camera) drawn as the game view is (<see cref="DrawGameView"/>):
	/// its camera prepared here, with the gizmos and editor drawing <see cref="PreFrame"/> made written for it, then the drawing
	/// and the present handed to the render thread. False when it isn't drawn this way (no camera of its own, a reason to draw
	/// here, no render thread): the caller draws it here as before.
	/// </summary>
	bool DrawSceneView()
	{
		if ( !Camera.IsValid() || RenderBackend.Default is not BorealisGpuRenderBackend backend ) return false;
		if ( Sandbox.Engine.GameWindow.DrawsHereAnyway() is not null ) return false;

		PrepareSceneCamera();
		drawnScene = Camera.Scene;
		drawnWindow = borealis;
		drawnSize = Size * DpiScale;
		drawnCameras.Clear();
		drawsGame = false;
		Sandbox.Rendering.GameDrawExtract.BeginFrame();
		using ( ExtractZone.Start() )
		{
			drawnScene?.PrepareSceneForRendering();
			if ( Camera.PrepareForRenderList() ) drawnCameras.Add( Camera );
			Sandbox.Rendering.GameDrawExtract.Extract( drawnCameras, record: true );
		}

		// What this frame changed in the scene and the gizmos' world since the frame's sync point, written for the drawing
		Sandbox.Rendering.RenderSync.Run();

		if ( Sandbox.Rendering.RenderSync.GameDrawsInFrame is null && backend.HandOverRenderFrame( borealis, drawFrame ??= DrawFrame ) ) return true;

		Sandbox.Rendering.GameDrawExtract.Learning( true );
		try
		{
			DrawFrame();
		}
		finally
		{
			Sandbox.Rendering.GameDrawExtract.Learning( false );
		}

		return true;
	}

	/// <summary>
	/// Whether this play widget draws the game's own cameras, as the game window does: playing, through no camera of the
	/// editor's (not ejected), with no load showing.
	/// </summary>
	bool DrawsGame() => !Camera.IsValid() && Game.IsPlaying && !Scene.IsLoading && !LoadingScreen.IsVisible && !Networking.IsConnecting
		&& RenderBackend.Default is BorealisGpuRenderBackend;

	// What the game view's frame draws, prepared by the extract: its scene's cameras in order, the window and the size they draw
	// at. The render thread reads them until the next frame's extract, which joins it first
	readonly List<CameraComponent> drawnCameras = new();
	Scene drawnScene;
	BorealisGpuWindow drawnWindow;
	Vector2 drawnSize;
	bool drawsGame;
	Action drawFrame;

	static readonly Sandbox.Utility.Superluminal ExtractZone = new( "Game View Extract", Color.Cyan );

	/// <summary>
	/// The game view's frame, as the game window's (docs/reference/borealis-render-thread.md, the editor): the game's half here
	/// (the extract: its cameras prepared, with every game hook that runs for them), then the drawing (the cameras, the overlay,
	/// the present) handed to the render thread, while the main thread ends the frame and runs the editor and the next frame's
	/// game code. It draws here instead when game code would run in the drawing (a game's custom object the extract didn't
	/// record), something reads the frame on this thread (the dev tools, a recording) or there's no render thread.
	/// </summary>
	void DrawGameView()
	{
		PrepareSceneCamera();
		var backend = (BorealisGpuRenderBackend)RenderBackend.Default;
		var here = Sandbox.Engine.GameWindow.DrawsHereAnyway();

		drawnScene = Scene;
		drawnWindow = borealis;
		drawnSize = Size * DpiScale;
		drawsGame = true;
		Sandbox.Rendering.GameDrawExtract.BeginFrame();
		using ( ExtractZone.Start() )
		{
			Scene.ExtractRender( drawnCameras );
			Sandbox.Rendering.GameDrawExtract.Extract( drawnCameras, record: here is null );
		}

		here ??= Sandbox.Rendering.RenderSync.GameDrawsInFrame;
		if ( here is null && backend.HandOverRenderFrame( borealis, drawFrame ??= DrawFrame ) ) return;

		// Drawn here, a game's custom objects run their drawing, which teaches the extract what they draw
		Sandbox.Rendering.GameDrawExtract.Learning( true );
		try
		{
			DrawFrame();
		}
		finally
		{
			Sandbox.Rendering.GameDrawExtract.Learning( false );
		}
	}

	/// <summary>
	/// The game view's drawing and present: on the render thread, where a throw is said and the frame dropped (its image
	/// presented as drawn so far), or here.
	/// </summary>
	void DrawFrame()
	{
		if ( !ThreadSafe.IsRenderThread )
		{
			try
			{
				DrawGame();
			}
			finally
			{
				drawnWindow.PresentFrame();
			}

			return;
		}

		try
		{
			DrawGame();
		}
		catch ( Exception e ) when ( !(e is Borealis.Gpu.GpuException { IsDeviceLost: true }) )
		{
			if ( BorealisGpuErrors.ShouldLog( e ) ) Log.Warning( e, $"The render thread's frame of the game view threw: {e.Message}" );
		}

		drawnWindow.PresentFrame();
	}

	void DrawGame()
	{
		if ( !drawnWindow.BeginFrame( Color.Black ) ) return;

		try
		{
			drawnScene.DrawRender( drawnCameras, default, drawnSize );
		}
		finally
		{
			drawnWindow.EndFrame( drawsGame ? Graphics.RenderOverlay : NoOverlay, screenshots: drawsGame );
		}
	}

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
		var play = GameMode.PlayWidget;
		var renderThread = BorealisGpuFrameThread.Draws && RenderBackend.Default is BorealisGpuRenderBackend;
		var playing = play is not null && Game.IsPlaying;

		// While no game plays in a play widget, the editor's scene view goes to the render thread instead
		handOver = renderThread && !playing ? SceneViewToHandOver() : null;
		Sandbox.Rendering.RenderSync.DeferForRenderThread( renderThread && (playing || handOver is not null) );

		// The play widget or the scene view last: its frame goes to the render thread, and the other widgets' frames don't wait for it
		var last = play ?? handOver;
		foreach ( var widget in All )
		{
			if ( widget.Visible && widget != last ) widget.RenderCatching();
		}

		if ( last is { Visible: true } ) last.RenderCatching();
	}

	/// <summary>
	/// The widget drawing the editor's view of a scene through its own camera that's handed to the render thread: the first visible
	/// one with a scene editor camera, or null.
	/// </summary>
	static SceneRenderingWidget SceneViewToHandOver()
	{
		foreach ( var widget in All )
		{
			if ( widget.Visible && !widget.renderingStopped && widget.Camera is { IsSceneEditorCamera: true } camera && camera.IsValid() && widget.Scene.IsValid() ) return widget;
		}

		return null;
	}

	void RenderCatching()
	{
		try
		{
			Render();
		}
		catch ( Exception e )
		{
			if ( renderFailed ) return;
			renderFailed = true;
			Log.Error( e, $"Exception rendering {Scene}" );
		}
	}
}
