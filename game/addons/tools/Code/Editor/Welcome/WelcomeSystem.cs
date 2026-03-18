namespace Editor;

/// <summary>
/// Manages the interactive welcome tutorial system for first-time users
/// </summary>
internal static class WelcomeSystem
{
	static WelcomeOverlay overlay;
	static WelcomePopup popup;
	static List<WelcomeStep> steps = new();
	static int currentStepIndex = 0;
	static bool neverShowAgain = false;
	static WelcomeFrameUpdater frameUpdater;

	static Widget mainWindow;

	static WelcomeSystem()
	{
		InitializeDefaultSteps();
	}

	static void InitializeDefaultSteps()
	{
		steps.Clear();

		// Step 1: Welcome message
		steps.Add( new WelcomeStep(
			"Press <strong>Next</strong> to follow a brief interactive guide to get more familiar with the editor.<br /><br />It will run through what each part of the interface does, and is highly recommended for beginners!"
		) );

		// Step 2: Viewport
		steps.Add( new WelcomeStep(
			"This is the <strong>Main Viewport</strong>, your view into the current scene.<br /><br />Here you can manipulate the <strong>GameObjects</strong> in your scene to build whatever you want!<br /><br />To look around, hold RMB and use WASD while holding RMB to move around.",
			() => SceneViewWidget.Current
		) );

		// Step 3: Hierarchy
		steps.Add( new WelcomeStep(
			"This is the <strong>Hierarchy</strong>, where all the GameObjects in your scene live.<br /><br />Here you can search through and organize all the objects in the scene.<br /><br />You can also <strong>create new GameObjects</strong> via the + button.",
			() => SceneTreeWidget.Current
		) );

		// Step 4: Inspector
		steps.Add( new WelcomeStep(
			"This is the <strong>Inspector</strong>, which will show you the properties of the selected GameObject.<br /><br />You can also <strong>add and remove components</strong> from here, and edit their properties as well. Components are what tell a GameObject what to do (like render a model with the Model Renderer, or play sounds with a Sound Point).<br /><br />You can make custom Components with C# to control GameObjects in whatever ways you want!",
			() => FindInspector(),
			onEnter: () => InspectMainCamera()
		) );

		// Step 5: Asset Browser
		steps.Add( new WelcomeStep(
			"This is the <strong>Asset Browser</strong>, where you can browse all the assets in your project.<br /><br />This is essentially the file explorer for this specific project, allowing you to manage your assets as well as the assets from any libraries you may have added.<br /><br />You can <strong>create new assets</strong> in the current folder using the \"New\" button!",
			() => MainAssetBrowser.Instance,
			onEnter: () => RaiseAssetBrowser()
		) );

		// Step 6: Cloud Browser
		steps.Add( new WelcomeStep(
			"This is the <strong>Cloud Browser</strong>, where you can browse assets that have been uploaded to the workshop.<br /><br />Here you can find models, materials, sounds, and more that you can instantly drag-and-drop directly into the main viewport to use right away!<br /><br />You can visit <a href=\"https://sbox.game\">sbox.game</a> in your browser and favourite various collections to get quick access to them here. (eg. <a href=\"https://sbox.game/facepunch/sboxassets\">s&box assets</a> and <a href=\"https://sbox.game/facepunch/sboxweapons\">s&box weapons</a>)",
			() => MainAssetBrowser.Instance,
			onEnter: () => RaiseCloudBrowser()
		) );

		// Step 7: Play Button
		steps.Add( new WelcomeStep(
			"When you're ready to test things out in-game, click the <strong>Play button</strong> (or <strong>F5</strong>) to instantly enter Play Mode!<br /><br />While playing, you can press <strong>ESC</strong> to free the mouse, and use the <strong>Pause and Stop buttons</strong> to control execution, or simply press <strong>F5</strong> to instantly exit Play Mode.<br /><br />Press the <strong>Eject button</strong> while the game is playing to view the game's current scene in the editor view.",
			() => FindPlayButton()
		) );

		// Step 8: Project Settings
		steps.Add( new WelcomeStep(
			"Then <strong>once your game is complete and ready for the world</strong>, you can come here to configure your project settings and prepare for publishing.<br /><br /><strong>You can choose to publish your game</strong>, so other people with s&box can play the game and find it in their Discover tab.<br /><br /><strong>Or you can choose to export the game</strong>, and publish it yourself on external sites/platforms.",
			() => FindTitleBarButtons()
		) );

		// Step 9: Welcome complete
		steps.Add( new WelcomeStep(
			"That's it! You're ready to start creating in s&box.<br /><br />Remember, you can always access this tutorial again from Help → Show Welcome Tutorial if you ever need a reminder."
		) );
	}

