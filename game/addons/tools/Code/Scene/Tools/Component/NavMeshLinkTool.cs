public class NavMeshLinkTool : EditorTool<NavMeshLink>
{

	public override void OnEnabled()
	{
		window = new NavMeshLinkToolWindow();
		AddOverlay( window, TextFlag.RightBottom, 10 );
	}

	public override void OnUpdate()
	{
		window.ToolUpdate();
	}

	public override void OnDisabled()
	{
		window.OnDisabled();
	}

	public override void OnSelectionChanged()
	{
		var target = GetSelectedComponent<NavMeshLink>();
		window.OnSelectionChanged( target );
	}


	private NavMeshLinkToolWindow window = null;
}

class NavMeshLinkToolWindow : WidgetWindow
{
	NavMeshLink targetComponent;

	static bool IsClosed = false;

	SceneTraceResult currentSceneTrace;

	string cursorText => isPickingStart ? "Start" : (isPickingEnd ? "End" : "");

	bool isPickingStart = false;

	bool isPickingEnd = false;
	bool isPickingStartOrEnd => isPickingStart || isPickingEnd;
	bool isAddingNewLink = false;

	Vector3 newLinkstart = Vector3.Zero;
	Vector3 newLinkEnd = Vector3.Zero;

	Label helpLabel;

	Checkbox centerGameObjectCheckbox;

	Button cancelButton;

	public NavMeshLinkToolWindow()
	{
		ContentMargins = 0;
		Layout = Layout.Column();
		MaximumWidth = 600;
		MinimumWidth = 400;
		Rebuild();
	}

	void Rebuild()
	{
		Layout.Clear( true );
		Layout.Margin = 0;
		Icon = IsClosed ? "" : "link";
		WindowTitle = IsClosed ? "" : $"NavMesh Link Editor";

		IsGrabbable = !IsClosed;

		if ( IsClosed )
		{
			var closedRow = Layout.AddRow();
			closedRow.Add( new IconButton( "link", () => { IsClosed = false; Rebuild(); } ) { ToolTip = "Open NavMeshLink Editor", FixedHeight = HeaderHeight, FixedWidth = HeaderHeight, Background = Color.Transparent } );
			MinimumWidth = 0;
			return;
		}

		MinimumWidth = 400;

		var headerRow = Layout.AddRow();
		headerRow.AddStretchCell();
		headerRow.Add( new IconButton( "info" )
		{
			ToolTip = "Controls to edit the NavMesh link points.\nUse to quickly snap start and end to the nevmesh.\nCan also be used to quickly add multiple new links.",
			FixedHeight = HeaderHeight,
			FixedWidth = HeaderHeight,
			Background = Color.Transparent
		} );
		headerRow.Add( new IconButton( "close", CloseWindow ) { ToolTip = "Close Editor", FixedHeight = HeaderHeight, FixedWidth = HeaderHeight, Background = Color.Transparent } );

		if ( targetComponent.IsValid() )
		{
			var controlSheet = new ControlSheet();

			var row = Layout.Row();
			row.Spacing = 16;
			row.Margin = row.Margin with { Bottom = 8 };
			row.Add( new Button( "Set Start", "ads_click" )
			{
				ToolTip = "Set Link Start Position",
				Clicked = () =>
				{
					isPickingStart = true;
					isPickingEnd = false;
					isAddingNewLink = false;
					helpLabel.Text = "Pick start position for selected link. Press Ctrl or Cancel Button to cancel.";
					SceneViewWidget.Current?.LastSelectedViewportWidget?.Focus();
				}
			} );

			row.Add( new Button( "Set End", "ads_click" )
			{
				ToolTip = "Set Link End Position",
				Clicked = () =>
				{
					isPickingEnd = true;
					isPickingStart = false;
					isAddingNewLink = false;
					helpLabel.Text = "Pick end position for selected link. Press Ctrl or Cancel Button to cancel.";
					SceneViewWidget.Current?.LastSelectedViewportWidget?.Focus();
				}
			} );

			row.Add( new Button( "(Bulk) Add New Links", "add_link" )
			{
				ToolTip = "Create one or more new Links",
				Clicked = () =>
				{
					isAddingNewLink = true;
					isPickingStart = true;
					helpLabel.Text = "Pick start Position for new link. Press Ctrl or Cancel Button to cancel.";
					SceneViewWidget.Current?.LastSelectedViewportWidget?.Focus();
				}
			} );

			cancelButton = row.Add( new Button( "Cancel" )
			{
				ToolTip = "Cancel current operation",
				Clicked = () => Cancel(),
				Enabled = false
			} );

			var settingsRow = Layout.Row();
			centerGameObjectCheckbox = settingsRow.Add( new Checkbox( "Center GameObject" )
			{ ToolTip = "Center GameObject int the middle of the start and end points. This will always be the case for new links.", Value = true } );

			var textRow = Layout.Row();
			helpLabel = textRow.Add( new Label( "Press a button to start a new operation." ) );

			controlSheet.AddLayout( row );
			controlSheet.AddLayout( settingsRow );
			controlSheet.AddLayout( textRow );

			Layout.Add( controlSheet );
		}

		Layout.Margin = 4;
	}

