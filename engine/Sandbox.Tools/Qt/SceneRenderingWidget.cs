using Sandbox.Engine.Settings;
using System;

namespace Editor;

/// <summary>
/// Render a scene to a native widget. This replaces NativeRenderingWidget. 
/// </summary>
public class SceneRenderingWidget : Frame
{
	private static readonly HashSet<SceneRenderingWidget> All = new();

	internal SwapChainHandle_t SwapChain;
	bool renderingStopped;

	// With -borealisgpu: the widget presents on Borealis.Gpu, a swap chain on its native window, in place of Source 2's
	// (docs/managed/borealis-gpu-path.md, the editor)
	BorealisGpuWindow borealis;

	/// <summary>Whether the widget draws and presents on Borealis.Gpu.</summary>
	internal bool OnBorealisGpu => borealis is not null;

	/// <summary>The texture the widget's last frame on Borealis.Gpu was drawn into, as it was presented. Null on Source 2.</summary>
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
		// expose events, briefly clearing the native window before the next SwapChain
		// present and causing a visible flash. Since all rendering goes through the
		// SwapChain, the Qt paint path serves no purpose and can be suppressed entirely.
		OnPaintOverride = () => true;

		var aa = RenderSettings.Instance.AntiAliasQuality.ToEngine();
		if ( RenderBackend.Default is BorealisGpuRenderBackend gpu )
			borealis = BorealisGpuWindow.FromNativeWindow( gpu, _widget.winId(), Size * DpiScale, vsync: false );

		if ( borealis is not null ) borealis.Samples = SampleCount( aa );
		else SwapChain = WidgetUtil.CreateSwapChain( _widget, aa );
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

	internal override void OnWinIdChanged()
	{
		base.OnWinIdChanged();
		if ( borealis is null || renderingStopped ) return;

		// Qt gave the widget a new native window (it was docked or undocked): the swap chain moves to it
		var samples = borealis.Samples;
		borealis.Dispose();
		borealis = BorealisGpuWindow.FromNativeWindow( (BorealisGpuRenderBackend)RenderBackend.Default, _widget.winId(), Size * DpiScale, vsync: false );
		if ( borealis is not null ) borealis.Samples = samples;
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

		if ( SwapChain == default ) return;
		// Finish outstanding rendering while the native window is still alive.
		g_pRenderDevice.Flush();
		g_pRenderDevice.ForceFlushGPU( SwapChain );
		g_pRenderDevice.DestroySwapChain( SwapChain );
		SwapChain = default;
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

		if ( SwapChain == default && borealis is null ) return;

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
			Camera.AddToRenderList( SwapChain, Size * DpiScale );
		}
		else if ( Scene.IsValid() )
		{
			Scene.Render( SwapChain, Size * DpiScale );
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

		UpdateGizmoInputs( ref GizmoInstance.Input, camera, hasMouseFocus );
	}

	void Render()
	{
		if ( !Scene.IsValid() ) return;
		if ( !Visible ) return;

		if ( borealis is not null )
		{
			RenderOnBorealisGpu();
			return;
		}

		if ( SwapChain == default ) return;

		using ( Scene.PushScope() )
		{
			using ( GizmoInstance.PushScope() )
			{
				PreFrame();
				RenderScene();
			}
		}

		if ( SwapChain == default ) return;

		if ( GameMode.IsPlayWidget( this ) )
		{
			CCameraRenderer.RenderOverlay( SwapChain );
		}

		g_pRenderDevice.Present( SwapChain );
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
			input.CursorRay = new Ray();
			return;
		}

		input.CursorPosition = Application.CursorPosition;
		input.LeftMouse = Application.MouseButtons.HasFlag( MouseButtons.Left );
		input.RightMouse = Application.MouseButtons.HasFlag( MouseButtons.Right );

		input.CursorPosition -= ScreenPosition;
		input.CursorRay = camera.GetRay( input.CursorPosition, Size );

		if ( !input.IsHovered )
		{
			input.LeftMouse = false;
			input.RightMouse = false;
		}
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

		if ( borealis is not null )
		{
			borealis.Samples = SampleCount( aa );
			return;
		}

		if ( SwapChain == default )
		{
			SwapChain = WidgetUtil.CreateSwapChain( _widget, aa );
			return;
		}

		WidgetUtil.UpdateSwapChainMSAA( SwapChain, aa );
	}

	internal static void RenderAll()
	{
		foreach ( var widget in All )
		{
			if ( !widget.Visible ) continue;

			widget.Render();
		}
	}
}
