
namespace Editor.MeshEditor;

public partial class BlockTool
{
	private Layout ControlLayout { get; set; }

	public override Widget CreateToolSidebar()
	{
		var widget = new ToolSidebarWidget();

		widget.AddTitle( "Create Primitive" );

		var list = new PrimitiveListView( widget );
		list.SetItems( GetBuilderTypes() );

		{
			var group = widget.AddGroup( "Shape Type" );

			group.Add( list );
			list.SelectItem( list.Items.FirstOrDefault() );
			list.ItemSelected = ( e ) => Current = _primitives.FirstOrDefault( x => x.GetType() == (e as TypeDescription).TargetType );
		}

		{
			var group = widget.AddGroup( "Shape Settings" );

			ControlLayout = group;
			BuildControlSheet();
		}

		widget.Layout.AddStretchCell();

		return widget;
	}

	public void OnEdited( SerializedProperty property )
	{
		RebuildMesh();
	}

	private void BuildControlSheet()
	{
		if ( !ControlLayout.IsValid() )
			return;

		ControlLayout.Clear( true );

		if ( Current is null )
			return;

		var so = Current.GetSerialized();
		so.OnPropertyChanged += OnEdited;
		var sheet = new ControlSheet();
		sheet.AddObject( so );
		ControlLayout.Add( sheet );
	}
}

file class PrimitiveListView : ListView
{
	public PrimitiveListView( Widget parent ) : base( parent )
	{
		ItemSpacing = 0;
		ItemSize = 24;

		HorizontalScrollbarMode = ScrollbarMode.Off;
		VerticalScrollbarMode = ScrollbarMode.Off;
	}

	protected override void DoLayout()
	{
		base.DoLayout();

		var rect = CanvasRect;
		var itemSize = ItemSize;
		var itemSpacing = ItemSpacing;
		var itemsPerRow = 1;
		var itemCount = Items.Count();

		if ( itemSize.x > 0 ) itemsPerRow = ((rect.Width + itemSpacing.x) / (itemSize.x + itemSpacing.x)).FloorToInt();
		itemsPerRow = Math.Max( 1, itemsPerRow );

		var rowCount = MathX.CeilToInt( itemCount / (float)itemsPerRow );
		FixedHeight = rowCount * (itemSize.y + itemSpacing.y) + Margin.EdgeSize.y;
	}

	protected override string GetTooltip( object obj )
	{
		var builder = obj as TypeDescription;
		var displayInfo = DisplayInfo.ForType( builder.TargetType );
		return displayInfo.Name;
	}

	protected override void PaintItem( VirtualWidget item )
	{
		if ( item.Selected )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Blue );
			Paint.DrawRect( item.Rect, 4 );
		}

		var builder = item.Object as TypeDescription;
		var displayInfo = DisplayInfo.ForType( builder.TargetType );

		Paint.SetPen( item.Selected || item.Hovered ? Color.White : Color.Gray );
		Paint.DrawIcon( item.Rect, displayInfo.Icon ?? "square", HeaderBarStyle.IconSize );
	}
}
