namespace Editor.DooEditor;
/// <summary>
/// Toolbox widget showing available block types that can be dragged onto the canvas.
/// </summary>
public class DooToolbox : Widget
{
	public DooEditorWidget Editor { get; }

	public DooToolbox( DooEditorWidget editor ) : base( null )
	{
		Editor = editor;

		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 2;

		RebuildToolbox();

		Layout.AddStretchCell();
	}

	void RebuildToolbox()
	{
		Layout.Clear( true );

		Layout.Add( new ToolboxItem<Doo.InvokeBlock>() );
		Layout.Add( new ToolboxItem<Doo.SetBlock>() );
		Layout.Add( new ToolboxItem<Doo.DelayBlock>() );
		Layout.Add( new ToolboxItem<Doo.ForBlock>() );
		//Layout.Add( new ToolboxItem<Doo.ReturnBlock>() );
	}
}

/// <summary>
/// A draggable block template in the toolbox.
/// </summary>
public class ToolboxItem<T> : Widget where T : Doo.Block, new()
{
	public string BlockIcon { get; set; }
	public TypeDescription BlockType { get; init; }
	public Color BlockColor { get; set; }

	public ToolboxItem() : base( null )
	{
		BlockType = TypeLibrary.GetType( typeof( T ) );
		ToolTip = $"<strong>{BlockType.Title}</strong><br>{BlockType.Description}";
		BlockIcon = BlockType.Icon;

		var b = new T();
		BlockColor = BlockType.GetAttribute<IconAttribute>()?.BackgroundColor ?? Color.White;

		FixedSize = Theme.RowHeight * 1.5f;
		Cursor = CursorShape.Finger;
		IsDraggable = true;
	}

	protected override void OnPaint()
	{
		var rect = LocalRect.Shrink( 2 );

		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( BlockColor.WithAlpha( Paint.HasMouseOver ? 1f : 0.7f ) );
		Paint.DrawRect( rect, 2 );

		Paint.SetPen( BlockColor.Lighten( 1.5f ) );
		Paint.DrawIcon( rect, BlockIcon, 18 );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			var block = CreateBlock();
			GetAncestor<DooEditorWidget>().Target.Body.Add( block );
			GetAncestor<DooEditorWidget>().BlockTree.SelectItem( block );
		}
	}

	protected override void OnDragStart()
	{
		var drag = new Drag( this );
		drag.Data.Object = CreateBlock();
		drag.Execute();
	}

	Doo.Block CreateBlock()
	{
		var block = BlockType.Create<Doo.Block>();

		block.Reset();

		return block;
	}
}
