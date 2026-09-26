using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Modals;
using Sandbox.Rendering;
using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// Holds onto a list of root panels to tick, input and draw
/// </summary>
internal partial class UISystem
{

	internal PanelInput Input { get; set; } = new();

	internal readonly CommandList GlobalCommandList = new();

	internal List<RootPanel> RootPanels = new();
	internal List<Panel> DeletionList = new();
	internal InputEventQueue InputEventQueue = new();

	/// <summary>
	/// Roots participating in frame and input processing. Keep RootPanels for maintenance and teardown.
	/// Use indices because panel callbacks can add or remove roots during iteration.
	/// </summary>
	internal IEnumerable<RootPanel> GetActiveRoots( bool reverse = false )
	{
		var step = reverse ? -1 : 1;

		for ( var i = reverse ? RootPanels.Count - 1 : 0; i >= 0 && i < RootPanels.Count; i += step )
		{
			var root = RootPanels[i];
			if ( root is { IsActive: true } ) yield return root;
		}
	}

	/// <summary>
	/// Tooltips for the panels in this UI. Each instance has its own, so a tooltip in one window
	/// has nothing to do with the game screen's.
	/// </summary>
	internal TooltipSystem Tooltips { get; } = new();

	/// <summary>
	/// Where popups in this UI open. Null floats them in the panel root, which is what a game
	/// wants; a window puts each one in an OS window of its own.
	/// </summary>
	internal IPopupHost PopupHost { get; set; }

	// focus
	internal Panel CurrentFocus { get; set; }
	internal Panel NextFocus { get; set; }
	internal bool FocusPendingChange { get; set; }

	/// <summary>
	/// The deepest panel at this position across every root, topmost root first.
	/// </summary>
	internal Panel FindPanelAt( Vector2 position )
	{
		foreach ( var root in GetActiveRoots( reverse: true ) )
		{
			var hit = UISurface.FindPanelAt( root, position, null );
			if ( hit is not null ) return hit;
		}

		return null;
	}

	internal void AddRoot( RootPanel rootPanel )
	{
		if ( RootPanels.Contains( rootPanel ) )
			throw new System.Exception( "Adding root panel twice" );

		RootPanels.Add( rootPanel );
	}

	internal void RemoveRoot( RootPanel rootPanel )
	{
		RootPanels.Remove( rootPanel );
	}

	internal void DeleteAllRoots()
	{
		var deleteList = new List<RootPanel>( RootPanels );
		foreach ( var rootPanel in deleteList )
		{
			rootPanel.Delete( true );

			// User can override Delete/OnDelete, so let's make sure we always remove from lists
			rootPanel.RemoveFromLists();
			RootPanels.Remove( rootPanel );
		}

		RunDeferredDeletion( true );
		DeletionList.Clear();
	}

	internal void Render( float opacity = 1.0f )
	{
		using ( Performance.Scope( "Execute Command Lists" ) )
		{
			Graphics.Attributes.SetCombo( "D_WORLDPANEL", 0 );
			GlobalCommandList.ExecuteOnRenderThread();
		}
	}

	internal void CombineCommandLists()
	{
		GlobalCommandList.Reset();

		foreach ( var root in GetActiveRoots( reverse: true ) )
		{
			if ( root.RenderedManually || root.IsWorldPanel ) continue;

			GlobalCommandList.InsertList( root.PanelCommandList );
		}
	}

	internal void Simulate( bool allowMouseInput )
	{
		using ( Performance.Scope( "Update Screen Size" ) )
		{
			Screen.UpdateFromEngine();
			Size = Screen.Size;
			DpiScale = Screen.DesktopScale;
		}

		using ( Performance.Scope( "Tick Panels" ) )
		{
			TickPanels();
		}

		using ( Performance.Scope( "Tick Input" ) )
		{
			TickInput( allowMouseInput );
		}

		LayoutAndBuild();
	}

	/// <summary>
	/// Lay every root panel out at <see cref="Size"/> and turn them into command lists. This is
	/// the half of the frame that has nothing to do with input, so any surface can drive it.
	/// </summary>
	internal void LayoutAndBuild()
	{
		using ( Performance.Scope( "Pre Layout" ) )
		{
			PreLayout();
		}

		using ( Performance.Scope( "Deferred Deletion" ) )
		{
			RunDeferredDeletion();
		}

		using ( Performance.Scope( "Layout" ) )
		{
			Layout();
		}

		using ( Performance.Scope( "Post Layout" ) )
		{
			PostLayout();
		}

		using ( Performance.Scope( "Deferred Deletion" ) )
		{
			RunDeferredDeletion();
		}

		using ( Performance.Scope( "Build Command Lists" ) )
		{
			BuildCommandLists();
		}

		using ( Performance.Scope( "Combine Command Lists" ) )
		{
			CombineCommandLists();
		}
	}

	internal void DirtyAllStyles()
	{
		for ( int i = RootPanels.Count() - 1; i >= 0; i-- )
		{
			if ( !RootPanels[i].IsValid ) continue;
			RootPanels[i].DirtyStylesRecursive();
		}
	}

