using Sandbox.UI;

namespace Editor;

/// <summary>
/// Represents a single row in a control sheet UI, providing editing and validation functionality for a serialized
/// property.
/// </summary>
class ControlSheetRow : Widget
{
	public ControlWidget ControlWidget { get; private set; }

	SerializedProperty property;
	bool includeExtraInfo;
	ControlSheetLabel label;
	GridLayout gridLayout;
	GridLayout validateResultContainer;

	public ControlSheetRow( SerializedProperty property, ControlWidget editor )
	{
		this.property = property;
		FocusMode = FocusMode.Click;
		ControlWidget = editor;

		property.OnFinishEdit += OnPropertyFinishEdit;

		gridLayout = Layout.Grid();
		gridLayout.HorizontalSpacing = 0;
		Layout = gridLayout;
	}

	public static ControlWidget CreateEditor( SerializedProperty property )
	{
		if ( property.PropertyType.IsAssignableTo( typeof( Resource ) ) )
		{
			return new ResourceWrapperControlWidget( property );
		}

		try
		{
			return ControlWidget.Create( property );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Error creating ControlWidget for {property.Name}" );
		}

		return default;
	}

	public static ControlSheetRow Create( SerializedProperty property, bool includeExtraInfo = false )
	{
		ControlWidget editor = CreateEditor( property );
		if ( !editor.IsValid() ) return null;

		return Create( property, editor, includeExtraInfo );
	}

	public static ControlSheetRow Create( SerializedProperty property, ControlWidget editor, bool includeExtraInfo = false )
	{
		if ( !editor.IsValid() ) return null;

		var row = new ControlSheetRow( property, editor );
		row.includeExtraInfo = includeExtraInfo;
		row.ContentMargins = new Margin( 6, 2, 0, 0 );

		row.Rebuild();

		return row;
	}

	public bool UpdateVisibility()
	{
		var ss = property.ShouldShow();
		if ( ss == !Hidden )
			return false;

		Hidden = !ss;
		return true;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		var isPropertyOverridden = EditorUtility.Prefabs.IsPropertyOverridden( property ) || EditorUtility.Prefabs.IsComponentAddedToInstance( property.Parent?.Targets?.OfType<Component>().FirstOrDefault() );
		if ( isPropertyOverridden )
		{
			var overrideIndicatorRect = LocalRect;
			overrideIndicatorRect.Width = 2f;
			Paint.SetBrush( Theme.Blue.Darken( 0.25f ) );
			Paint.ClearPen();
			Paint.DrawRect( overrideIndicatorRect );
		}
	}

	public void OnPropertyFinishEdit( SerializedProperty property )
	{
		UpdateValidation();
	}

	public void Rebuild()
	{
		if ( property is null )
			return;

		float spaceAbove = 0;

		if ( property.TryGetAttribute( out SpaceAttribute spaceAttr ) )
		{
			spaceAbove = spaceAttr.Height;
		}

		var hasLabel = ControlWidget.IncludeLabel;
		var isExpanded = ControlWidget.IsWideMode;
		if ( property.TryGetAttribute<WideModeAttribute>( out var wideMode ) )
		{
			isExpanded = true;
			hasLabel = ControlWidget.IncludeLabel && wideMode.HasLabel;
		}

		gridLayout.Margin = new Margin( 0, spaceAbove, 4, 0 );

		if ( property.TryGetAttribute( out InfoBoxAttribute infoAttribute ) )
		{
			var header = new InfoBoxWidget( infoAttribute.Message, infoAttribute.Tint, infoAttribute.Icon );
			gridLayout.AddCell( 0, 0, header, 10, 1 );
		}

		if ( property.TryGetAttribute( out HeaderAttribute headerAttribute ) )
		{
			var header = new Label.Header( headerAttribute.Title );
			gridLayout.AddCell( 0, 1, header, 10, 1 );
		}

		ToolTip = ControlSheetFormatter.GetPropertyToolTip( property, includeExtraInfo );
		HorizontalSizeMode = SizeMode.CanShrink;

		label = new ControlSheetLabel( property );

		ControlWidget.HorizontalSizeMode = SizeMode.Flexible;

		if ( property.IsNullable )
		{
			gridLayout.AddCell( 1, 5, new PropertyButton( property, ControlWidget ), 1, 1, TextFlag.LeftTop );
			ControlWidget.Enabled = !property.IsNull;
		}

		if ( hasLabel )
		{
			label.ContentMargins = isExpanded ? new( 0, 0, 0, 4 ) : new( 0, 0, 4, 0 );
			gridLayout.AddCell( 2, 5, label, xSpan: (isExpanded ? 2 : 1), alignment: TextFlag.LeftTop );
			gridLayout.AddCell( 3 - (isExpanded ? 1 : 0), 5 + (isExpanded ? 1 : 0), ControlWidget, alignment: TextFlag.LeftTop );
		}
		else
		{
			gridLayout.AddCell( 2, 5, ControlWidget, xSpan: 2, alignment: TextFlag.LeftTop );
		}

		var validateAttributes = property.GetAttributes<ValidateAttribute>().ToList();
		if ( validateAttributes.Any() )
		{
			validateResultContainer = (GridLayout)gridLayout.AddCell( 0, 6, new GridLayout(), 10, 1 );
		}
		else if ( validateResultContainer.IsValid() )
		{
			validateResultContainer.Destroy();
		}

		gridLayout.SetColumnStretch( 0, 0, 0, 1 );
		gridLayout.SetMinimumColumnWidth( 0, 0 );
		gridLayout.SetMinimumColumnWidth( 1, 0 );
		gridLayout.SetMinimumColumnWidth( 2, 140 );

		UpdateValidation();
	}

