using Editor.ProjectSettingPages;

namespace Editor;

internal sealed class ProjectSettingsWindow : Window
{
	Layout HeaderLayout;
	TreeView TreeView;
	ScrollArea Scroller;
	Layout FooterLayout;
	Button SaveButton;

	Dictionary<TreeNode, List<Type>> NodeToCategories;
	Dictionary<string, TreeNode> SectionNodes;
	Project CurrentProject;
	TreeNode CurrentNode;

	/// <summary>
	/// Called when we save settings
	/// </summary>
	public Action<Project> OnSave { get; set; }

	/// <summary>
	/// Called when a property changes within a <see cref="Category"/> in the inspector
	/// </summary>
	public Action<SerializedProperty> OnPropertyChanged { get; set; }

	private bool _hasUnsavedChanges;
	private PopupDialogWidget _popup;

	/// <summary>
	/// Does this inspector have any unsaved changes?
	/// </summary>
	bool HasUnsavedChanges
	{
		get
		{
			return _hasUnsavedChanges;
		}

		set
		{
			_hasUnsavedChanges = value;
			SaveButton.Enabled = _hasUnsavedChanges;
		}
	}

	public ProjectSettingsWindow( Project project )
	{
		if ( project is null )
			return;

		// Makes the window always on top of the editor only, not other applications
		Parent = EditorWindow;
		WindowFlags = WindowFlags.Dialog | WindowFlags.Customized | WindowFlags.CloseButton | WindowFlags.WindowSystemMenuHint | WindowFlags.WindowTitle;

		CurrentProject = project;
		NodeToCategories = new();
		SectionNodes = new();
		StatusBar.Visible = false;

		DeleteOnClose = true;
		WindowTitle = $"Project Settings";
		Size = new Vector2( 1024, 768 );
		MinimumSize = new Vector2( 1024, 768 );

		Canvas = new Widget( this );
		Canvas.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.WidgetBackground );
			Paint.DrawRect( Canvas.LocalRect );
			return true;
		};

		SetWindowIcon( "settings" );

		Canvas.Layout = Layout.Column();

		var header = Canvas.Layout.Add( new ProjectSettingsHeader( this, CurrentProject ) );

		Canvas.Layout.AddSeparator();

		HeaderLayout = Layout.Column();

		Canvas.Layout.Add( HeaderLayout );

		// Create split layout: TreeView on left, properties on right
		var splitLayout = Canvas.Layout.AddRow( 1 );

		// Left side: TreeView
		var treeViewContainer = new Widget( this );
		treeViewContainer.MinimumWidth = 200;
		treeViewContainer.MaximumWidth = 300;
		treeViewContainer.Layout = Layout.Column();
		treeViewContainer.Layout.Margin = 0;

		TreeView = new TreeView( treeViewContainer );
		TreeView.Margin = TreeView.Margin with { Top = 0 };
		TreeView.ItemActivated += OnNodeSelected;
		TreeView.ItemSelected += OnNodeSelected;
		TreeView.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( TreeView.LocalRect, Theme.ControlRadius );

			return false;
		};

		treeViewContainer.Layout.Add( TreeView, 1 );
		splitLayout.Add( treeViewContainer );

		// Right side: Scroller for properties
		Scroller = new ScrollArea( this );
		Scroller.Layout = Layout.Column();
		Scroller.FocusMode = FocusMode.None;

		splitLayout.Add( Scroller, 1 );

		FooterLayout = Layout.Row();
		FooterLayout.Spacing = 8;
		FooterLayout.Margin = new( 8, 8 );

		Canvas.Layout.AddSeparator();

		AddFooterDefaults();

		Canvas.Layout.Add( FooterLayout );

		Scroller.Canvas = new Widget( Scroller );
		Scroller.Canvas.Layout = Layout.Column();
		Scroller.Canvas.Layout.Spacing = 0;
		Scroller.Canvas.Layout.Margin = new( 16, 16 );

		BuildTreeView();

		OnPropertyChanged += _ => HasUnsavedChanges = true;

		// Select the first node by default
		if ( NodeToCategories.Keys.FirstOrDefault() is TreeNode firstNode )
		{
			TreeView.SelectItem( firstNode );
			SelectNode( firstNode );
		}

		Show();
	}

	private void BuildTreeView()
	{
		var project = CurrentProject;

		// Collect all categories by section first
		var categoriesBySection = new Dictionary<string, List<(Type type, string icon, string title)>>();

		void AddCategoryToList( Type categoryType, string sectionName )
		{
			if ( !categoriesBySection.ContainsKey( sectionName ) )
				categoriesBySection[sectionName] = new();

			var typeInfo = EditorTypeLibrary.GetType( categoryType );
			var icon = typeInfo?.Icon ?? "settings";
			var title = typeInfo?.Title ?? categoryType.Name;
			categoriesBySection[sectionName].Add( (categoryType, icon, title) );
		}

		if ( project.Config.Type == "game" )
		{
			AddCategoryToList( typeof( ProjectPage ), "Project" );
			AddCategoryToList( typeof( GameCategory ), "Project" );
			AddCategoryToList( typeof( StandaloneCategory ), "Project" );
			AddCategoryToList( typeof( SystemsPage ), "Systems" );

			AddCategoryToList( typeof( PhysicsCategory ), "Physics" );

			AddCategoryToList( typeof( InputCategory ), "Input" );

			AddCategoryToList( typeof( MultiplayerCategory ), "Networking" );

			AddCategoryToList( typeof( CompilerCategory ), "Compiler" );

			AddCategoryToList( typeof( ResourcesCategory ), "Other" );
			AddCategoryToList( typeof( ReferencesCategory ), "Other" );
			AddCategoryToList( typeof( CursorCategory ), "Other" );
		}

		if ( project.Config.Type == "map" )
		{
			AddCategoryToList( typeof( ReferencesCategory ), "Project" );
		}

		if ( project.Config.Type == "library" )
		{
			AddCategoryToList( typeof( CompilerCategory ), "Compiler" );
		}

		if ( project.Config.Type == "tool" )
		{
			AddCategoryToList( typeof( CompilerCategory ), "Compiler" );
		}

		// Build the tree based on category counts
		foreach ( var (sectionName, categories) in categoriesBySection )
		{
			var sectionIcon = categories.FirstOrDefault().icon;
			var sectionNode = new CategorySectionNode( sectionName, sectionIcon );
			SectionNodes[sectionName] = sectionNode;
			TreeView.AddItem( sectionNode );

			if ( categories.Count == 1 )
			{
				var (type, icon, title) = categories[0];
				var children = Category.GetTreeChildren( type );

				if ( children == null || !children.Any() )
				{
					// Single category with no children - map directly to section node
					NodeToCategories[sectionNode] = new List<Type> { type };
				}
				else
				{
					// Single category with children - create parent + children
					NodeToCategories[sectionNode] = new List<Type> { type };

					foreach ( var child in children )
					{
						var childNode = new CategoryNode( child.Title, child.Icon ) { ConfigureAction = child.Configure };
						NodeToCategories[childNode] = new List<Type> { type };
						sectionNode.AddItem( childNode );
					}
				}
			}
			else
			{
				// Multiple categories - create child nodes and map section to all categories
				NodeToCategories[sectionNode] = categories.Select( x => x.type ).ToList();

				foreach ( var (categoryType, icon, title) in categories )
				{
					var children = Category.GetTreeChildren( categoryType );

					if ( children != null && children.Any() )
					{
						// Category with custom child nodes
						var parentNode = new CategoryNode( title, icon );
						NodeToCategories[parentNode] = new List<Type> { categoryType };
						sectionNode.AddItem( parentNode );

						foreach ( var child in children )
						{
							var childNode = new CategoryNode( child.Title, child.Icon ) { ConfigureAction = child.Configure };
							NodeToCategories[childNode] = new List<Type> { categoryType };
							parentNode.AddItem( childNode );
						}
					}
					else
					{
						// Regular category node
						var categoryNode = new CategoryNode( title, icon );
						NodeToCategories[categoryNode] = new List<Type> { categoryType };
						sectionNode.AddItem( categoryNode );
					}
				}
			}
		}

		// Expand all sections by default
		foreach ( var section in SectionNodes.Values )
		{
			TreeView.Open( section );
		}
	}

	void Reset()
	{
		HasUnsavedChanges = false;

		if ( CurrentNode != null )
		{
			SelectNode( CurrentNode );
		}
	}

	[Shortcut( "editor.save", "CTRL+S" )]
	internal void Save()
	{
		foreach ( var child in Scroller.Canvas.Children.OfType<Category>() )
		{
			child.OnSave();
		}

		HasUnsavedChanges = false;
	}

	void AddFooterDefaults()
	{
		FooterLayout.Clear( true );

		var revert = new Button( "Revert Changes", "history", null );
		revert.Clicked = Reset;

		var save = new Button.Primary( "Save", "save", null );
		save.Clicked = Save;
		save.Enabled = false;

		SaveButton = save;

		FooterLayout.AddStretchCell();
		FooterLayout.Add( revert );
		FooterLayout.Add( save );
	}

	void OnNodeSelected( object item )
	{
		if ( item is TreeNode node )
		{
			SelectNode( node );
		}
	}

	void SelectNode( TreeNode node )
	{
		using var su = SuspendUpdates.For( Scroller.Canvas );

		CurrentNode = node;
		Scroller.Canvas.Layout.Clear( true );

		// Get all categories for this node
		if ( !NodeToCategories.TryGetValue( node, out var categoryTypes ) )
			return;

		if ( categoryTypes.Count() > 1 )
		{
			var title = new Label.Subtitle( node.Name, null ) { Color = Color.White };
			Scroller.Canvas.Layout.Add( title );
		}

		// Display all categories sequentially
		foreach ( var categoryType in categoryTypes )
		{
			var type = EditorTypeLibrary.GetType( categoryType );
			var section = type.Create<Category>();

			if ( node is CategoryNode categoryNode )
			{
				categoryNode.ConfigureAction?.Invoke( section );
			}

			section.InitFromProject( CurrentProject, OnSave, OnPropertyChanged );

			if ( section.ShowTitle )
			{
				var header = new Label.Header( type.Title, null );
				Scroller.Canvas.Layout.Add( header );
			}

			Scroller.Canvas.Layout.Add( section );
		}

		Scroller.Canvas.Layout.AddStretchCell();
	}

	public static async void OpenForProject( Project project )
	{
		// Try to load the project first
		await Package.FetchAsync( project.Config.FullIdent, false );

		var window = new ProjectSettingsWindow( project );
	}

	protected override bool OnClose()
	{
		// We have no pending changes, let's just close
		if ( !HasUnsavedChanges )
			return true;

		if ( _popup.IsValid() )
		{
			// If this hits, it means we're already showing a popup, don't create another
			return false;
		}

		_popup = new PopupDialogWidget( "⚠️" );
		_popup.FixedWidth = 462;
		_popup.WindowTitle = $"Project Settings";
		_popup.MessageLabel.Text = $"You have some unsaved settings, do you want to save them?";

		_popup.ButtonLayout.Spacing = 4;
		_popup.ButtonLayout.AddStretchCell();
		_popup.ButtonLayout.Add( new Button.Primary( "Save" )
		{
			Clicked = () =>
			{
				Save();
				_popup.Destroy();
				_popup = null;
				Close();
			}
		} );

		_popup.ButtonLayout.Add( new Button( "Don't Save" )
		{
			Clicked = () =>
			{
				_hasUnsavedChanges = false;
				_popup.Destroy();
				_popup = null;
				Close();
			}
		} );

		_popup.ButtonLayout.Add( new Button( "Cancel" )
		{
			Clicked = () =>
			{
				_popup.Destroy();
				_popup = null;
			}
		} );

		_popup.SetModal( true, true );
		_popup.Show();

		return false;
	}

	/// <summary>
	/// TreeNode for section headers (e.g., "Project", "Physics")
	/// </summary>
	class CategorySectionNode : TreeNode
	{
		string _icon;

		public CategorySectionNode( string title, string icon )
		{
			Name = title;
			_icon = icon;
			Height = 28;
		}

		public override void OnPaint( VirtualWidget item )
		{
			PaintSelection( item );

			var rect = item.Rect;

			if ( !string.IsNullOrWhiteSpace( _icon ) )
			{
				Paint.SetPen( Theme.Text.WithAlpha( 0.7f ) );
				var iconRect = Paint.DrawIcon( rect, _icon, 18, TextFlag.LeftCenter );
				rect.Left = iconRect.Right + 8;
			}

			Paint.SetPen( Theme.Text );
			Paint.SetHeadingFont( 10, 600 );
			Paint.DrawText( rect, Name, TextFlag.LeftCenter );
		}
	}

	/// <summary>
	/// TreeNode for individual categories (e.g., "Game", "Standalone")
	/// </summary>
	class CategoryNode : TreeNode
	{
		string _icon;
		public Action<Category> ConfigureAction { get; set; }

		public CategoryNode( string title, string icon )
		{
			Name = title;
			_icon = icon;
			Height = 24;
		}

		public override void OnPaint( VirtualWidget item )
		{
			PaintSelection( item );

			var rect = item.Rect;

			if ( !string.IsNullOrWhiteSpace( _icon ) )
			{
				Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
				var iconRect = Paint.DrawIcon( rect, _icon, 16, TextFlag.LeftCenter );
				rect.Left = iconRect.Right + 6;
			}

			Paint.SetPen( Theme.Text );
			Paint.SetDefaultFont();
			Paint.DrawText( rect, Name, TextFlag.LeftCenter );
		}
	}

	internal partial class Category : Widget
	{
		public Project Project { get; private set; }

		protected Action<Project> SaveCallback;
		protected Action<SerializedProperty> PropertyChangedCallback;

		internal virtual bool ShowTitle => true;

		public Layout BodyLayout;

		public Category() : base( null )
		{
			Layout = Layout.Column();
			BodyLayout = Layout.AddColumn();
			BodyLayout.Spacing = 4;
		}

		/// <summary>
		/// Override this to provide custom child nodes in the tree view.
		/// </summary>
		public virtual IEnumerable<TreeChildNode> GetTreeChildren() => null;

		/// <summary>
		/// Get tree children from a category type
		/// </summary>
		internal static IEnumerable<TreeChildNode> GetTreeChildren( Type categoryType )
		{
			var instance = EditorTypeLibrary.GetType( categoryType )?.Create<Category>();
			return instance?.GetTreeChildren();
		}

		/// <summary>
		/// Listens for changes to a <see cref="SerializedObject"/> and lets the inspector know we changed a value
		/// </summary>
		/// <param name="so"></param>
		protected void ListenForChanges( SerializedObject so )
		{
			so.OnPropertyChanged = StateHasChanged;
		}

		/// <summary>
		/// This tells the project settings inspector that a property has changed, so we know to notify the user.
		/// </summary>
		/// <param name="prop"></param>
		protected void StateHasChanged( SerializedProperty prop = null )
		{
			PropertyChangedCallback?.Invoke( prop );
		}

		public void InitFromProject( Project project, Action<Project> saveCallback, Action<SerializedProperty> propertyCallback )
		{
			Project = project;
			SaveCallback = saveCallback;
			PropertyChangedCallback = propertyCallback;

			OnInit( project );
		}

		public virtual void OnInit( Project project )
		{
		}

		public Layout StartSection( string name, Layout layout = null )
		{
			layout ??= BodyLayout;

			var block = layout.AddColumn();
			block.Add( new Label.Header( name ) );

			return block;
		}

		public virtual void OnSave()
		{
			EditorUtility.Projects.Updated( Project );
			SaveCallback?.Invoke( Project );
		}

		/// <summary>
		/// Defines a child node for the tree view
		/// </summary>
		public record TreeChildNode( string Title, string Icon = "settings", Action<Category> Configure = null );
	}
}
