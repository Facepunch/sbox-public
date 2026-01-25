namespace Editor.ShaderGraph;

public partial class PaletteWidget : Widget
{
	private readonly LineEdit _searchFilter;
	private readonly TreeView _treeView;
	private readonly Button _searchFilterClear;

	private bool IsSubgraph { get; }

	public PaletteWidget( Widget parent, bool isSubgraph ) : base( parent )
	{
		IsSubgraph = isSubgraph;
		Name = "Palette";
		WindowTitle = "Palette";
		SetWindowIcon( "palette" );

		Layout = Layout.Column();

		var toolbar = new ToolBar( this );
		Layout.Add( toolbar );

		_searchFilter = new LineEdit( this );
		_searchFilter.PlaceholderText = "Filter Nodes..";
		_searchFilter.TextEdited += ( x ) => UpdateList();
		toolbar.AddWidget( _searchFilter );

		_searchFilterClear = new Button( "", "close", _searchFilter );
		_searchFilterClear.Visible = false;
		_searchFilterClear.Clicked = () => { _searchFilter.Text = ""; UpdateList(); };
		_searchFilter.Layout = Layout.Row( true );
		_searchFilter.Layout.Add( _searchFilterClear );
		_searchFilter.Layout.AddStretchCell();

		_treeView = new TreeView( this );
		Layout.Add( _treeView, 1 );

		_treeView.ItemDrag = ( a ) =>
		{
			if ( a is not TypeNode node )
				return false;

			var drag = new Drag( this );
			drag.Data.Text = $"{node.DisplayInfo.Fullname}";
			drag.Execute();

			return true;
		};

		UpdateList();
	}

	public IEnumerable<IGrouping<string, DisplayInfo>> GetItems()
	{
		var types = EditorTypeLibrary.GetTypes<ShaderNode>()
			.Where( x =>
			{
				if ( x.IsAbstract ) return false;
				if ( x.HasAttribute<HideAttribute>() ) return false;
				if ( IsSubgraph && x.TargetType == typeof( Result ) )
				{
					return false;
				}
				if ( !IsSubgraph && x.TargetType == typeof( FunctionResult ) )
				{
					return false;
				}
				if ( x.TargetType == typeof( SubgraphNode ) )
				{
					return false;
				}
				return true;
			} )
			.Select( x => DisplayInfo.ForType( x.TargetType ) );

		if ( !string.IsNullOrEmpty( _searchFilter.Text ) )
		{
			return types.Where( e => e.Name.Contains( _searchFilter.Text, StringComparison.OrdinalIgnoreCase ) )
				.GroupBy( x => x.Group?.ToLower() ).OrderBy( p => p.Key == null )
				.ThenBy( p => p.Key );
		}

		return types.GroupBy( x => x.Group?.ToLower() )
			.OrderBy( p => p.Key == null )
			.ThenBy( p => p.Key );
	}

	public void UpdateList()
	{
		_searchFilterClear.Visible = _searchFilter.Text.Length > 0;
		_treeView.Clear();

		foreach ( var category in GetItems() )
		{
			var header = new TreeNode.Header( null, category.Key ?? "Uncategorized" )
			{
				IconColor = Color.White.Darken( 0.1f )
			};

			_treeView.AddItem( header );
			_treeView.Open( header );

			foreach ( var item in category.OrderBy( x => x.Name ) )
			{
				var node = new TypeNode( item );
				header.AddItem( node );
			}

			header.AddItem( new TreeNode.Spacer( 8 ) );
		}
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect );

		base.OnPaint();
	}

	private class TypeNode : TreeNode
	{
		public DisplayInfo DisplayInfo { get; private set; }

		public TypeNode( DisplayInfo info )
		{
			DisplayInfo = info;
		}

		public override string GetTooltip()
		{
			List<string> extras = new();

			string name = $"<span style=\"font-size: 16px;font-weight: 900;\">{DisplayInfo.Name}</span>";
			string description = (DisplayInfo.Description ?? "No description given.");

			return $"{name}<br>{description}{(extras.Count > 0 ? "<br><br>" + string.Join( "<br>", extras ) : "")}";
		}

		public override void OnPaint( VirtualWidget item )
		{
			PaintSelection( item );

			var rect = item.Rect.Shrink( 0, 0, 4, 0 );
			rect.Left = 8;

			Paint.Antialiasing = true;

			Color fg = Theme.Text.Darken( 0.1f );

			if ( Paint.HasSelected )
			{
				fg = Theme.Text;
				Paint.ClearPen();
				Paint.SetBrush( Theme.Primary.WithAlpha( 0.9f ) );
				Paint.SetPen( Theme.Text );
			}
			else
			{
				Paint.SetDefaultFont();
				Paint.SetPen( Theme.Text.Darken( 0.3f ) );
			}

			var iconRect = rect.Shrink( 4, 4 );
			iconRect.Width = iconRect.Height;
			if ( !string.IsNullOrEmpty( DisplayInfo.Icon ) )
			{
				Paint.DrawIcon( iconRect, DisplayInfo.Icon, 14 );
			}
			else
			{
				Paint.Draw( iconRect, "animgraph_editor/search_result_node.png" );
			}

			var textRect = rect.Shrink( 4, 2 );
			textRect.Left = iconRect.Right + 6;

			Paint.SetDefaultFont();
			Paint.SetPen( fg );
			Paint.DrawText( textRect, DisplayInfo.Name, TextFlag.LeftCenter );
		}
	}
}
