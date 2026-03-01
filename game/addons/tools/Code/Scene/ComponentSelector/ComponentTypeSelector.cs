using System.IO;

namespace Editor;

/// <summary>
/// A popup dialog to select a component type
/// </summary>
internal class ComponentTypeSelector : PopupWidget
{
	public Action<TypeDescription> OnSelect
	{
		get => Widget.OnComponentSelect;
		set => Widget.OnComponentSelect = value;
	}

	ComponentTypeSelectorWidget Widget { get; set; }

	public ComponentTypeSelector( Widget parent ) : base( parent )
	{
		Widget = new ComponentTypeSelectorWidget( this );
		Widget.OnFinished = Destroy;

		Layout = Layout.Column();
		Layout.Add( Widget );

		DeleteOnClose = true;
	}
}

internal partial class ComponentTypeSelectorWidget : AdvancedDropdownWidget
{
	public Action<TypeDescription> OnComponentSelect { get; set; }

	const string CategorySeparator = "/";
	const string NoCategoryName = "Uncategorized";

	/// <summary>
	/// If this is enabled then only user created components will be shown
	/// </summary>
	public bool HideBaseComponents
	{
		get => _hideBaseComponents;
		set
		{
			_hideBaseComponents = value;
			EditorCookie.Set( "ComponentSelector.HideBase", value );
			Rebuild();
		}
	}
	bool _hideBaseComponents = EditorCookie.Get<bool>( "ComponentSelector.HideBase", false );

	public bool FlatView
	{
		get => _flatView;
		set
		{
			_flatView = value;
			EditorCookie.Set( "ComponentSelector.FlatView", value );
			Rebuild();
		}
	}
	bool _flatView = EditorCookie.Get<bool>( "ComponentSelector.FlatView", false );

	public ComponentTypeSelectorWidget( Widget parent ) : base( parent )
	{
		SearchPlaceholderText = "Search Components";
		RootTitle = "Component";

		FilterWidget = new ComponentFilterControlWidget( this );

		OnBuildItems = BuildComponentTree;

		SearchScorer = ComponentSearchScore;

		OnSelect = ( value ) =>
		{
			if ( value is TypeDescription type )
			{
				OnComponentSelect?.Invoke( type );
			}
		};

		Rebuild();
	}

	void BuildComponentTree( AdvancedDropdownItem root )
	{
		var types = EditorTypeLibrary.GetTypes<Component>()
			.Where( x => !x.IsAbstract && !x.HasAttribute<HideAttribute>() && !x.HasAttribute<ObsoleteAttribute>() );

		if ( HideBaseComponents )
		{
			types = types.Where( x => !x.FullName.StartsWith( "Sandbox." ) );
		}

		// "New Component" entry at the top
		var newComponentItem = new AdvancedDropdownItem( "New Component", "note_add" );
		root.Add( newComponentItem );

		if ( FlatView )
		{
			foreach ( var type in types.OrderBy( x => x.Title ) )
			{
				root.Add( CreateComponentItem( type ) );
			}
			return;
		}

		var categories = types
			.Select( x => string.IsNullOrWhiteSpace( x.Group ) ? NoCategoryName : x.Group )
			.Distinct()
			.OrderBy( x => x )
			.ToArray();

		if ( categories.Length <= 1 )
		{
			// Single or no category - just list all types
			foreach ( var type in types.OrderBy( x => x.Title ) )
			{
				root.Add( CreateComponentItem( type ) );
			}
			return;
		}

		// Group types into category tree
		var categoryNodes = new Dictionary<string, AdvancedDropdownItem>();

		foreach ( var category in categories )
		{
			var topLevel = category.Split( CategorySeparator ).FirstOrDefault() ?? NoCategoryName;

			if ( !categoryNodes.TryGetValue( topLevel, out var categoryNode ) )
			{
				categoryNode = new AdvancedDropdownItem( topLevel );
				categoryNodes[topLevel] = categoryNode;
				root.Add( categoryNode );
			}

			// Add sub-categories if needed
			var parts = category.Split( CategorySeparator );
			if ( parts.Length > 1 )
			{
				var current = categoryNode;
				for ( int i = 1; i < parts.Length; i++ )
				{
					var subName = parts[i];
					var existing = current.Children.FirstOrDefault( x => x.Title == subName && x.HasChildren );
					if ( existing is null )
					{
						existing = new AdvancedDropdownItem( subName );
						current.Add( existing );
					}
					current = existing;
				}

				// Add types for this full category path
				foreach ( var type in types.Where( x => x.Group == category ).OrderBy( x => x.Title ) )
				{
					current.Add( CreateComponentItem( type ) );
				}
			}
			else
			{
				// Add types for this top-level category
				var categoryTypes = types.Where( x =>
					(category == NoCategoryName) ? (x.Group == null) : (x.Group == category) )
					.OrderBy( x => x.Title );

				foreach ( var type in categoryTypes )
				{
					categoryNode.Add( CreateComponentItem( type ) );
				}
			}
		}
	}