	internal void TickPanels()
	{
		RootPanels.RemoveAll( x => x == null );

		// Release held input before skipping a suspended scene's panels.
		if ( NextFocus?.Scene?.IsSuspended == true ) ReleaseFocusSubtree( NextFocus );
		if ( CurrentFocus?.Scene?.IsSuspended == true ) ReleaseFocusSubtree( CurrentFocus );
		if ( Panel.MouseCapture?.Scene?.IsSuspended == true ) Panel.MouseCapture.SetMouseCapture( false );

		if ( Input.Active?.Scene?.IsSuspended == true || Input.Hovered?.Scene?.IsSuspended == true )
		{
			Input.CancelPointerInteraction();
			Input.SetHovered( null );
			Input.Clear();
			Tooltips.Clear();
		}

		foreach ( var root in GetActiveRoots() )
		{
			root.TickInternal();
		}
	}

	internal void PreLayout()
	{
		var screenRect = new Rect( 0, 0, Size.x, Size.y );

		foreach ( var root in GetActiveRoots() )
		{
			root.PreLayout( screenRect );
		}
	}

	internal void Layout()
	{
		foreach ( var root in GetActiveRoots() )
		{
			root.CalculateLayout();
		}
	}

	internal void PostLayout()
	{
		foreach ( var root in GetActiveRoots() )
		{
			root.PostLayout();
		}
	}

	internal void BuildCommandLists()
	{
		ThreadSafe.AssertIsMainThread();

		foreach ( var root in GetActiveRoots() )
		{
			if ( root is Sandbox.UI.WorldPanel { SceneObject: not null } wp )
			{
				wp.SceneObject.BuildCommandList();
			}

			root.BuildCommandList();
		}
	}

	/// <summary>
	/// The input half of a frame for a surface that isn't the game screen - no game input context,
	/// no cursor visibility rules, just hover, focus and events for our own root panels.
	/// </summary>
	internal void TickSurfaceInput( bool allowMouseInput )
	{
		foreach ( var root in GetActiveRoots() )
		{
			root.TickInputInternal();
		}

		Input.Tick( GetActiveRoots().Where( p => !p.IsWorldPanel ).OrderByDescending( x => x.ComputedStyle?.ZIndex ?? 0 ), allowMouseInput );

		TickFocus();

		// With nothing focused the keys go to the root, so a surface can have window wide shortcuts
		// instead of dropping every key press
		InputEventQueue.TickFocused( CurrentFocus ?? GetActiveRoots().FirstOrDefault() );
		InputEventQueue.Tick( Input.Hovered, Input.Active );

		Tooltips.SetHovered( allowMouseInput ? Input.Hovered : null, Input.CursorPosition );
		Tooltips.Frame( Input.CursorPosition, allowMouseInput );
	}

	internal void TickInput( bool allowMouseInput )
	{
		foreach ( var root in GetActiveRoots() )
		{
			root.TickInputInternal();
		}

		//
		// Tick various input systems
		//
		Input.Tick( GetActiveRoots().Where( p => !p.IsWorldPanel ).OrderByDescending( x => x.ComputedStyle?.ZIndex ?? 0 ), allowMouseInput && DoAnyPanelsWantMouseVisible() );

		TickWorldInput();

		//
		// We tick focus here, after the layout. This way any styles that
		// were set at the same time as changing focus will be applied so
		// that when we judge elibility the logic will be correct
		//
		TickFocus();

		//
		// Send all key events to the focused panel
		//
		InputEventQueue.TickFocused( CurrentFocus );

		//
		// Pass our global input events ( mouse move, double click ) to 2d panels
		// WorldInputs simulate this themselves in WorldInputInternal.Tick
		//
		InputEventQueue.Tick( Input.Hovered, Input.Active );

		// The input router picks which UI is hovered for tooltips - this just runs the one it picked
		Tooltips.Frame( Input.CursorPosition, InputRouter.MouseCursorVisible );

		//
		// Set mouse delta to 0 so it doesn't repeat the last frame's
		// delta on the next frame
		//
		Mouse.Frame();

		bool inGame = IGameInstance.Current is not null;

		var mouseState = Sandbox.Engine.InputContext.InputState.Ignore;
		var buttonState = Sandbox.Engine.InputContext.InputState.Ignore;

		if ( Game.IsMenu )
		{
			if ( !inGame )
			{
				mouseState = InputContext.InputState.UI;
				buttonState = Sandbox.Engine.InputContext.InputState.Game;

				if ( CurrentFocus is not null )
					buttonState = CurrentFocus.ButtonInput == PanelInputType.Game ? InputContext.InputState.Game : InputContext.InputState.UI;
			}

			//
			// A modal is open
			//
			if ( (IModalSystem.Current?.HasModalsOpen() ?? false) )
			{
				mouseState = Sandbox.Engine.InputContext.InputState.UI;
				buttonState = Sandbox.Engine.InputContext.InputState.Game;

				if ( CurrentFocus is not null )
					buttonState = CurrentFocus.ButtonInput == PanelInputType.Game ? InputContext.InputState.Game : InputContext.InputState.UI;
			}

			//
			// The loading screen is visible
			//
			if ( Sandbox.LoadingScreen.IsVisible )
			{
				mouseState = Sandbox.Engine.InputContext.InputState.UI;
				buttonState = Sandbox.Engine.InputContext.InputState.UI;
			}

			//
			// The developer console / chat is open
			//
			if ( IMenuSystem.Current?.ForceCursorVisible ?? false )
			{
				mouseState = Sandbox.Engine.InputContext.InputState.UI;
				buttonState = Sandbox.Engine.InputContext.InputState.UI;
			}
		}

		//
		// If we're a game menu, and there is no client - then treat all input as game input by default.
		//
		if ( !Game.IsMenu )
		{
			mouseState = Sandbox.Engine.InputContext.InputState.Game;
			buttonState = Sandbox.Engine.InputContext.InputState.Game;

			if ( DoAnyPanelsWantMouseVisible() )
				mouseState = InputContext.InputState.UI;

			if ( CurrentFocus is not null )
				buttonState = CurrentFocus.ButtonInput == PanelInputType.Game ? InputContext.InputState.Game : InputContext.InputState.UI;

			// No input if we're not playing
			if ( !inGame )
			{
				mouseState = InputContext.InputState.UI;
				buttonState = InputContext.InputState.UI;
			}

			if ( Application.IsEditor && !Game.IsPlaying )
			{
				mouseState = InputContext.InputState.Ignore;
				buttonState = InputContext.InputState.Ignore;
			}
		}

		Game.InputContext.UpdateInputFromUI( mouseState, Input.Hovered, Panel.MouseCapture is not null, buttonState, CurrentFocus );
	}

