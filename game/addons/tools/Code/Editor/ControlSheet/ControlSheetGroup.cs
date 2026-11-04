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
	GroupHeader headerWidget;
	SerializedProperty toggleGroup;

	bool hasHeader;

	/// <summary>
	/// Whether property rows also display their underlying member names.
	/// </summary>
	public bool IncludePropertyNames = false;

	/// <summary>
	/// Creates a property group with an optional collapsible heading.
	/// </summary>
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

			toggleGroup = props.FirstOrDefault( x => x.HasAttribute<ToggleGroupAttribute>() && x.Name == groupName );
			if ( toggleGroup is not null )
			{
				toggleGroup.TryGetAttribute<ToggleGroupAttribute>( out var toggleAttr );
				if ( toggleGroup is not null )
				{
					properties.Remove( toggleGroup );

					var enabler = ControlWidget.Create( toggleGroup );

					headerWidget.Title = toggleAttr.Label ?? groupName;

					if ( toggleGroup.TryGetAttribute<TitleAttribute>( out var titleAttr ) && !string.IsNullOrWhiteSpace( titleAttr.Value ) )
					{
						headerWidget.Title = titleAttr.Value;
					}

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
		// Open groups use whitespace instead of an indented tree bracket.
		Body.Layout.Margin = new Margin( 0, 4, 0, hasHeader ? 12 : 4 );
		Body.Layout.Spacing = 0;

		Layout.Add( Body );

		if ( hasHeader )
		{
			firstOpen = () => BuildContents();
			headerWidget.OnToggled += SetVisible;

			// Defer initial expansion to the first frame to avoid building all content synchronously during inspector construction
			_deferredExpand = !closed;
		}
		else
		{
			_deferredExpand = true;
		}

		//
		// Hide the group if its toggle is hidden or none of its properties are visible.
		//
		if ( toggleGroup?.ShouldShow() == false || !properties.Any( x => x.ShouldShow() ) )
		{
			Visible = false;
		}

		// Log.Info( $"{groupName}: {string.Join( ",", properties.Where( x => x.ShouldShow() ).Select( x => x.Name ) )}" );
	}

	Action firstOpen;
	bool _deferredExpand;

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


	/// <summary>
	/// Expands deferred contents and refreshes conditional property visibility.
	/// </summary>
	[EditorEvent.Frame]
	public void UpdateVisibility()
	{
		if ( Parent is null )
			return;

		if ( _deferredExpand )
		{
			_deferredExpand = false;

			if ( hasHeader )
			{
				// This will call BuildContents eventually
				headerWidget?.Toggle();
			}
			else
			{
				BuildContents();
				Body.Visible = true;
			}
		}

		if ( visibilityDebounce < 0.2f )
			return;

		visibilityDebounce = Random.Shared.Float( 0, 0.1f );

		Visible = (toggleGroup?.ShouldShow() ?? true) && (properties?.Any( x => x.ShouldShow() ) ?? false);

		if ( !Visible )
			return;

		bool bChanged = false;

		foreach ( var r in rows )
		{
			bChanged = r.UpdateVisibility() || bChanged;
		}
	}

}


/// <summary>
/// Compact uppercase heading with an aligned solid divider and a muted folded state.
/// </summary>
class GroupHeader : Widget
{
	ControlSheetGroup groupControl;
	Layout toggleLayout;
	ControlWidget toggleControl;
	SerializedProperty toggleProperty;

	/// <summary>
	/// Creates the heading and reserves space for an optional group toggle.
	/// </summary>
	public GroupHeader( ControlSheetGroup group ) : base( null )
	{
		FixedHeight = Theme.RowHeight;
		VerticalSizeMode = SizeMode.CanGrow;
		HorizontalSizeMode = SizeMode.Flexible;
		Cursor = CursorShape.Finger;
		Layout = Layout.Row();
		Layout.Spacing = 5;
		Layout.Margin = new Margin( 6, 0, 24, 0 );

		groupControl = group;
		toggleLayout = Layout.AddColumn();

		Layout.AddStretchCell();
	}