	static AdvancedDropdownItem CreateComponentItem( TypeDescription type )
	{
		return new AdvancedDropdownItem
		{
			Title = type.Title,
			Icon = type.Icon,
			Description = type.Description,
			Tooltip = $"<b>{type.FullName}</b><br/>{type.Description}",
			Value = type,
			PaintIcon = (!string.IsNullOrEmpty( type.Icon ))
				? ( rect, opacity ) => type.PaintComponentIcon( rect, opacity )
				: null
		};
	}

	int ComponentSearchScore( AdvancedDropdownItem item, string[] parts )
	{
		if ( item.Value is not TypeDescription type ) return 0;

		var score = 0;
		var t = type.Title.Replace( " ", "" );
		var c = type.ClassName.Replace( " ", "" );
		var d = type.Description.Replace( " ", "" );

		foreach ( var w in parts )
		{
			if ( t.Contains( w, StringComparison.OrdinalIgnoreCase ) ) score += 10;
			if ( c.Contains( w, StringComparison.OrdinalIgnoreCase ) ) score += 5;
			if ( d.Contains( w, StringComparison.OrdinalIgnoreCase ) ) score += 1;
		}

		return score;
	}

	protected override void OnBuildSearchResults( AdvancedDropdownPanel panel, string searchText )
	{
		panel.AddEntry( new ItemEntry( panel )
		{
			Text = $"New Component '{searchText}'",
			Icon = "note_add",
			MouseClick = () => OnNewComponentSelected( searchText )
		} );
	}

	protected override void BuildPanel( AdvancedDropdownPanel panel )
	{
		// Let base handle search results
		if ( IsSearching )
		{
			base.BuildPanel( panel );
			return;
		}

		// Handle "New Component" item specially - it triggers the template flow
		var items = panel.SourceItem?.Children ?? RootItem.Children;
		var newComponentItem = items.FirstOrDefault( x => x.Title == "New Component" && !x.HasChildren && x.Value is null );

		if ( newComponentItem is not null )
		{
			// Build normally but intercept the "New Component" item
			panel.ClearEntries();
			panel.ItemList.Add( panel.CategoryHeader );

			foreach ( var item in items )
			{
				if ( item == newComponentItem )
				{
					panel.AddEntry( new ItemEntry( panel, item )
					{
						Text = "New Component",
						Icon = "note_add",
						MouseClick = () => OnNewComponentSelected()
					} );
				}
				else if ( item.HasChildren )
				{
					panel.AddEntry( new CategoryEntry( panel )
					{
						Category = item.Title,
						MouseClick = () =>
						{
							var sub = new AdvancedDropdownPanel( Main, this, item.Title ) { SourceItem = item };
							PushPanel( sub );
						}
					} );
				}
				else
				{
					panel.AddEntry( new ItemEntry( panel, item )
					{
						MouseClick = () =>
						{
							OnSelect?.Invoke( item.Value );
							OnFinished?.Invoke();
						}
					} );
				}
			}

			panel.AddStretchCell();
			return;
		}

		base.BuildPanel( panel );
	}

	void OnNewComponentSelected( string componentName = "MyComponent" )
	{
		var templateTypes = ComponentTemplate.GetAllTypes();
		var panel = new AdvancedDropdownPanel( Main, this, "Create a new component" ) { IsManual = true };

		panel.AddEntry( new Label( "Name", this ) ).ContentMargins = new( 8, 8, 8, 8 );

		var lineEdit = new LineEdit( this );
		lineEdit.Text = componentName;
		lineEdit.ContentMargins = new( 8, 0, 8, 0 );
		lineEdit.MinimumHeight = 22;

		lineEdit.EditingStarted += () => IsTextInputActive = true;
		lineEdit.EditingFinished += () => IsTextInputActive = false;

		panel.AddEntry( lineEdit ).ContentMargins = 0;
		panel.AddEntry( new Label( "Create Script from Template", this ) ).ContentMargins = 8;

		foreach ( var componentTemplate in templateTypes )
		{
			panel.AddEntry( new ItemEntry( panel )
			{
				Icon = componentTemplate.Icon,
				Text = $"New {componentTemplate.Title}..",
				MouseClick = () => _ = CreateNewComponent( componentTemplate, lineEdit.Text )
			} );
		}

		panel.AddStretchCell();

		PushPanel( panel );

		lineEdit.Focus();
	}

