using Sandbox.UI;

namespace Editor;

public class ToolSidebarWidget : Widget
{
	public ToolSidebarWidget( Widget parent = null ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 2;
		Layout.Margin = 8;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.ClearPen();
		Paint.SetBrushAndPen( Theme.TabBackground );
		Paint.DrawRect( Paint.LocalRect, 0 );
	}

	public void AddTitle( string title, string icon = "people" )
	{
		var titleRow = Layout.AddRow();
		titleRow.Margin = new Margin( 0, 0, 0, 8 );
		titleRow.Spacing = 4;

		var iconLabel = titleRow.Add( new IconButton( icon ), 0 );
		iconLabel.IconSize = 18;
		iconLabel.Background = Color.Transparent;
		iconLabel.Foreground = Theme.Blue;

		var titleLabel = titleRow.Add( new Label.Header( title ), 1 );
	}

	public Layout AddGroup( string title )
	{
		var group = new SidebarGroupWidget();
		group.Title = title;

		Layout.Add( group );

		return group.ContentLayout;
	}

	public IconButton CreateButton( string text, string icon, string keybind, Action clicked, bool enabled, Layout addToLayout = null, bool active = false )
	{
		var btn = new IconButton( icon, clicked, this )
		{
			Enabled = enabled,
			ToolTip = text,
			IconSize = 24,
			FixedSize = 32,
		};

		if ( !string.IsNullOrEmpty( keybind ) )
		{
			btn.ToolTip = text + " [" + EditorShortcuts.GetKeys( keybind ) + "]";
		}

		if ( active )
		{
			btn.Background = Theme.Blue.WithAlpha( 0.2f );
			btn.Foreground = Theme.Blue;
		}

		addToLayout?.Add( btn );

		return btn;
	}

	public Widget CreateSmallButton( string text, string icon, string keybind, Action clicked, bool enabled, Layout addToLayout = null )
	{
		var btn = new IconButton( icon, clicked, this )
		{
			Enabled = enabled,
			ToolTip = text,
			FixedSize = Theme.RowHeight,
		};

		if ( !string.IsNullOrEmpty( keybind ) )
		{
			btn.ToolTip = text + " [" + EditorShortcuts.GetKeys( keybind ) + "]";
		}

		addToLayout?.Add( btn );

		return btn;
	}
}

file class SidebarGroupWidget : Widget
{
	public readonly Layout ContentLayout;

	public string Title { get; set; }

	public SidebarGroupWidget( Widget parent = null ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 2;
		Layout.Margin = new Margin( 8, 16, 8, 8 );

		ContentLayout = Layout.AddColumn();
		ContentLayout.Spacing = 4;

		HorizontalSizeMode = SizeMode.Expand | SizeMode.CanGrow;
		VerticalSizeMode = SizeMode.CanShrink;
	}

	protected override Vector2 SizeHint()
	{
		return 0;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		var controlRect = Paint.LocalRect;
		controlRect.Top += 6;
		controlRect = controlRect.Shrink( 0, 0, 1, 1 );

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;
		Paint.SetBrushAndPen( Theme.Text.WithAlpha( 0.01f ), Theme.Text.WithAlpha( 0.1f ) );
		Paint.DrawRect( controlRect, 4 );

		Paint.SetPen( Theme.TextControl.WithAlpha( 0.6f ) );
		Paint.SetDefaultFont( 7, 500 );
		Paint.DrawText( new Vector2( 12, 0 ), Title );
	}
}

