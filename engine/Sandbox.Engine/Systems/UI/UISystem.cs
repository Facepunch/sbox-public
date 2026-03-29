using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Modals;
using Sandbox.Rendering;
using Sandbox.UI;
using Sandbox.VR;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Holds onto a list of root panels to tick, input and draw
/// </summary>
internal class UISystem
{
	internal ThreadLocal<PanelRenderer> Renderer = new( () => new PanelRenderer() );

	internal PanelInput Input { get; set; } = new();

	internal readonly CommandList GlobalCommandList = new();

	internal List<RootPanel> RootPanels = new();
	internal List<Panel> DeletionList = new();
	internal InputEventQueue InputEventQueue = new();

	// focus
	internal Panel CurrentFocus { get; set; }
	internal Panel NextFocus { get; set; }
	internal bool FocusPendingChange { get; set; }

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

		for ( int i = RootPanels.Count - 1; i >= 0; i-- )
		{
			var root = RootPanels[i];
			if ( !root.IsValid ) continue;
			if ( root.RenderedManually || root.IsWorldPanel ) continue;

			GlobalCommandList.InsertList( root.PanelCommandList );
		}
	}

	internal void Simulate( bool allowMouseInput )
	{
		using ( Performance.Scope( "Update Screen Size" ) )
		{
			Screen.UpdateFromEngine();
		}

		using ( Performance.Scope( "Tick Panels" ) )
		{
			TickPanels();
		}

		using ( Performance.Scope( "Tick Input" ) )
		{
			TickInput( allowMouseInput );
		}

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

		using ( Performance.Scope( "Gather Command Lists" ) )
		{
			GatherCommandLists();
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

		for ( int i = 0; i < RootPanels.Count(); i++ )
		{
			if ( !RootPanels[i].IsValid ) continue;
			RootPanels[i].TickInternal();
		}
	}

	internal void PreLayout()
	{
		var width = Screen.Width;
		var height = Screen.Height;

		var screenRect = new Rect( 0, 0, width, height );

		for ( int i = 0; i < RootPanels.Count(); i++ )
		{
			if ( !RootPanels[i].IsValid ) continue;
			RootPanels[i].PreLayout( screenRect );
		}
	}

	internal void Layout()
	{
		for ( int i = 0; i < RootPanels.Count(); i++ )
		{
			if ( !RootPanels[i].IsValid ) continue;
			RootPanels[i].CalculateLayout();
		}
	}

	internal void PostLayout()
	{
		for ( int i = 0; i < RootPanels.Count(); i++ )
		{
			if ( !RootPanels[i].IsValid ) continue;
			RootPanels[i].PostLayout();
		}
	}

	internal void BuildCommandLists()
	{
		for ( int i = 0; i < RootPanels.Count; i++ )
		{
			var root = RootPanels[i];
			if ( !root.IsValid ) continue;

			root.BuildCommandLists();
		}
	}

	internal void GatherCommandLists()
	{
		for ( int i = 0; i < RootPanels.Count; i++ )
		{
			var root = RootPanels[i];
			if ( !root.IsValid ) continue;
			if ( root.RenderedManually ) continue;

			root.GatherCommandLists();
		}
	}

	internal void TickInput( bool allowMouseInput )
	{
		for ( int i = 0; i < RootPanels.Count(); i++ )
		{
			if ( !RootPanels[i].IsValid ) continue;
			RootPanels[i].TickInputInternal();
		}

		//
		// Tick various input systems
		//
		Input.Tick( RootPanels.Where( p => !p.IsWorldPanel ).OrderByDescending( x => x.ComputedStyle?.ZIndex ?? 0 ), allowMouseInput && DoAnyPanelsWantMouseVisible() );

		TickWorldInput();

		//
		// We tick focus here, after the layout. This way any styles that
		// were set at the same time as changing focus will be applied so
		// that when we judge elibility the logic will be correct
		//
		InputFocus.Tick();

		//
		// Send all key events to the focused panel
		//
		InputEventQueue.TickFocused( CurrentFocus );

		//
		// Pass our global input events ( mouse move, double click ) to 2d panels
		// WorldInputs simulate this themselves in WorldInputInternal.Tick
		//
		InputEventQueue.Tick( Input.Hovered, Input.Active );

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
			// The developer console is open
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
			worldInput.WorldPanelInput.Tick( rootPanels.Select( x => x.GetPanel() as RootPanel ), true );
		}
	}

	bool DoAnyPanelsWantMouseVisible()
	{
		if ( Mouse.Visibility == MouseVisibility.Visible ) return true;
		if ( Mouse.Visibility == MouseVisibility.Hidden && !Game.IsMenu ) return false;

		for ( int i = 0; i < RootPanels.Count; i++ )
		{
			if ( !RootPanels[i].IsValid )
				continue;

			if ( !RootPanels[i].IsVisible )
				continue;

			if ( RootPanels[i].IsWorldPanel )
				continue;

			if ( !RootPanels[i].ChildrenWantMouseInput )
				continue;

			if ( Game.IsMenu && RootPanels[i].RenderedManually && !Game.IsMainMenuVisible ) continue;

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
			if ( !force && p.HasActiveTransitions ) continue;

			p.Delete( true );
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
		TooltipSystem.Clear();

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
		Renderer = new( () => new PanelRenderer() );
		CurrentFocus = null;
		NextFocus = null;
	}
}