	async Task CreateNewComponent( TypeDescription desc, string componentName )
	{
		var template = EditorTypeLibrary.Create<ComponentTemplate>( desc.Name );

		var codePath = template.DefaultDirectory;

		if ( !Directory.Exists( codePath ) )
		{
			Directory.CreateDirectory( codePath );
		}

		var fd = new FileDialog( EditorWindow );
		fd.Title = "Create new component..";
		fd.Directory = codePath;
		fd.DefaultSuffix = template.Suffix;
		fd.SelectFile( $"{componentName}{template.Suffix}" );
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( template.NameFilter );

		if ( !fd.Execute() )
			return;

		componentName = System.IO.Path.GetFileNameWithoutExtension( fd.SelectedFile );
		componentName = componentName.ToTitleCase().Replace( " ", "" );

		if ( !System.IO.File.Exists( fd.SelectedFile ) )
		{
			template.Create( componentName, fd.SelectedFile );
		}

		await Task.Delay( 500 );

		CodeEditor.OpenFile( fd.SelectedFile );

		await EditorUtility.Projects.WaitForCompiles();

		var componentType = FindComponentType( componentName, fd.SelectedFile );
		if ( componentType is null )
		{
			Log.Warning( $"Couldn't find target component type {componentName}" );

			foreach ( var t in EditorTypeLibrary.GetTypes<Component>() )
			{
				Log.Info( $"{t}" );
			}
		}
		else
		{
			OnComponentSelect?.Invoke( componentType );
		}

		OnFinished?.Invoke();
	}

	private static TypeDescription FindComponentType( string name, string filePath )
	{
		if ( EditorTypeLibrary.GetType<Component>( name ) is { } match )
		{
			return match;
		}

		var assetsPath = Project.Current.GetAssetsPath().Replace( "\\", "/" );

		filePath = filePath.Replace( "\\", "/" );

		if ( filePath.StartsWith( $"{assetsPath}/" ) )
		{
			var assetPath = filePath.Substring( assetsPath.Length + 1 );

			return EditorTypeLibrary.GetTypes<Component>()
				.FirstOrDefault( x => string.Equals( assetPath, x.SourceFile, StringComparison.OrdinalIgnoreCase ) );
		}

		return null;
	}
}

file class ComponentFilterControlWidget : Widget
{
	ComponentTypeSelectorWidget Target;

	ContextMenu menu;

	public ComponentFilterControlWidget( ComponentTypeSelectorWidget targetObject )
	{
		Target = targetObject;
		Cursor = CursorShape.Finger;
		MinimumWidth = Theme.RowHeight;
		HorizontalSizeMode = SizeMode.CanShrink;
		ToolTip = "Filter Settings";
	}

	protected override Vector2 SizeHint()
	{
		return new( Theme.RowHeight, Theme.RowHeight );
	}

	protected override Vector2 MinimumSizeHint()
	{
		return new( Theme.RowHeight, Theme.RowHeight );
	}

	protected override void OnDoubleClick( MouseEvent e ) { }

	protected override void OnMousePress( MouseEvent e )
	{
		if ( ReadOnly ) return;
		OpenSettings();
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		var rect = LocalRect.Shrink( 2 );
		var icon = "sort";

		if ( menu?.IsValid ?? false )
		{
			Paint.SetPen( Theme.Blue, 1 );
			Paint.SetBrush( Theme.Blue );
			Paint.DrawRect( rect, 2 );

			Paint.SetPen( Theme.Text );
			Paint.DrawIcon( rect, icon, 13 );
		}
		else
		{
			Paint.SetPen( Theme.Blue );
			Paint.DrawIcon( rect, icon, 13 );
		}

		if ( IsUnderMouse )
		{
			Paint.SetPen( Theme.Blue.Lighten( 0.1f ), 1 );
			Paint.ClearBrush();
			Paint.DrawRect( rect, 1 );
		}
	}

	void OpenSettings()
	{
		if ( Target is null ) return;

		menu = new ContextMenu( this );

		{
			var widget = new Widget( menu );
			widget.OnPaintOverride = () =>
			{
				Paint.SetBrushAndPen( Theme.WidgetBackground.WithAlpha( 0.5f ) );
				Paint.DrawRect( widget.LocalRect.Shrink( 2 ), 2 );
				return true;
			};
			var cs = new ControlSheet();

			cs.AddRow( Target.GetSerialized().GetProperty( nameof( ComponentTypeSelectorWidget.FlatView ) ) );
			cs.AddRow( Target.GetSerialized().GetProperty( nameof( ComponentTypeSelectorWidget.HideBaseComponents ) ) );

			widget.Layout = cs;

			widget.MaximumWidth = 400;

			menu.AddWidget( widget );
		}

		menu.OpenAtCursor();
		menu.ConstrainToScreen();
	}
}
