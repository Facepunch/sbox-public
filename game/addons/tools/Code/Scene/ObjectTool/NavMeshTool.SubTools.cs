using Sandbox.Navigation;

namespace Editor;

/// <summary>
/// Navigation settings
/// </summary>
[Title( "Navigation Settings" )]
[Icon( "edit_note" )]
[Alias( "tools.navmesh-settings" )]
[Group( "1" )]
[Order( 0 )]
public class NavTestSettings : EditorTool
{
	private bool _previousNavDrawState = false;
	internal bool NavDrawStateChangedManually = false;

	public override void OnEnabled()
	{
		_previousNavDrawState = Scene.NavMesh.DrawMesh;
		Scene.NavMesh.DrawMesh = true;
		NavDrawStateChangedManually = false;
	}

	public override void OnDisabled()
	{
		if ( !NavDrawStateChangedManually )
		{
			// Restore previous state if we didn't change it manually
			Scene.NavMesh.DrawMesh = _previousNavDrawState;
		}
	}

	public override Widget CreateToolSidebar()
	{
		return new NavigationSettingsWidget( this );
	}

	/// <summary>
	/// Sidebar widget for navigation settings
	/// </summary>
	private class NavigationSettingsWidget : ToolSidebarWidget
	{
		private readonly NavTestSettings Tool;
		private readonly Checkbox EnabledCheckbox;
		private readonly Checkbox EditorAutoRefresh;
		private readonly Checkbox DebugCheckbox;

		public NavigationSettingsWidget( NavTestSettings tool ) : base()
		{
			Tool = tool;

			AddTitle( "Settings", "edit_note" );

			EnabledCheckbox = new Checkbox( "Enabled" )
			{
				StateChanged = state =>
				{
					Tool.Scene.NavMesh.IsEnabled = state == CheckState.On;
				}
			};
			Layout.Add( EnabledCheckbox );

			DebugCheckbox = new Checkbox( "Debug Render" )
			{
				StateChanged = state =>
				{
					Tool.Scene.NavMesh.DrawMesh = state == CheckState.On;
				},
				Clicked = () =>
				{
					Tool.NavDrawStateChangedManually = true;
				}
			};
			Layout.Add( DebugCheckbox );

			EditorAutoRefresh = new Checkbox( "Editor Auto Refresh" )
			{
				StateChanged = state =>
				{
					Tool.Scene.NavMesh.EditorAutoUpdate = state == CheckState.On;
				}
			};
			Layout.Add( EditorAutoRefresh );

			Layout.AddSpacingCell( 8 );

			Layout.Add( new Button( "Rebuild", "autorenew" )
			{
				Clicked = () => Tool.Scene.NavMesh.SetDirty()
			} );
			Layout.Add( new Button( "Bake", "download" )
			{
				Clicked = () => NavMesh.BakeNavMesh()
			} );
			Layout.Add( new Button( "Settings", "edit_note" )
			{
				Clicked = () => OpenPopup()
			} );

			Layout.AddStretchCell();
		}

		protected override void OnPaint()
		{
			base.OnPaint();

			// Update checkbox states
			EnabledCheckbox!.State = Tool.Scene.NavMesh.IsEnabled ? CheckState.On : CheckState.Off;
			DebugCheckbox!.State = Tool.Scene.NavMesh.DrawMesh ? CheckState.On : CheckState.Off;
			EditorAutoRefresh!.State = Tool.Scene.NavMesh.EditorAutoUpdate ? CheckState.On : CheckState.Off;
		}

		void OpenPopup()
		{
			var so = EditorTypeLibrary.GetSerializedObject( SceneEditorSession.Active.Scene.NavMesh );

			EditorUtility.OpenControlSheet( so, null );

			so.OnPropertyChanged = ( p ) => SceneEditorSession.Active.Scene.NavMesh.SetDirty();
		}
	}
}

/// <summary>
/// Navigation testing tool. You can select a start and target position and display the path between them.
/// </summary>
[Title( "Navigation Test" )]
[Icon( "cruelty_free" )]
[Alias( "tools.navmesh-tester" )]
[Group( "1" )]
[Order( 0 )]
public class NavTestTool : EditorTool
{
	internal Vector3 StartPoint;
	internal Vector3 TargetPoint;
	private bool CanTest => StartPoint != Vector3.Zero && TargetPoint != Vector3.Zero;

	internal NavMeshPathStatus CurrentStatus = NavMeshPathStatus.PathNotFound;

	private static readonly Dictionary<NavMeshPathStatus, Color> PathColorPalette = new()
	{
		[NavMeshPathStatus.Complete] = Color.Green,
		[NavMeshPathStatus.Partial] = Color.Yellow,
		[NavMeshPathStatus.PathNotFound] = Color.Red
	};

	internal enum PickingState
	{
		None,
		Start,
		Target
	}
	internal PickingState Picking = PickingState.None;

	private bool _previousNavDrawState = false;

	public override void OnEnabled()
	{
		_previousNavDrawState = Scene.NavMesh.DrawMesh;
		Scene.NavMesh.DrawMesh = true;
	}

	public override void OnDisabled()
	{
		Scene.NavMesh.DrawMesh = _previousNavDrawState;
	}

	public override Widget CreateToolSidebar()
	{
		return new NavTestWidget( this );
	}

	internal void Pick( PickingState state )
	{
		Picking = state;
		SceneOverlay.Parent.Focus();
	}

	/// <summary>
	/// Draw an arrow pointing down at a specific position
	/// </summary>
	/// <param name="position"></param>
	private void DrawPreviewArrow( Vector3 position )
	{
		if ( position == Vector3.Zero ) return;

		Color previousColor = Gizmo.Draw.Color;
		float previousThickness = Gizmo.Draw.LineThickness;

		Gizmo.Draw.Color = PathColorPalette[CurrentStatus];
		Gizmo.Draw.LineThickness = 3.0f;
		Gizmo.Draw.Arrow( position + new Vector3( 0, 0, 25 ), position );

		Gizmo.Draw.Color = previousColor;
		Gizmo.Draw.LineThickness = previousThickness;
	}