	/// <summary>
	/// Automatically show the welcome tutorial when the editor first opens, for first-time users
	/// </summary>
	[Event( "editor.created" )]
	static void OnEditorCreated( EditorMainWindow editorWindow )
	{
		if ( EditorCookie.Get( "editor.welcome.neverShowAgain", false ) )
			return;

		// Small delay to allow the editor layout to fully restore before showing the tutorial
		_ = Task.Delay( 500 ).ContinueWith( _ => Start(), TaskScheduler.FromCurrentSynchronizationContext() );
	}

	/// <summary>
	/// Start the welcome tutorial
	/// </summary>
	[Menu( "Editor", "Help/Show Welcome Tutorial", "waving_hand" )]
	public static void Start()
	{
		// If tutorial is already running, don't start a new one
		if ( overlay != null && overlay.IsValid )
			return;

		mainWindow = SceneViewWidget.Current?.GetWindow() ?? SceneTreeWidget.Current?.GetWindow();
		if ( mainWindow == null || !mainWindow.IsValid )
			return;

		// Make sure the main editor window is active before showing the tutorial popup
		mainWindow.Show();
		mainWindow.Raise();

		currentStepIndex = 0;
		neverShowAgain = true;

		// Create the overlay
		overlay = new WelcomeOverlay( mainWindow );
		overlay.Size = mainWindow.Size;
		overlay.Position = Vector2.Zero;
		overlay.Lower();
		overlay.Show();

		// Create the popup
		popup = new WelcomePopup( mainWindow );
		popup.OnNext = OnNextClicked;
		popup.OnPrevious = OnPreviousClicked;
		popup.OnSkip = OnSkipClicked;
		popup.OnNeverAskChanged = () => neverShowAgain = popup.NeverAskCheckbox.Value;
		popup.Raise();

		ShowCurrentStep();

		popup.Show();

		// Listen for window resize
		frameUpdater = new WelcomeFrameUpdater();
		EditorEvent.Register( frameUpdater );
	}

	class WelcomeFrameUpdater
	{
		Vector2 lastWindowSize;
		Widget lastHighlightedWidget;
		RealTimeSince timeSinceLastRefresh;

		public WelcomeFrameUpdater()
		{
			if ( mainWindow != null && mainWindow.IsValid )
			{
				lastWindowSize = mainWindow.Size;
			}
			timeSinceLastRefresh = 0;
		}

		[EditorEvent.Frame]
		public void OnFrame()
		{
			if ( overlay == null || !overlay.IsValid )
			{
				Stop();
				EditorEvent.Unregister( this );
				return;
			}

			// Ensure overlay and popup remain visible (since it can be hidden when entering play mode)
			if ( overlay.IsValid && !overlay.Visible )
			{
				overlay.Show();
				overlay.Lower();
			}

			if ( popup != null && popup.IsValid && !popup.Visible )
			{
				popup.Show();
				popup.Raise();
			}

			// Check if highlighted widget became invalid (happens when switching play/editor modes)
			if ( currentStepIndex >= 0 && currentStepIndex < steps.Count )
			{
				var step = steps[currentStepIndex];
				bool needsRefresh = false;

				if ( lastHighlightedWidget != null && !lastHighlightedWidget.IsValid )
				{
					// Previous widget became invalid
					lastHighlightedWidget = null;
					needsRefresh = true;
				}

				if ( step.GetHighlightWidget != null )
				{
					var currentHighlight = step.GetHighlightWidget.Invoke();

					if ( currentHighlight != null && currentHighlight.IsValid && currentHighlight != lastHighlightedWidget )
					{
						// We got a new valid widget that's different from the last one
						lastHighlightedWidget = currentHighlight;
						needsRefresh = true;
					}
					else if ( lastHighlightedWidget == null && timeSinceLastRefresh > 0.1f )
					{
						// Refresh until we find a widget (this really only happens when the step is opening the widget it's highlighting so it needs a sec to be initialized)
						needsRefresh = true;
					}
				}

				if ( needsRefresh )
				{
					timeSinceLastRefresh = 0;
					RefreshHighlight();
				}
			}

			if ( mainWindow != null && mainWindow.IsValid )
			{
				var currentSize = mainWindow.Size;
				if ( currentSize != lastWindowSize )
				{
					lastWindowSize = currentSize;
					overlay.UpdateSize();

					if ( popup != null && popup.IsValid )
					{
						PositionPopup();
					}
				}
			}
		}
	}