	private void UpdateValidation()
	{
		var validateAttributes = property.GetAttributes<ValidateAttribute>().ToList();
		if ( validateAttributes.Any() && validateResultContainer.IsValid() )
		{
			var owner = property.Parent.Targets.FirstOrDefault();
			var typeDesc = TypeLibrary.GetType( owner.GetType() );
			var propertyValue = property.GetValue<object>();

			int rowOffset = 0;

			validateResultContainer.Clear( true );

			// Process each validation attribute
			foreach ( var validateAttribute in validateAttributes )
			{
				var validationResult = validateAttribute.Validate( owner, typeDesc, propertyValue );

				// Only display non-success results
				if ( !validationResult.Success )
				{
					var tint = validationResult.Status switch
					{
						LogLevel.Warn => EditorTint.Yellow,
						LogLevel.Error => EditorTint.Red,
						LogLevel.Info => EditorTint.Blue,
						LogLevel.Trace => EditorTint.Blue,
						_ => EditorTint.Blue
					};

					var icon = validationResult.Status switch
					{
						LogLevel.Warn => "warning",
						LogLevel.Error => "error",
						LogLevel.Info => "info",
						LogLevel.Trace => "info",
						_ => "info"
					};

					var header = new InfoBoxWidget( validationResult.Message, tint, icon );
					validateResultContainer.AddCell( 0, rowOffset, header, 10, 1 );

					rowOffset++;
				}
			}
		}
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		e.Accepted = true;

		var menu = new ContextMenu( this );
		menu.AddOption( $"Copy {property.DisplayName}", "content_copy", () =>
		{
			string str = ControlWidget.ToClipboardString();
			EditorUtility.Clipboard.Copy( str );
		} );

		menu.AddOption( $"Paste as {property.DisplayName}", "content_paste", () =>
		{
			string str = EditorUtility.Clipboard.Paste();
			ControlWidget.FromClipboardString( str );
		} );


		menu.AddOption( "Reset to Default", "restart_alt", () =>
		{
			property.Parent.NoteStartEdit( property );
			property.SetValue( property.GetDefault() );
			property.Parent.NoteFinishEdit( property );
		} );

		bool isPrefab = property.GetContainingGameObject()?.IsPrefabInstance ?? false;
		if ( isPrefab && (IsEditingComponent || IsEditingGameObject) )
		{
			menu.AddSeparator();

			var isPropertyModified = EditorUtility.Prefabs.IsPropertyOverridden( property );

			object editedObject = EditedComponents.FirstOrDefault();
			editedObject ??= EditedGameObjects.FirstOrDefault();
			var prefabName = EditorUtility.Prefabs.GetOuterMostPrefabName( editedObject ) ?? "";

			var revertActionName = "Revert Change";
			menu.AddOption( revertActionName, "history", () =>
			{
				using var scene = SceneEditorSession.Scope();

				using ( SceneEditorSession.Active.UndoScope( revertActionName ).WithComponentChanges( EditedComponents ).WithGameObjectChanges( EditedGameObjects, GameObjectUndoFlags.Properties ).Push() )
				{
					EditorUtility.Prefabs.RevertPropertyChange( property );
				}
			} ).Enabled = isPropertyModified;

			menu.AddOption( "Apply to Prefab", "save", () =>
			{
				EditorUtility.Prefabs.ApplyPropertyChange( property );
			} ).Enabled = isPropertyModified;
		}

		if ( CodeEditor.CanOpenFile( property.SourceFile ) )
		{
			menu.AddSeparator();

			var filename = System.IO.Path.GetFileName( property.SourceFile );
			menu.AddOption( $"Jump to code", "code", action: () => CodeEditor.OpenFile( property.SourceFile, property.SourceLine ) );
		}

		AddComponentOptions( menu );

		menu.OpenAt( e.ScreenPosition, false );
	}

