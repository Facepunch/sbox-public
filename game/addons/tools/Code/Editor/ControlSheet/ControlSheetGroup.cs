using Sandbox.UI;

namespace Editor;

/// <summary>
/// Represents a group of related controls within a control sheet, optionally with a collapsible header and property
/// name display.
/// </summary>
public class ControlSheetGroup : Widget
{
	internal List<SerializedProperty> properties;
	List<ControlSheetRow> rows = new();
	string groupCookie;
	Widget Body;

	bool hasHeader;

	public bool IncludePropertyNames = false;

	public ControlSheetGroup( string groupName, SerializedProperty[] props, bool includePropertyNames = false )
	{
		if ( !string.IsNullOrWhiteSpace( groupName ) )
		{
			groupCookie = $"controlsheetgroup.{groupName}";
		}

		IncludePropertyNames = includePropertyNames;
		VerticalSizeMode = SizeMode.CanGrow;
		HorizontalSizeMode = SizeMode.Flexible;

		Layout = Layout.Column();
		Layout.Margin = new Margin( 0, 0, 12, 0 );

		bool closed = props.SelectMany( x => x.GetAttributes<GroupAttribute>() ).Any( x => x.StartFolded );
		properties = props.ToList();
		GroupHeader headerWidget = null;

		if ( groupCookie is not null )
		{
			closed = EditorCookie.Get( groupCookie, closed );
		}

		visibilityDebounce = 10;

		if ( !string.IsNullOrWhiteSpace( groupName ) )
		{
			headerWidget = new GroupHeader( this );

			Layout.Add( headerWidget );

			headerWidget.Title = groupName;

			var toggleGroup = props.FirstOrDefault( x => x.HasAttribute<ToggleGroupAttribute>() && x.Name == groupName );
			if ( toggleGroup is not null )
			{
				toggleGroup.TryGetAttribute<ToggleGroupAttribute>( out var toggleAttr );
				if ( toggleGroup is not null )
				{
					properties.Remove( toggleGroup );

					var enabler = ControlWidget.Create( toggleGroup );

					headerWidget.Title = toggleAttr.Label ?? groupName;
					headerWidget.ToolTip = toggleGroup.Description;
					headerWidget.AddToggle( toggleGroup, enabler );

					if ( !toggleGroup.As.Bool ) closed = true;
				}
			}
		}

		hasHeader = headerWidget.IsValid();

		Body = new Widget();
		Body.Hidden = true;
		Body.VerticalSizeMode = SizeMode.CanGrow;
		Body.HorizontalSizeMode = SizeMode.Flexible;

		Body.Layout = Layout.Column();
		Body.Layout.Margin = new Margin( hasHeader ? 12 : 0, 4, 0, 4 );
		Body.Layout.Spacing = 0;

		Layout.Add( Body );

		if ( hasHeader )
		{
			firstOpen = () => BuildContents();
			headerWidget.OnToggled += SetVisible;

			if ( !closed )
				headerWidget.Toggle();
		}
		else
		{
			BuildContents();
			Body.Visible = true;
		}

		//
		// If none of our properties are visible, hide the whole group
		//
		if ( !properties.Any( x => x.ShouldShow() ) )
		{
			Visible = false;
		}

		// Log.Info( $"{groupName}: {string.Join( ",", properties.Where( x => x.ShouldShow() ).Select( x => x.Name ) )}" );
	}

	Action firstOpen;

	void SetVisible( bool visible )
	{
		using var x = SuspendUpdates.For( Parent );

		firstOpen?.Invoke();
		firstOpen = null;

		Body.Hidden = !visible;

		UpdateGeometry();
		Parent?.UpdateGeometry();

		if ( groupCookie is not null )
		{
			EditorCookie.Set( groupCookie, !visible );
		}
	}

	private void BuildContents()
	{
		foreach ( var prop in properties )
		{
			if ( prop.HasAttribute<HideAttribute>() )
				continue;

			var row = ControlSheetRow.Create( prop, IncludePropertyNames );
			if ( row.IsValid() )
			{
				rows.Add( row );
				Body.Layout.Add( row );
				row.UpdateVisibility();
			}
		}
	}

	RealTimeSince visibilityDebounce = 0;