	static void PositionPopup()
	{
		if ( popup == null || !popup.IsValid || mainWindow == null || !mainWindow.IsValid )
			return;

		if ( currentStepIndex < 0 || currentStepIndex >= steps.Count )
			return;

		var step = steps[currentStepIndex];
		var highlightWidget = step.GetHighlightWidget?.Invoke();

		// Default to center if no highlight
		if ( highlightWidget == null || !highlightWidget.IsValid )
		{
			var centerX = (mainWindow.Width - popup.Width) / 2.0f;
			var centerY = mainWindow.Height * 0.40f - popup.Height / 2.0f;

			popup.Position = new Vector2( centerX, centerY );
			return;
		}

		var widgetScreenPos = highlightWidget.ScreenPosition;
		var widgetSize = highlightWidget.Size;
		float margin = 20;
		float spaceLeft = widgetScreenPos.x;
		float spaceRight = mainWindow.Width - (widgetScreenPos.x + widgetSize.x);
		float spaceTop = widgetScreenPos.y;
		float spaceBottom = mainWindow.Height - (widgetScreenPos.y + widgetSize.y);

		// Determine which side has the most space, and we'll put the popup there if it fits
		float maxSpace = Math.Max( Math.Max( spaceLeft, spaceRight ), Math.Max( spaceTop, spaceBottom ) );
		Vector2 popupPos = Vector2.Zero;

		if ( maxSpace == spaceRight && spaceRight > popup.Width + margin )
		{
			// Right side
			popupPos.x = widgetScreenPos.x + widgetSize.x + margin;
			popupPos.y = widgetScreenPos.y + (widgetSize.y - popup.Height) * 0.5f;
		}
		else if ( maxSpace == spaceLeft && spaceLeft > popup.Width + margin )
		{
			// Left side
			popupPos.x = widgetScreenPos.x - popup.Width - margin;
			popupPos.y = widgetScreenPos.y + (widgetSize.y - popup.Height) * 0.5f;
		}
		else if ( maxSpace == spaceBottom && spaceBottom > popup.Height + margin )
		{
			// Below
			popupPos.x = widgetScreenPos.x + (widgetSize.x - popup.Width) * 0.5f;
			popupPos.y = widgetScreenPos.y + widgetSize.y + margin;
		}
		else if ( maxSpace == spaceTop && spaceTop > popup.Height + margin )
		{
			// Above
			popupPos.x = widgetScreenPos.x + (widgetSize.x - popup.Width) * 0.5f;
			popupPos.y = widgetScreenPos.y - popup.Height - margin;
		}
		else
		{
			var centerX = (mainWindow.Width - popup.Width) / 2.0f;
			var centerY = mainWindow.Height * 0.40f - popup.Height / 2.0f;

			popupPos = new Vector2( centerX, centerY );
		}

		// Make sure popup stays within window bounds with some padding
		popupPos.x = Math.Max( 10, Math.Min( popupPos.x, mainWindow.Width - popup.Width - 10 ) );
		popupPos.y = Math.Max( 10, Math.Min( popupPos.y, mainWindow.Height - popup.Height - 10 ) );

		popup.Position = popupPos;
	}

	static void RefreshHighlight()
	{
		if ( currentStepIndex < 0 || currentStepIndex >= steps.Count )
			return;

		var step = steps[currentStepIndex];
		var highlightWidget = step.GetHighlightWidget?.Invoke();

		overlay.ClearHighlight();
		if ( highlightWidget != null && highlightWidget.IsValid )
		{
			overlay.SetHighlight( highlightWidget );

			if ( !overlay.Visible )
			{
				overlay.Show();
				overlay.Lower();
			}
		}

		PositionPopup();
	}

	static void ShowCurrentStep()
	{
		if ( currentStepIndex < 0 || currentStepIndex >= steps.Count )
			return;

		var step = steps[currentStepIndex];
		step.OnEnter?.Invoke();

		popup.SetMessage( step.Message );
		popup.SetStepInfo( currentStepIndex, steps.Count );
		overlay.ClearHighlight();

		var highlightWidget = step.GetHighlightWidget?.Invoke();
		if ( highlightWidget != null && highlightWidget.IsValid )
		{
			overlay.SetHighlight( highlightWidget );
		}

		PositionPopup();
	}