	/// <summary>
	/// Original group label, also used by property context menu actions.
	/// </summary>
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
				var session = SceneEditorSession.Resolve( editedComponents.FirstOrDefault() );
				using ( session.UndoScope( revertAllActionName ).WithComponentChanges( editedComponents ).Push() )
				{
					groupControl.properties.ForEach( prop => EditorUtility.Prefabs.RevertPropertyChange( prop ) );
				}
			} ).Enabled = arePropertiesModified;

			var applyAllActionName = $"Apply all {Title} instance changes to prefab \"{prefabName}\"";

			menu.AddOption( applyAllActionName, "update", () =>
			{
				var session = SceneEditorSession.Resolve( editedComponents.FirstOrDefault() );
				using ( session.UndoScope( applyAllActionName ).WithComponentChanges( editedComponents ).Push() )
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
					var session = SceneEditorSession.Resolve( editedComponents.FirstOrDefault() );
					using ( session.UndoScope( revertToggleActionName ).WithComponentChanges( editedComponents ).Push() )
					{
						EditorUtility.Prefabs.RevertPropertyChange( toggleProperty );
					}
				} ).Enabled = isToggleGroupPropModified;

				var applyToggleActionName = $"Apply Toggle State instance change to prefab \"{prefabName}\"";

				menu.AddOption( applyToggleActionName, "update", () =>
				{
					var session = SceneEditorSession.Resolve( editedComponents.FirstOrDefault() );
					using ( session.UndoScope( applyToggleActionName ).WithComponentChanges( editedComponents ).Push() )
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
		var textLeft = toggleControl.IsValid() ? toggleLayout.OuterRect.Right + 5 : 6;
		var dividerLeft = 6 + ControlSheetRow.LabelColumnWidth;
		var dividerRight = LocalRect.Right - 28;
		var textBounds = LocalRect.Shrink( textLeft, 0, 28, 0 );
		textBounds.Width = Math.Max( 0, Math.Min( textBounds.Right, dividerLeft - 12 ) - textLeft );

		// Bright neutral foreground when expanded, with a quieter neutral tone when folded.
		var headerColor = state ? Color.FromBytes( 240, 240, 237 ) : Color.FromBytes( 175, 175, 172 );
		var textColor = IsUnderMouse ? Color.Lerp( headerColor, Color.White, 0.22f ) : headerColor;

		Paint.SetDefaultFont( 10, weight: 600, sizeInPixels: true );

		// Keep even long titles inside the label column so every divider starts at the same position.
		var title = Paint.GetElidedText( Title?.ToUpperInvariant() ?? "", Math.Max( 0, textBounds.Width ), flags: TextFlag.LeftCenter );
		Paint.SetPen( textColor );
		Paint.DrawText( textBounds, title, TextFlag.LeftCenter );

		if ( dividerRight > dividerLeft )
		{
			var dividerAlpha = state ? 0.4f : 0.3f;

			Paint.SetBrushAndPen( textColor.WithAlpha( dividerAlpha ) );
			Paint.DrawRect( new Rect( dividerLeft, LocalRect.Center.y, dividerRight - dividerLeft, 1 ) );
		}

		var iconRect = LocalRect.Shrink( 0, 0, 4, 0 );
		iconRect.Left = iconRect.Right - 16;

		Paint.SetPen( textColor );
		Paint.DrawIcon( iconRect, state ? "expand_more" : "chevron_right", 12, TextFlag.Center );

		// Keep prefab override feedback beside the toggle, independent of disclosure.
		if ( toggleProperty is not null && EditorUtility.Prefabs.IsPropertyOverridden( toggleProperty ) )
		{
			var overrideIndicatorRect = LocalRect.Shrink( 1, 5, 0, 5 );
			overrideIndicatorRect.Width = 2;
			Paint.SetBrushAndPen( Theme.Blue.Darken( 0.25f ) );
			Paint.DrawRect( overrideIndicatorRect );
		}
	}

	bool state = false;

	/// <summary>
	/// Toggles content visibility and persists the optional project cookie.
	/// </summary>
	public void Toggle()
	{
		state = !state;
		OnToggled?.Invoke( state );
		Update();

		if ( CookieName is not null )
		{
			ProjectCookie.Set( CookieName, state );
		}
	}

	/// <summary>
	/// Called with the expanded state whenever the heading is toggled.
	/// </summary>
	public Action<bool> OnToggled;

	string _cookieName;

	/// <summary>
	/// Optional project cookie used to restore the expanded state.
	/// </summary>
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