	[EditorEvent.Frame]
	public void UpdateVisibility()
	{
		if ( Parent is null )
			return;

		if ( visibilityDebounce < 0.2f )
			return;

		visibilityDebounce = Random.Shared.Float( 0, 0.1f );

		Visible = properties?.Any( x => x.ShouldShow() ) ?? false;

		if ( !Visible )
			return;

		bool bChanged = false;

		foreach ( var r in rows )
		{
			bChanged = r.UpdateVisibility() || bChanged;
		}
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		if ( hasHeader && !Body.Hidden )
		{
			{
				var r = LocalRect.Shrink( 8, 4, 0, 0 );
				r.Width = 3;

				Paint.SetBrushAndPen( Theme.WindowBackground );
				Paint.DrawRect( r, 5 );
			}

			{
				var r = LocalRect.Shrink( 10, 0, 0, 0 );
				r.Top = r.Bottom - 3;
				r.Width = 8;

				Paint.SetBrushAndPen( Theme.WindowBackground );
				Paint.DrawRect( r, 5 );
			}
		}

	}
}


file class GroupHeader : Widget
{
	ControlSheetGroup groupControl;
	Layout toggleLayout;
	ControlWidget toggleControl;
	SerializedProperty toggleProperty;

	public GroupHeader( ControlSheetGroup group ) : base( null )
	{
		FixedHeight = Theme.RowHeight;
		VerticalSizeMode = SizeMode.CanGrow;
		HorizontalSizeMode = SizeMode.Flexible;
		Cursor = CursorShape.Finger;
		Layout = Layout.Row();
		Layout.Spacing = 5;
		Layout.Margin = new Margin( 16, 0, 0, 0 );
		Layout.AddSpacingCell( 10 );

		groupControl = group;
		toggleLayout = Layout.AddColumn();

		Layout.AddStretchCell();
	}

	public string Title { get; set; }


	internal void AddToggle( SerializedProperty toggleProp, ControlWidget controlWidget )
	{
		controlWidget.FixedHeight = 17;
		controlWidget.FixedWidth = 17;
		controlWidget.Tint = Theme.Green;

		toggleLayout.Add( controlWidget );
		toggleControl = controlWidget;
		toggleProperty = toggleProp;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( e.Button == MouseButtons.Left )
		{
			Toggle();
		}
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		base.OnContextMenu( e );

		var menu = new ContextMenu( this );

		{
			menu.AddOption( $"Copy {Title} Properties", "content_copy", () =>
			{
				ClipboardTools.CopyProperties( Title, groupControl.properties );
			} );
			var pasteOption = menu.AddOption( $"Paste {Title} Properties", "content_paste", () =>
			{
				ClipboardTools.PasteProperties( Title, groupControl.properties );
			} );
			pasteOption.Enabled = ClipboardTools.CanPasteProperties( Title, groupControl.properties );
			menu.AddOption( "Reset all to Default", "restart_alt", () =>
			{
				foreach ( var prop in groupControl.properties )
				{
					prop.SetValue( prop.GetDefault() );
				}
			} );

			menu.AddSeparator();

			var editedComponents = groupControl.properties.First().Parent?.Targets?.OfType<Component>().Where( x => x.IsValid() ) ?? Enumerable.Empty<Component>();

			var arePropertiesModified = groupControl.properties.Any( prop => EditorUtility.Prefabs.IsPropertyOverridden( prop ) );

			var prefabName = EditorUtility.Prefabs.GetOuterMostPrefabName( editedComponents.First() );

			var revertAllActionName = $"Revert all {Title} instance changes";

			menu.AddOption( revertAllActionName, "history", () =>
			{
				using var scene = SceneEditorSession.Scope();

				using ( SceneEditorSession.Active.UndoScope( revertAllActionName ).WithComponentChanges( editedComponents ).Push() )
				{
					groupControl.properties.ForEach( prop => EditorUtility.Prefabs.RevertPropertyChange( prop ) );
				}
			} ).Enabled = arePropertiesModified;

			var applyAllActionName = $"Apply all {Title} instance changes to prefab \"{prefabName}\"";

			menu.AddOption( applyAllActionName, "update", () =>
			{
				using var scene = SceneEditorSession.Scope();

				using ( SceneEditorSession.Active.UndoScope( applyAllActionName ).WithComponentChanges( editedComponents ).Push() )
				{
					groupControl.properties.ForEach( prop => EditorUtility.Prefabs.ApplyPropertyChange( prop ) );
				}
			} ).Enabled = arePropertiesModified;

			// check toggle group property
			if ( toggleProperty != null )
			{
				menu.AddSeparator();

				var isToggleGroupPropModified = EditorUtility.Prefabs.IsPropertyOverridden( toggleProperty );

				var revertToggleActionName = $"Revert Toggle State instance change";

				menu.AddOption( revertToggleActionName, "history", () =>
				{
					using var scene = SceneEditorSession.Scope();

					using ( SceneEditorSession.Active.UndoScope( revertToggleActionName ).WithComponentChanges( editedComponents ).Push() )
					{
						EditorUtility.Prefabs.RevertPropertyChange( toggleProperty );
					}
				} ).Enabled = isToggleGroupPropModified;

				var applyToggleActionName = $"Apply Toggle State instance change to prefab \"{prefabName}\"";

				menu.AddOption( applyToggleActionName, "update", () =>
				{
					using var scene = SceneEditorSession.Scope();

					using ( SceneEditorSession.Active.UndoScope( applyToggleActionName ).WithComponentChanges( editedComponents ).Push() )
					{
						EditorUtility.Prefabs.ApplyPropertyChange( toggleProperty );
					}

				} ).Enabled = isToggleGroupPropModified;
			}
		}

		if ( toggleProperty is not null && CodeEditor.CanOpenFile( toggleProperty.SourceFile ) )
		{
			menu.AddSeparator();
			menu.AddOption( "Jump to code", "code", action: () => CodeEditor.OpenFile( toggleProperty.SourceFile, toggleProperty.SourceLine ) );
		}

		menu.OpenAtCursor();
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		e.Accepted = false;
	}

	protected override void OnPaint()
	{
		float spacing = toggleControl is BoolControlWidget ? 5 : 0;

		var textRect = Paint.MeasureText( LocalRect.Shrink( toggleLayout.OuterRect.Right + spacing, 0, 0, 0 ), Title, TextFlag.LeftCenter );

		var backgroundRect = LocalRect.Shrink( 3, 4, 4, 4 );
		backgroundRect.Height = 22 - 8;
		backgroundRect.Right = textRect.Right + 8;
		backgroundRect.Width = backgroundRect.Height;

		// Background
		{
			if ( toggleProperty != null && EditorUtility.Prefabs.IsPropertyOverridden( toggleProperty ) )
			{
				var overrideIndicatorRect = backgroundRect;
				overrideIndicatorRect.Left += backgroundRect.Width + 1;
				overrideIndicatorRect.Width = 2;
				Paint.SetBrushAndPen( Theme.Blue.Darken( 0.25f ) );
				Paint.DrawRect( overrideIndicatorRect );
			}

			var backgroundColor = Theme.WindowBackground.Lighten( 0.2f ).WithAlphaMultiplied( state ? 1 : 0.5f );

			Paint.SetBrushAndPen( backgroundColor );
			Paint.DrawRect( backgroundRect, 6 );
		}

		Paint.ClearBrush();

		var iconIntensity = IsUnderMouse ? 1.5f : 1f;
		if ( state )
		{
			Paint.Pen = Theme.TextControl.WithAlpha( 0.2f * iconIntensity );
			Paint.DrawIcon( backgroundRect, "remove", 12, TextFlag.Center );
		}
		else
		{
			Paint.Pen = Theme.TextControl.WithAlpha( 0.4f * iconIntensity );
			Paint.DrawIcon( backgroundRect, "add", 12, TextFlag.Center );
		}

		Paint.Pen = Theme.TextControl.WithAlpha( state ? 1 : 0.8f );

		Paint.SetDefaultFont( 11, weight: 400, sizeInPixels: true );
		Paint.DrawText( LocalRect.Shrink( toggleLayout.OuterRect.Right + spacing, 0, 0, 0 ), Title, TextFlag.LeftCenter );
	}

	bool state = false;

	public void Toggle()
	{
		state = !state;
		OnToggled?.Invoke( state );

		if ( CookieName is not null )
		{
			ProjectCookie.Set( CookieName, state );
		}
	}

	public Action<bool> OnToggled;

	string _cookieName;

	public string CookieName
	{
		get
		{
			return _cookieName;
		}

		set
		{
			_cookieName = value;

			var newState = ProjectCookie.Get<bool>( _cookieName, state );
			if ( newState == state ) return;

			Toggle();
		}
	}
}


