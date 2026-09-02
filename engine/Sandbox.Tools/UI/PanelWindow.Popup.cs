using NativeEngine;
using Sandbox.Engine.Settings;
using Sandbox.UI;
using System;

namespace Editor;

public sealed partial class PanelWindow
{
	bool _isPopup;
	PanelWindow _parent;
	Vector2 _pendingPosition;
	Vector2 _pendingSize;

	bool IPanelWindow.IsPopup => _isPopup;

	/// <summary>
	/// Open a popup - a borderless window that sits above its parent and can hang outside it, the
	/// way an OS menu does. The position is in the parent's client pixels.
	/// </summary>
	public static PanelWindow Popup( PanelWindow parent, Vector2 position, Vector2 size )
	{
		ArgumentNullException.ThrowIfNull( parent );

		// SDL popup windows position themselves relative to their parent
		return new PanelWindow( parent, position / parent.Surface.DpiScale, size );
	}

	PanelWindow( PanelWindow parent, Vector2 localPosition, Vector2 size )
	{
		ThreadSafe.AssertIsMainThread();

		_isPopup = true;
		_shown = false;
		SizeToContents = true;
		Borderless = true;

		_parent = parent;
		_pendingSize = size;
		_pendingPosition = localPosition;

		Surface = new UISurface { DpiScale = parent.Surface.DpiScale, Size = size };
		Surface.OnCursorChanged = x => _cursor = x;

		// The OS rounds and clips this window like its own menus - the styles square off
		// what would double-round inside that clip
		Surface.Root.AddClass( "os-popup" );

		_all.Add( this );
		PanelWindows.Register( this );
	}

	/// <summary>
	/// Make the OS window. Popups wait until the frame boundary for this - building a swap chain
	/// while another window is mid-render leaves it in a state that never presents.
	/// </summary>
	void CreateNativeWindow()
	{
		_window = PanelWindowNative.CreatePopup( _parent._window, (int)_pendingPosition.x, (int)_pendingPosition.y, (int)_pendingSize.x, (int)_pendingSize.y );
		if ( _window == IntPtr.Zero )
			throw new Exception( "Couldn't create the popup" );

		_swapChain = PanelWindowNative.CreateSwapChain( _window, (int)RenderSettings.Instance.AntiAliasQuality.ToEngine(), false );
		_swapChainSize = Size;

		_world = new SceneWorld();

		_camera = new SceneCamera( "PanelWindow Popup" )
		{
			World = _world,

			// Opaque - the compositor rounds the window's corners itself. Windows doesn't
			// composite swapchain alpha, so drawing our own round corners isn't an option.
			BackgroundColor = Color.Black,
			ClearFlags = ClearFlags.All,
			EnablePostProcessing = false,
			ZNear = 1,
			ZFar = 1000,
		};
	}
}