	void TickWorldInput()
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;

		var rootPanels = scene.GetAllComponents<WorldPanel>();
		var worldInputs = scene.GetAllComponents<WorldInput>();

		foreach ( var worldInput in worldInputs )
		{
			if ( scene.IsSuspended )
			{
				worldInput.WorldPanelInput.CancelPointerInteraction();
				worldInput.WorldPanelInput.Clear();
				continue;
			}

			worldInput.WorldPanelInput.Tick( rootPanels.Select( x => x.GetPanel() as RootPanel ), true );
		}
	}

	bool DoAnyPanelsWantMouseVisible()
	{
		if ( Mouse.Visibility == MouseVisibility.Visible ) return true;
		if ( Mouse.Visibility == MouseVisibility.Hidden && !Game.IsMenu ) return false;

		foreach ( var root in GetActiveRoots() )
		{
			if ( !root.IsVisible )
				continue;

			if ( root.IsWorldPanel )
				continue;

			if ( !root.ChildrenWantMouseInput )
				continue;

			return true;
		}

		return false;
	}

	/// <summary>
	/// This panel should get deleted at some point
	/// </summary>
	internal void AddDeferredDeletion( Panel panel )
	{
		Assert.NotNull( panel );

		DeletionList.Add( panel );
	}

	/// <summary>
	/// Delete all panels that were deferred and are no longer playing outro transitions
	/// </summary>
	internal void RunDeferredDeletion( bool force = false )
	{
		for ( int i = 0; i < DeletionList.Count; i++ )
		{
			var p = DeletionList[i];

			// Hotload can clear the reference; an ancestor can finish deletion before this outro.
			if ( !p.IsValid() )
			{
				DeletionList.RemoveAt( i );
				i--;
				continue;
			}

			if ( !force && p.HasActiveTransitions ) continue;

			try
			{
				p.Delete( true );
			}
			catch ( System.Exception ex )
			{
				Log.Warning( ex, $"Exception while deferred-deleting {p}" );
			}

			DeletionList.RemoveAt( i );
			i--;
		}
	}

	internal void OnHotload()
	{
		for ( int i = 0; i < RootPanels.Count(); i++ )
		{
			if ( !RootPanels[i].IsValid ) continue;
			RootPanels[i].OnHotloaded();
		}
	}

	internal void OnLanguageChanged()
	{
		for ( int i = 0; i < RootPanels.Count(); i++ )
		{
			if ( !RootPanels[i].IsValid ) continue;
			RootPanels[i].LanguageChanged();
		}
	}

	internal void Clear()
	{
		// Clear any dangling tooltip panel references before destroying the tree.
		Tooltips.Clear();

		// Use immediate deletion so child panels are recursively cleaned up
		// right now. The default (deferred) path just queues an outro
		// animation and adds to DeletionList — but during shutdown there
		// is no next frame to process deferred deletions, so child panels
		// and their owned textures (gradients, text blocks, avatars) would
		// survive until GC, leaving native strong handles un-released.
		foreach ( var rp in RootPanels.ToArray() )
		{
			try
			{
				rp.Delete( immediate: true );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
			}
		}

		RootPanels.Clear();
		DeletionList.Clear();

		// Drop the entire input subsystem — replaces it wholesale so we
		// don't need to chase individual panel references inside PanelInput,
		// MouseButtonState, InputEventQueue, etc.
		Input = new();
		InputEventQueue = new();
		CurrentFocus = null;
		NextFocus = null;
	}
}