	private void Cancel()
	{
		isPickingStart = false;
		isPickingEnd = false;
		isAddingNewLink = false;
		helpLabel.Text = "Press a button to start a new operation.";
		cancelButton.Enabled = false;
	}


	public void ToolUpdate()
	{
		if ( !targetComponent.IsValid() )
			return;

		currentSceneTrace = targetComponent.Scene.Trace.Ray( Gizmo.CurrentRay, Gizmo.RayDepth ).Run();

		DrawGizmos();
	}

	void DrawGizmos()
	{
		var cursorRadius = 16f;

		using ( Gizmo.ObjectScope( targetComponent, Transform.Zero ) )
		{
			if ( !isAddingNewLink )
			{
				// Draw text above start
				Gizmo.Draw.Color = Color.White;
				var startTextTransform = Gizmo.CameraTransform.WithPosition( targetComponent.WorldStartPosition + Vector3.Up * cursorRadius * 2 );
				Gizmo.Draw.Text( "Start", startTextTransform, "Roboto", 16f );

				// Draw text above end
				var endTextTransform = Gizmo.CameraTransform.WithPosition( targetComponent.WorldEndPosition + Vector3.Up * cursorRadius * 2 );
				Gizmo.Draw.Text( "End", endTextTransform, "Roboto", 16f );
			}
		}

		if ( !isPickingStartOrEnd )
			return;

		cancelButton.Enabled = true;

		var navMeshSearchRadius = targetComponent.ConnectionRadius;
		var navmeshHitLocation = targetComponent.Scene.NavMesh.GetClosestPoint( currentSceneTrace.HitPosition, navMeshSearchRadius );

		if ( navmeshHitLocation.HasValue )
		{
			using ( Gizmo.ObjectScope( targetComponent, Transform.Zero ) )
			{
				Gizmo.Settings.Selection = false;

				var textTransform = Gizmo.CameraTransform.WithPosition( navmeshHitLocation.Value + Vector3.Up * cursorRadius * 2 );

				Gizmo.Draw.Color = Color.Yellow;
				Gizmo.Draw.Text( cursorText, textTransform, "Roboto", 16f );
				Gizmo.Draw.SolidSphere( navmeshHitLocation.Value, cursorRadius, 16, 16 );

				if ( isPickingStartOrEnd )
				{
					if ( isAddingNewLink && isPickingEnd )
					{
						Gizmo.Draw.Color = Color.Blue.WithAlpha( 0.5f );
						Gizmo.Draw.Line( navmeshHitLocation.Value, newLinkstart );
						Gizmo.Draw.Color = Color.Yellow.WithAlpha( 0.5f );
						Gizmo.Draw.LineSphere( newLinkstart, 16 );
					}
					else if ( !isAddingNewLink )
					{
						Gizmo.Draw.Color = Color.Blue.WithAlpha( 0.5f );
						Gizmo.Draw.Line( navmeshHitLocation.Value, targetComponent.WorldPosition + (isPickingStart ? targetComponent.LocalEndPosition : targetComponent.LocalStartPosition) );
					}
				}

				using ( Gizmo.Scope() )
				{
					Gizmo.Draw.IgnoreDepth = true;
					Gizmo.Draw.Color = Gizmo.Draw.Color.WithAlpha( 0.5f );
					Gizmo.Draw.SolidSphere( navmeshHitLocation.Value, cursorRadius, 16, 16 );
				}

				Gizmo.Hitbox.Sphere( new Sphere { Center = navmeshHitLocation.Value, Radius = navMeshSearchRadius + cursorRadius } );

				if ( Gizmo.IsCtrlPressed )
				{
					Cancel();
				}

				if ( Gizmo.WasClicked )
				{
					if ( isPickingStart )
					{
						if ( isAddingNewLink )
						{
							newLinkstart = navmeshHitLocation.Value;
							isPickingStart = false;
							isPickingEnd = true;
							helpLabel.Text = "Pick end Position for new link. Press Ctrl or Cancel Button to cancel.";
						}
						else
						{
							targetComponent.LocalStartPosition = navmeshHitLocation.Value - targetComponent.WorldPosition;
							isPickingStart = false;
							helpLabel.Text = "";

							if ( centerGameObjectCheckbox.Value )
							{
								var startWorldPos = targetComponent.WorldPosition + targetComponent.LocalStartPosition;
								var endWorldPos = targetComponent.WorldPosition + targetComponent.LocalEndPosition;
								var newWorldPos = (startWorldPos - endWorldPos) * 0.5f + endWorldPos;
								targetComponent.WorldPosition = newWorldPos;
								targetComponent.LocalStartPosition = startWorldPos - newWorldPos;
								targetComponent.LocalEndPosition = endWorldPos - newWorldPos;
							}
						}
					}
					else if ( isPickingEnd )
					{
						if ( isAddingNewLink )
						{
							newLinkEnd = navmeshHitLocation.Value;
							isPickingEnd = false;
							var newLinkObject = new GameObject( targetComponent.GameObject.Parent, true, targetComponent.GameObject.Name );
							newLinkObject.MakeNameUnique();
							var td = TypeLibrary.GetType( targetComponent.GetType() );
							var newLink = newLinkObject.Components.Create( td ) as NavMeshLink;
							newLink.WorldPosition = (newLinkstart - newLinkEnd) * 0.5f + newLinkEnd;
							newLink.LocalStartPosition = newLinkstart - newLinkObject.WorldPosition;
							newLink.LocalEndPosition = newLinkEnd - newLinkObject.WorldPosition;
							newLink.IsBiDirectional = targetComponent.IsBiDirectional;
							newLink.ConnectionRadius = targetComponent.ConnectionRadius;

							SceneEditorSession.Active.Selection.Clear();
							SceneEditorSession.Active.Selection.Add( newLink.GameObject );

							// start again
							isPickingStart = true;
							helpLabel.Text = "Pick start position for new link. Press Ctrl or Cancel Button to cancel.";
						}
						else
						{
							targetComponent.LocalEndPosition = navmeshHitLocation.Value - targetComponent.WorldPosition;
							isPickingEnd = false;
							helpLabel.Text = "";

							if ( centerGameObjectCheckbox.Value )
							{
								var startWorldPos = targetComponent.WorldPosition + targetComponent.LocalStartPosition;
								var endWorldPos = targetComponent.WorldPosition + targetComponent.LocalEndPosition;
								var newWorldPos = (startWorldPos - endWorldPos) * 0.5f + endWorldPos;
								targetComponent.WorldPosition = newWorldPos;
								targetComponent.LocalStartPosition = startWorldPos - newWorldPos;
								targetComponent.LocalEndPosition = endWorldPos - newWorldPos;
							}
						}
					}
				}
			}
		}
	}

	void CloseWindow()
	{
		IsClosed = true;
		// TODO internal ?
		// Release();
		Rebuild();
		Position = Parent.Size - 32;
	}

	public void OnSelectionChanged( NavMeshLink link )
	{
		targetComponent = link;
		// If we already in operation only update title
		if ( !isPickingStartOrEnd )
		{
			Rebuild();
		}
	}

	public void OnDisabled()
	{
	}
}