	static void OnNextClicked()
	{
		currentStepIndex++;

		if ( currentStepIndex >= steps.Count )
		{
			Stop();
			return;
		}

		ShowCurrentStep();
	}

	static void OnPreviousClicked()
	{
		if ( currentStepIndex > 0 )
		{
			currentStepIndex--;
			ShowCurrentStep();
		}
	}

	static void OnSkipClicked()
	{
		Stop();
	}

	/// <summary>
	/// Stop and cleanup the welcome tutorial
	/// </summary>
	static void Stop()
	{
		if ( neverShowAgain )
		{
			EditorCookie.Set( "editor.welcome.neverShowAgain", true );
		}

		if ( frameUpdater != null )
		{
			EditorEvent.Unregister( frameUpdater );
			frameUpdater = null;
		}

		overlay?.Destroy();
		popup?.Destroy();
		overlay = null;
		popup = null;
	}



	// Helper methods to find specific widgets //

	static Widget FindInspector()
	{
		var window = mainWindow.GetWindow();
		if ( window is DockWindow dockWindow )
		{
			return dockWindow.DockManager.GetDockWidget( "Inspector" );
		}

		return null;
	}

	static void InspectMainCamera()
	{
		// Find the Main Camera in the current scene and inspect it so there are values to play with in the Inspector step
		var sceneView = SceneViewWidget.Current;
		if ( sceneView == null || !sceneView.IsValid )
			return;

		var scene = sceneView.Session?.Scene;
		if ( scene == null || !scene.IsValid )
			return;

		var mainCameraObj = scene.Children.FirstOrDefault( x => x.Name == "Main Camera" );
		if ( mainCameraObj != null && mainCameraObj.IsValid )
		{
			EditorUtility.InspectorObject = mainCameraObj;
		}
	}

	static void RaiseAssetBrowser()
	{
		// Raise the Asset Browser and switch to the Local tab
		var assetBrowser = MainAssetBrowser.Instance;
		if ( assetBrowser == null || !assetBrowser.IsValid )
			return;

		var window = mainWindow.GetWindow();
		if ( window is DockWindow dockWindow )
		{
			dockWindow.DockManager.RaiseDock( assetBrowser );
		}

		// Switch to the Local tab
		var tabs = assetBrowser.GetDescendants<Widget>().FirstOrDefault( w => w.GetType().Name == "VerticalTabWidget" );
		if ( tabs is VerticalTabWidget verticalTabWidget && assetBrowser.Local != null )
		{
			verticalTabWidget.SetPage( assetBrowser.Local );
		}
	}

	static void RaiseCloudBrowser()
	{
		// Raise the Asset Browser and switch to the Cloud tab
		var assetBrowser = MainAssetBrowser.Instance;
		if ( assetBrowser == null || !assetBrowser.IsValid )
			return;

		var window = mainWindow.GetWindow();
		if ( window is DockWindow dockWindow )
		{
			dockWindow.DockManager.RaiseDock( assetBrowser );
		}

		// Switch to the Cloud tab
		var tabs = assetBrowser.GetDescendants<Widget>().FirstOrDefault( w => w.GetType().Name == "VerticalTabWidget" );
		if ( tabs is VerticalTabWidget verticalTabWidget && assetBrowser.Cloud != null )
		{
			verticalTabWidget.SetPage( assetBrowser.Cloud );
		}
	}

	static Widget FindPlayButton()
	{
		var sceneView = SceneViewWidget.Current;
		if ( sceneView == null || !sceneView.IsValid )
			return null;

		var viewportTools = sceneView.GetDescendants<Widget>()
			.FirstOrDefault( w => w.GetType().Name == "ViewportTools" );

		if ( viewportTools == null || !viewportTools.IsValid )
			return null;

		// Find the play button by searching for an IconButton with "play_arrow" icon
		var playButton = viewportTools.GetDescendants<IconButton>()
			.FirstOrDefault( b => b.Icon == "play_arrow" );

		if ( playButton != null && playButton.IsValid && playButton.Parent != null )
		{
			// Return the parent container that holds play/pause/eject buttons
			return playButton.Parent;
		}

		// Fallback to the entire toolbar if we can't find the play button
		return viewportTools.GetDescendants<Widget>().FirstOrDefault( w => w.Name == "ViewportToolbar" );
	}

	static Widget FindTitleBarButtons()
	{
		var window = mainWindow.GetWindow();
		if ( window == null || !window.IsValid )
			return null;

		return window.GetDescendants<Widget>().FirstOrDefault( w => w.GetType().Name == "TitleBarButtons" );
	}
}