	/// <summary>
	/// Performs a scene trace to pick start/end position and draw a preview
	/// </summary>
	private void PickPositions()
	{
		if ( Picking == PickingState.None )
		{
			SceneOverlay.Parent.Cursor = CursorShape.Arrow;
			return;
		}

		var trace = Trace.UseRenderMeshes( true ).Run();
		if ( !trace.Hit )
		{
			return;
		}

		// Target cursor to indicate we can pick in the viewport
		SceneOverlay.Parent.Cursor = CursorShape.Cross;

		var result = trace.HitPosition;
		if ( Picking == PickingState.Start ) StartPoint = result;
		if ( Picking == PickingState.Target ) TargetPoint = result;

		// We made a selection, lets stop picking
		if ( Gizmo.WasLeftMouseReleased )
		{
			// If we are picking start, automatically switch to end picking
			if ( Picking == PickingState.Start ) Picking = PickingState.Target;
			else Picking = PickingState.None;
		}

		// Draw a preview of the current selection
		DrawPreviewArrow( result );
	}

	/// <summary>
	/// Draw a single path segment with a line and an arrow to show path direction
	/// </summary>
	/// <param name="a"></param>
	/// <param name="b"></param>
	private static void DrawPathSegment( Vector3 a, Vector3 b )
	{
		Gizmo.Draw.LineThickness = 2;
		Gizmo.Draw.Line( a, b );

		Gizmo.Draw.LineThickness = 4;
		Gizmo.Draw.Arrow( a, b );
	}

	public override void OnUpdate()
	{
		if ( !Scene.NavMesh.IsEnabled )
		{
			StartPoint = TargetPoint = Vector3.Zero;
			return;
		}

		PickPositions();

		// Always draw start and target position
		DrawPreviewArrow( StartPoint );
		DrawPreviewArrow( TargetPoint );

		if ( !CanTest ) return; // Start & End not defined

		NavMeshPath result = Scene.NavMesh.CalculatePath( new()
		{
			Start = StartPoint,
			Target = TargetPoint
		} );

		if ( !result.IsValid || result.Points.Count == 0 ) return;

		// Save original line thickness for later
		float prevThickness = Gizmo.Draw.LineThickness;

		// Lets draw a tiny bit above grove to avoid clipping
		Vector3 zOffset = new( 0, 0, 0.25f );

		CurrentStatus = result.Status;
		Color currentColor = PathColorPalette[CurrentStatus];

		var path = result.Points;
		for ( int i = 0; i < path.Count - 1; i++ )
		{
			var a = path[i].Position + zOffset;
			var b = path[i + 1].Position + zOffset;

			// Non-occluded
			Gizmo.Draw.IgnoreDepth = false;
			{
				// Simple pulsing effect [0.1, 1] alpha
				float alpha = (float)Math.Cos( Time.Now * 4 ).Remap( -1, 1, 0.1, 1, true );
				currentColor.a = alpha;
				Gizmo.Draw.Color = currentColor;
				DrawPathSegment( a, b );
			}

			// Occluded with light opacity
			Gizmo.Draw.IgnoreDepth = true;
			{
				currentColor.a = 0.1f;
				Gizmo.Draw.Color = currentColor;
				DrawPathSegment( a, b );
			}
		}

		// Reset original state
		Gizmo.Draw.IgnoreDepth = false;
		Gizmo.Draw.LineThickness = prevThickness;
	}

	/// <summary>
	/// Navigation testing tool sidebar widget
	/// </summary>
	private class NavTestWidget : ToolSidebarWidget
	{
		private readonly NavTestTool Tool;
		private readonly Button SelectStart;
		private readonly Button SelectEnd;
		private readonly Label StatusLabel;

		public NavTestWidget( NavTestTool tool ) : base()
		{
			Tool = tool;

			AddTitle( "Path Tester", "cruelty_free" );

			StatusLabel = new Label( "Select a start and target position..." );
			StatusLabel.Alignment = TextFlag.CenterHorizontally;
			StatusLabel.ToolTip = "Status";
			Layout.Add( StatusLabel );

			Layout.AddSpacingCell( 8 );

			SelectStart = new Button( "Pick Start", "ads_click" )
			{
				Clicked = () => Tool.Pick( PickingState.Start )
			};
			Layout.Add( SelectStart );

			SelectEnd = new Button( "Pick Target", "ads_click" )
			{
				Clicked = () => Tool.Pick( PickingState.Target )
			};
			Layout.Add( SelectEnd );

			Layout.AddStretchCell();
		}

		protected override void OnPaint()
		{
			base.OnPaint();

			bool navmeshEnabled = Tool.Scene.NavMesh.IsEnabled;
			bool hasStart = Tool.StartPoint != Vector3.Zero;
			bool hasTarget = Tool.TargetPoint != Vector3.Zero;

			string statusText = navmeshEnabled switch
			{
				false => "NavMesh is disabled",
				true when !hasStart && !hasTarget => "Missing start and target",
				true when !hasStart => "Missing start",
				true when !hasTarget => "Missing target",
				true => Tool.CurrentStatus switch
				{
					NavMeshPathStatus.Complete => "Path complete",
					NavMeshPathStatus.Partial => "Path partial",
					_ => "Path not found",
				}
			};

			StatusLabel.Text = statusText;
			SelectStart.Enabled = navmeshEnabled;
			SelectEnd.Enabled = navmeshEnabled;
		}
	}
}