	bool IsEditingComponent => property.Parent?.Targets?.OfType<Component>().FirstOrDefault( x => x.IsValid() ) is not null;
	bool IsEditingGameObject => property.Parent?.Targets?.OfType<GameObject>().FirstOrDefault( x => x.IsValid() ) is not null || property.Parent?.Targets?.OfType<GameTransform>().FirstOrDefault() is not null;

	IEnumerable<Component> EditedComponents => property.Parent?.Targets?.OfType<Component>().Where( x => x.IsValid() ) ?? Enumerable.Empty<Component>();
	IEnumerable<GameObject> EditedGameObjects => (property.Parent?.Targets?.OfType<GameObject>().Where( x => x.IsValid() ) ?? Enumerable.Empty<GameObject>()).Concat( property.Parent?.Targets?.OfType<GameTransform>().Where( x => x.GameObject.IsValid() ).Select( x => x.GameObject ) ?? Enumerable.Empty<GameObject>() );

	void AddComponentOptions( Menu menu )
	{
		// Are we editing the property of a component?
		if ( !IsEditingComponent )
			return;

		var component = EditedComponents.FirstOrDefault();

		// Only show if we're editing in a game session
		var session = SceneEditorSession.Resolve( component?.GameObject?.Scene );
		if ( session is null )
			return;

		// try to find the version of this component in the editor session
		var targetComponent = session.Scene.Directory.FindComponentByGuid( component.Id );
		if ( !targetComponent.IsValid() ) return;

		// get a serialized version of this property from that session
		var so = targetComponent.GetSerialized();
		var prop = so.GetProperty( property.Name );

		// add option to apply this value to that scene
		menu.AddSeparator();
		var setter = menu.AddOption( "Apply to Scene", "save", () =>
		{
			using var scope = session.Scene.Push();

			using ( session.UndoScope( "Apply to Scene" ).WithComponentChanges( targetComponent ).Push() )
			{
				prop.SetValue<object>( property.GetValue<object>() );
			}
		} );
		setter.Enabled = Json.Serialize( prop.GetValue<object>() ) != Json.Serialize( property.GetValue<object>() );
	}
}

internal class InfoBoxWidget : Widget
{
	private string message;
	private EditorTint tint;
	private string icon;

	public InfoBoxWidget( string message, EditorTint tint, string icon )
	{
		this.message = message;
		this.tint = tint;
		this.icon = icon;

		SetSizeMode( SizeMode.Flexible, SizeMode.Expand );

		Layout = Layout.Row();
		Layout.Margin = new Margin( 12, 12, 12, 20 );

		Layout.AddSpacingCell( 32 );
		Layout.Add( new Label( message ) { WordWrap = true } );
	}

	protected override Vector2 SizeHint()
	{
		return 1000;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.SetBrushAndPen( Theme.GetTint( tint ).Darken( 0.3f ) );
		Paint.DrawRect( LocalRect.Shrink( 3 ).Shrink( 0, 0, 0, 8 ), 5 );

		Paint.SetPen( Theme.GetTint( tint ).Desaturate( 0.5f ).Lighten( 5 ) );
		Paint.DrawIcon( new Rect( 10, 18 ), icon, 18, TextFlag.Center );
	}
}

/// <summary>
/// A button to the left of the property, allows toggling NULL state on nullabe values.
/// </summary>
file class PropertyButton : Widget
{
	SerializedProperty property;
	ControlWidget controlWidget;

	public PropertyButton( SerializedProperty property, ControlWidget controlWidget )
	{
		this.property = property;
		this.controlWidget = controlWidget;

		FixedHeight = Theme.RowHeight;
		FixedWidth = Theme.RowHeight;
		HorizontalSizeMode = SizeMode.Flexible;
		Cursor = CursorShape.Finger;
		ToolTip = "Has Value";
	}

	protected override void OnPaint()
	{
		var icon = "eject";
		var size = 15;
		Color color = Theme.TextControl.WithAlpha( 0.3f );
		Paint.TextAntialiasing = true;

		// Nullable - and null
		var isMultiple = property.IsMultipleDifferentValues;
		if ( property.IsNull && !isMultiple )
		{
			icon = "radio_button_unchecked";
			size = 10;
			color = Theme.TextControl.WithAlpha( 0.3f );
		}
		else
		{
			icon = "circle";
			size = 10;
			color = isMultiple ? Theme.MultipleValues : Theme.Blue;
			color = color.WithAlpha( 0.3f );
		}

		Paint.Pen = color;
		Paint.DrawIcon( LocalRect, icon, size );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		property.SetNullState( !property.IsNull );

		controlWidget.Enabled = !property.IsNull;
		Update();
	}
}

