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

	// A released swapchain is destroyed at frame end and owns our window until then
	private bool _destroyPending;

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
		// Keep ancestors non-native, otherwise every dock splitter above us becomes a
		// real HWND and resizes recurse through Win32 until stack overflow. Must be
		// set before WA_NativeWindow. See qtoolscenewidget.cpp for the full story.
		SetFlag( Flag.WA_DontCreateNativeAncestors, true );
		SetFlag( Flag.WA_NativeWindow, true );
		SetFlag( Flag.WA_PaintOnScreen, true );
		SetFlag( Flag.WA_NoSystemBackground, true );
		SetFlag( Flag.WA_OpaquePaintEvent, true );

		// On Linux/XWayland, Qt's software paint cycle fires on mouse-enter and click
		// expose events, briefly clearing the native window before the next SwapChain
		// present and causing a visible flash. Since all rendering goes through the
		// SwapChain, the Qt paint path serves no purpose and can be suppressed entirely.
		OnPaintOverride = () => true;

		SwapChain = WidgetUtil.CreateSwapChain( _widget, RenderSettings.Instance.AntiAliasQuality.ToEngine() );
		RenderSettings.Instance.OnVideoSettingsChanged += HandleVideoChanged;

		FocusMode = FocusMode.Click; // If we're focused we're probably accepting input, don't let tab blur us

		All.Add( this );
	}

	internal override void NativeShutdown()
	{
		base.NativeShutdown();

		All.Remove( this );
		RenderSettings.Instance.OnVideoSettingsChanged -= HandleVideoChanged;

		ReleaseSwapChain();

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

		if ( SwapChain == default ) return;

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

	// Qt destroys and recreates our native window when a dock reparents us into
	// another window - release the swapchain and Render will rebuild it against
	// the new handle.
	internal override void OnWinIdChanged()
	{
		ReleaseSwapChain();
	}

	void ReleaseSwapChain()
	{
		if ( SwapChain == default ) return;

		// The swapchain might still be in use by native, so defer its destruction until the end of the frame.
		// Otherwise, a race condition could occur where render targets are accessed after destruction, causing a delayed crash.
		var swapChain = SwapChain;
		_destroyPending = true;
		EngineLoop.DisposeAtFrameEnd( new Sandbox.Utility.DisposeAction( () =>
		{
			g_pRenderDevice.DestroySwapChain( swapChain );
			_destroyPending = false;
		} ) );
		SwapChain = default;
	}

	void Render()
	{
		if ( !Scene.IsValid() ) return;
		if ( !Visible ) return;

		if ( SwapChain == default )
		{
			// The retired swapchain still owns our window until it's destroyed at
			// frame end - creating a second one against it would fail, wait a frame.
			if ( _destroyPending ) return;

			SwapChain = WidgetUtil.CreateSwapChain( _widget, RenderSettings.Instance.AntiAliasQuality.ToEngine() );
		}

		using ( Scene.Push() )
		{
			using ( GizmoInstance.Push() )
			{
				PreFrame();
				RenderScene();
			}
		}

		if ( GameMode.IsPlayWidget( this ) )
		{
			CCameraRenderer.RenderOverlay( SwapChain );
		}

		g_pRenderDevice.Present( SwapChain );
	}

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
		// No swapchain right now - Render will create one with the current settings
		if ( SwapChain == default ) return;

		WidgetUtil.UpdateSwapChainMSAA( SwapChain, RenderSettings.Instance.AntiAliasQuality.ToEngine() );
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
