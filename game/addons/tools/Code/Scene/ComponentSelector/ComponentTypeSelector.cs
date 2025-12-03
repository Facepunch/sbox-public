using System.IO;

namespace Editor;

/// <summary>
/// A popup dialog to select an entity type
/// </summary>
internal class ComponentTypeSelector : PopupWidget
{
	public Action<TypeDescription> OnSelect
	{
		get => Widget.OnSelect;
		set => Widget.OnSelect = value;
	}

	ComponentTypeSelectorWidget Widget { get; set; }

	public ComponentTypeSelector( Widget parent ) : base( parent )
	{
		Widget = new ComponentTypeSelectorWidget( this )
		{
			OnDestroy = Destroy
		};

		Layout = Layout.Column();
		Layout.Add( Widget );

		DeleteOnClose = true;
	}
}

internal partial class ComponentTypeSelectorWidget : Widget
{
	public Action<TypeDescription> OnSelect { get; set; }
	public Action OnDestroy { get; set; }
	List<ComponentSelection> Panels { get; set; } = new();
	int CurrentPanelId { get; set; } = 0;
	Widget Main { get; set; }

	string CategorySeparator => "/"; // This is used for sub-categories

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
			ResetSelection();
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
			ResetSelection();
		}
	}
	bool _flatView = EditorCookie.Get<bool>( "ComponentSelector.FlatView", false );

	string searchString;
	const string NoCategoryName = "Uncategorized";

	internal LineEdit Search { get; init; }

	public ComponentTypeSelectorWidget( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();

		var head = Layout.Row();
		head.Margin = 6;

		Layout.Add( head );

		Main = new Widget( this );
		Main.Layout = Layout.Row();
		Main.Layout.Enabled = false;
		Main.FixedSize = new( 300, 400 );
		Layout.Add( Main, 1 );

		DeleteOnClose = true;

		Search = new LineEdit( this );
		Search.Layout = Layout.Row();
		Search.Layout.AddStretchCell( 1 );
		Search.MinimumHeight = 22;
		Search.PlaceholderText = "Search Components";
		Search.TextEdited += ( t ) =>
		{
			searchString = t;
			ResetSelection();
		};

		var clearButton = Search.Layout.Add( new ToolButton( string.Empty, "clear", this ) );
		clearButton.MouseLeftPress = () =>
		{
			Search.Text = searchString = string.Empty;
			ResetSelection();
		};

		head.Add( Search );

		var filterButton = new ComponentFilterControlWidget( this );
		head.Add( filterButton );

		ResetSelection();

		Search.Focus();
	}

	/// <summary>
	/// Pushes a new selection to the selector
	/// </summary>
	/// <param name="selection"></param>
	void PushSelection( ComponentSelection selection )
	{
		CurrentPanelId++;

		// Do we have something at our new index, if so, kill it
		if ( Panels.Count > CurrentPanelId && Panels.ElementAt( CurrentPanelId ) is var existingObj ) existingObj.Destroy();

		Panels.Insert( CurrentPanelId, selection );
		Main.Layout.Add( selection, 1 );

		if ( !selection.IsManual )
		{
			UpdateSelection( selection );
		}

		AnimateSelection( true, Panels[CurrentPanelId - 1], selection );

		selection.Focus();
	}

	/// <summary>
	/// Pops the current selection off
	/// </summary>
	internal void PopSelection()
	{
		// Don't pop while empty
		if ( CurrentPanelId == 0 ) return;

		var currentIdx = Panels[CurrentPanelId];
		CurrentPanelId--;

		AnimateSelection( false, currentIdx, Panels[CurrentPanelId] );

		Panels[CurrentPanelId].Focus();
	}

	/// <summary>
	/// Runs an animation on the last selection, and the current selection.
	/// I kinda hate this. A lot. But it's pretty.
	/// </summary>
	/// <param name="forward"></param>
	/// <param name="prev"></param>
	/// <param name="selection"></param>
	void AnimateSelection( bool forward, ComponentSelection prev, ComponentSelection selection )
	{
		const string easing = "ease-out";
		const float speed = 0.2f;

		var distance = Width;

		var prevFrom = prev.Position.x;
		var prevTo = forward ? prev.Position.x - distance : prev.Position.x + distance;

		var selectionFrom = forward ? selection.Position.x + distance : selection.Position.x;
		var selectionTo = forward ? selection.Position.x : selection.Position.x + distance;

		var func = ( ComponentSelection a, float x ) =>
		{
			a.Position = a.Position.WithX( x );
			OnMoved();
		};

		Animate.Add( prev, speed, prevFrom, prevTo, x => func( prev, x ), easing );
		Animate.Add( selection, speed, selectionFrom, selectionTo, x => func( selection, x ), easing );
	}

	/// <summary>
	/// Resets the current selection, useful when setting up / searching
	/// </summary>
	protected void ResetSelection()
	{
		Main.Layout.Clear( true );
		Panels.Clear();

		var selection = new ComponentSelection( Main, this );

		CurrentPanelId = 0;

		UpdateSelection( selection );

		Panels.Add( selection );
		Main.Layout.Add( selection );
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.SetBrushAndPen( Theme.WidgetBackground );
		Paint.DrawRect( LocalRect );
	}

	/// <summary>
	/// Called when a category is selected
	/// </summary>
	/// <param name="category"></param>
	void OnCategorySelected( string category )
	{
		// Push this as a new selection
		PushSelection( new ComponentSelection( Main, this, category ) );
	}

	/// <summary>
	/// Called when an individual component is selected
	/// </summary>
	/// <param name="type"></param>
	void OnComponentSelected( TypeDescription type )
	{
		OnSelect( type );
		OnDestroy?.Invoke();
	}

	private bool lineEditFocused = false;

	/// <summary>
	/// Called when the New Component button is pressed
	/// </summary>
	void OnNewComponentSelected( string componentName = "MyComponent" )
	{
		var templateTypes = ComponentTemplate.GetAllTypes();
		var selection = new ComponentSelection( Main, this, "Create a new component" ) { IsManual = true };

		selection.AddEntry( new Label( "Name", this ) ).ContentMargins = new( 8, 8, 8, 8 );

		var lineEdit = new LineEdit( this );
		lineEdit.Text = componentName;
		lineEdit.ContentMargins = new( 8, 0, 8, 0 );
		lineEdit.MinimumHeight = 22;

		lineEdit.EditingStarted += () => lineEditFocused = true;
		lineEdit.EditingFinished += () => lineEditFocused = false;

		selection.AddEntry( lineEdit ).ContentMargins = 0;
		selection.AddEntry( new Label( "Create Script from Template", this ) ).ContentMargins = 8;

		foreach ( var componentTemplate in templateTypes )
		{
			selection.AddEntry( new ComponentEntry( selection )
			{
				Icon = componentTemplate.Icon,
				Text = $"New {componentTemplate.Title}..",
				MouseClick = () => _ = CreateNewComponent( componentTemplate, lineEdit.Text )
			}
			);
		}

		selection.AddStretchCell();

		PushSelection( selection );

		// Focus the TextEdit
		lineEdit.Focus();
	}

	/// <summary>
	/// We're creating a new component..
	/// </summary>
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

		// User might change their mind on the component name
		componentName = System.IO.Path.GetFileNameWithoutExtension( fd.SelectedFile );

		// Anything with a space will be an invalid class name, convert to title case and remove whitespace at least.
		componentName = componentName.ToTitleCase().Replace( " ", "" );

		if ( !System.IO.File.Exists( fd.SelectedFile ) )
		{
			template.Create( componentName, fd.SelectedFile );
		}

		// give it half a second, should do it
		await Task.Delay( 500 );

		// open it in the code editor
		CodeEditor.OpenFile( fd.SelectedFile );

		// we just wrote a file, lets wait until its compiled and loaded
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
			OnSelect( componentType );
		}

		OnDestroy?.Invoke();
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

	protected override void OnKeyRelease( KeyEvent e )
	{
		if ( e.Key == KeyCode.Down )
		{
			var selection = Panels[CurrentPanelId];
			if ( selection.ItemList.FirstOrDefault().IsValid() )
			{
				selection.Focus();
				selection.PostKeyEvent( KeyCode.Down );
				e.Accepted = true;
			}
		}
	}

	int SearchScore( TypeDescription type, string[] parts )
	{
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

	/// <summary>
	/// Updates any selection
	/// </summary>
	/// <param name="selection"></param>
	void UpdateSelection( ComponentSelection selection )
	{
		selection.Clear();

		selection.ItemList.Add( selection.CategoryHeader );

		// entity components
		var types = EditorTypeLibrary.GetTypes<Component>().Where( x => !x.IsAbstract && !x.HasAttribute<HideAttribute>() && !x.HasAttribute<ObsoleteAttribute>() );

		if ( HideBaseComponents )
		{
			types = types.Where( x => !x.FullName.StartsWith( "Sandbox." ) );
		}

		if ( !string.IsNullOrWhiteSpace( searchString ) )
		{
			var searchWords = searchString.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
			var query = types.Select( x => new { x, score = SearchScore( x, searchWords ) } )
								.ToArray()
								.Where( x => x.score > 0 );

			foreach ( var type in query.OrderByDescending( x => x.score ).Select( x => x.x ) )
			{
				selection.AddEntry( new ComponentEntry( selection, type ) { MouseClick = () => OnComponentSelected( type ) } );
			}

			selection.AddEntry( new ComponentEntry( selection ) { Text = $"New Component '{searchString}'", MouseClick = () => OnNewComponentSelected( searchString ) } );
			selection.AddStretchCell();
			return;
		}

		var categories = types.Select( x => string.IsNullOrWhiteSpace( x.Group ) ? NoCategoryName : x.Group ).Distinct().OrderBy( x => x ).ToArray();
		if ( selection.Category == null )
		{
			if ( FlatView )
			{
				foreach ( var type in types.OrderBy( x => x.Title ) )
				{
					selection.AddEntry( new ComponentEntry( selection, type ) { MouseClick = () => OnComponentSelected( type ) } );
				}
			}
			else if ( categories.Length > 1 )
			{
				selection.AddEntry( new ComponentEntry( selection ) { Text = "New Component", MouseClick = () => OnNewComponentSelected() } );

				foreach ( var category in categories )
				{
					var categoryName = category.Split( CategorySeparator ).FirstOrDefault() ?? NoCategoryName;
					if ( selection.ItemList.Any( x => x is ComponentCategory c && c.Category == categoryName ) )
					{
						continue; // Skip if we already have this category in the list
					}
					selection.AddEntry( new ComponentCategory( selection )
					{
						Category = categoryName,
						MouseClick = () => OnCategorySelected( category ),
					} );
				}
			}
		}
		else
		{
			types = types.Where( x => (selection.Category == NoCategoryName)
									? (x.Group == null)
									: (x.Group == selection.Category) )
									.OrderBy( x => x.Title );

			var currentDepth = selection.Category.Split( CategorySeparator ).Length;
			foreach ( var category in categories )
			{
				if ( !category.StartsWith( selection.Category ) || category == selection.Category ) continue;
				var categoryName = category.Split( CategorySeparator ).ElementAtOrDefault( currentDepth ) ?? NoCategoryName;
				if ( selection.ItemList.Any( x => x is ComponentCategory c && c.Category == categoryName ) )
				{
					continue; // Skip if we already have this category in the list
				}
				selection.AddEntry( new ComponentCategory( selection )
				{
					Category = categoryName,
					MouseClick = () => OnCategorySelected( selection.Category + CategorySeparator + categoryName ),
				} );
			}

			foreach ( var type in types )
			{
				selection.AddEntry( new ComponentEntry( selection, type ) { MouseClick = () => OnComponentSelected( type ) } );
			}
		}

		selection.AddStretchCell();
	}

	/// <summary>
	/// A widget that contains a given selection - we hold this in a class because more than one can exist.
	/// </summary>
	partial class ComponentSelection : Widget
	{
		internal string Category { get; init; }
		internal Widget CategoryHeader { get; init; }
		ScrollArea Scroller { get; init; }
		ComponentTypeSelectorWidget Selector { get; set; }

		internal List<Widget> ItemList { get; private set; } = new();
		internal int CurrentItemId { get; private set; } = 0;
		internal Widget CurrentItem { get; private set; }

		internal bool IsManual { get; set; }

		internal ComponentSelection( Widget parent, ComponentTypeSelectorWidget selector, string categoryName = null ) : base( parent )
		{
			Selector = selector;
			Category = categoryName;
			FixedSize = parent.ContentRect.Size;

			Layout = Layout.Column();

			CategoryHeader = new Widget( this );
			CategoryHeader.FixedHeight = Theme.RowHeight;
			CategoryHeader.OnPaintOverride = PaintHeader;
			CategoryHeader.MouseClick = Selector.PopSelection;
			Layout.Add( CategoryHeader );

			Scroller = new ScrollArea( this );
			Scroller.Layout = Layout.Column();
			Scroller.FocusMode = FocusMode.None;
			Layout.Add( Scroller, 1 );

			Scroller.Canvas = new Widget( Scroller );
			Scroller.Canvas.Layout = Layout.Column();
			Scroller.Canvas.OnPaintOverride = () =>
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.WidgetBackground );
				Paint.DrawRect( Scroller.Canvas.LocalRect );
				return true;
			};
		}

		protected bool SelectMoveRow( int delta )
		{
			var selection = Selector.Panels[Selector.CurrentPanelId];
			if ( delta == 1 && selection.ItemList.Count - 1 > selection.CurrentItemId )
			{
				selection.CurrentItem = selection.ItemList[++selection.CurrentItemId];
				selection.Update();

				if ( selection.CurrentItem.IsValid() )
				{
					Scroller.MakeVisible( selection.CurrentItem );
				}

				return true;
			}
			else if ( delta == -1 )
			{
				if ( selection.CurrentItemId > 0 )
				{
					selection.CurrentItem = selection.ItemList[--selection.CurrentItemId];
					selection.Update();

					if ( selection.CurrentItem.IsValid() )
					{
						Scroller.MakeVisible( selection.CurrentItem );
					}

					return true;
				}
				else
				{
					selection.Selector.Search.Focus();
					selection.CurrentItem = null;
					selection.Update();
					return true;
				}
			}

			return false;
		}

		protected bool Enter()
		{
			var selection = Selector.Panels[Selector.CurrentPanelId];
			if ( selection.ItemList[selection.CurrentItemId] is Widget entry )
			{
				entry.MouseClick?.Invoke();
				return true;
			}

			return false;
		}

		protected override void OnKeyRelease( KeyEvent e )
		{
			// Move down
			if ( e.Key == KeyCode.Down )
			{
				e.Accepted = true;
				SelectMoveRow( 1 );
				return;
			}

			// Move up 
			if ( e.Key == KeyCode.Up )
			{
				e.Accepted = true;
				SelectMoveRow( -1 );
				return;
			}

			// Back button while in any selection, goes to previous selction.
			if ( e.Key == KeyCode.Left && !Selector.lineEditFocused )
			{
				e.Accepted = true;
				Selector.PopSelection();
				return;
			}

			// Moving right, or hitting the enter key assumes you're trying to select something
			if ( (e.Key == KeyCode.Return || e.Key == KeyCode.Right) && Enter() )
			{
				e.Accepted = true;
				return;
			}
		}

		internal bool PaintHeader()
		{
			var c = CategoryHeader;
			var selected = c.IsUnderMouse || CurrentItem == c;

			Paint.ClearPen();
			Paint.SetBrush( selected ? Theme.ControlBackground : Theme.WidgetBackground.WithAlpha( selected ? 0.7f : 0.4f ) );
			Paint.DrawRect( c.LocalRect );

			var r = c.LocalRect.Shrink( 12, 2 );
			Paint.SetPen( Theme.TextControl );

			if ( Selector.CurrentPanelId > 0 )
			{
				Paint.DrawIcon( r, "arrow_back", 14, TextFlag.LeftCenter );
			}

			var category = string.IsNullOrEmpty( Category ) ? "Component" : Category.Split( Selector.CategorySeparator ).LastOrDefault();
			Paint.SetDefaultFont( 8, 600 );
			Paint.DrawText( r, category, TextFlag.Center );

			return true;
		}

		/// <summary>
		/// Adds a new entry to the current selection.
		/// </summary>
		/// <param name="entry"></param>
		internal Widget AddEntry( Widget entry )
		{
			var layoutWidget = Scroller.Canvas.Layout.Add( entry );
			ItemList.Add( entry );

			if ( entry is ComponentEntry e ) e.Selector = this;

			return layoutWidget;
		}

		/// <summary>
		/// Adds a stretch cell to the bottom of the selection - good to call this when you know you're done adding entries.
		/// </summary>
		internal void AddStretchCell()
		{
			Scroller.Canvas.Layout.AddStretchCell( 1 );
			Update();
		}

		/// <summary>
		/// Adds a separator cell.
		/// </summary>
		internal void AddSeparator()
		{
			Scroller.Canvas.Layout.AddSeparator( true );
			Update();
		}

		/// <summary>
		/// Clears the current selection
		/// </summary>
		internal void Clear()
		{
			Scroller.Canvas.Layout.Clear( true );
			ItemList.Clear();
		}

		protected override void OnPaint()
		{
			Paint.Antialiasing = true;
			Paint.SetBrushAndPen( Theme.ControlBackground );
			Paint.DrawRect( LocalRect.Shrink( 0 ), 3 );
		}
	}

	/// <summary>
	/// A component entry
	/// </summary>
	class ComponentEntry : Widget
	{
		public string Text { get; set; } = "My Component";
		public string Icon { get; set; } = "note_add";
		public bool IsSelected { get; set; } = false;

		internal ComponentSelection Selector { get; set; }

		public TypeDescription Type { get; init; }

		internal ComponentEntry( Widget parent, TypeDescription type = null ) : base( parent )
		{
			FixedHeight = 24;
			Type = type;

			if ( type is not null )
			{
				Text = type.Title;
				Icon = type.Icon;
				ToolTip = $"<b>{type.FullName}</b><br/>{type.Description}";
			}
		}

		protected override void OnPaint()
		{
			var r = LocalRect.Shrink( 12, 2 );
			var selected = IsUnderMouse || Selector.CurrentItem == this;
			var opacity = selected ? 1.0f : 0.7f;

			Paint.ClearPen();
			Paint.SetBrush( Theme.WidgetBackground );

			if ( selected )
			{
				Paint.SetBrush( Theme.ControlBackground );
			}

			Paint.DrawRect( LocalRect );

			if ( Type is not null && !string.IsNullOrEmpty( Type.Icon ) )
			{
				Type.PaintComponentIcon( new Rect( r.Position, r.Height ).Shrink( 2 ), opacity );
			}
			else
			{
				Paint.SetPen( Theme.Green.WithAlpha( opacity ) );

				var icon = !string.IsNullOrEmpty( Icon ) ? Icon : "note_add";
				Paint.DrawIcon( new Rect( r.Position, r.Height ).Shrink( 2 ), icon, r.Height, TextFlag.Center );
			}

			r.Left += r.Height + 6;

			Paint.SetDefaultFont( 8 );
			Paint.SetPen( Theme.TextControl.WithAlpha( selected ? 1.0f : 0.5f ) );
			Paint.DrawText( r, Text, TextFlag.LeftCenter );
		}
	}

	/// <summary>
	/// A category component entry
	/// </summary>
	class ComponentCategory : ComponentEntry
	{
		public string Category { get; set; }
		public ComponentCategory( Widget parent ) : base( parent ) { }

		protected override void OnPaint()
		{
			var selected = IsUnderMouse || Selector.CurrentItem == this;

			Paint.ClearPen();
			Paint.SetBrush( Theme.WidgetBackground );

			if ( selected )
			{
				Paint.SetBrush( Theme.ControlBackground );
			}

			Paint.DrawRect( LocalRect );

			var r = LocalRect.Shrink( 12, 2 );

			Paint.SetPen( Theme.TextControl.WithAlpha( selected ? 1.0f : 0.5f ) );

			Paint.SetDefaultFont( 8 );
			Paint.DrawText( r, Category, TextFlag.LeftCenter );
			Paint.DrawIcon( r, "arrow_forward", 14, TextFlag.RightCenter );
		}
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
