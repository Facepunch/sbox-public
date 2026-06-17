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

	public Layout AddGroup( string title, SizeMode sizeMode = SizeMode.CanShrink, bool collapsible = false )
	{
		var group = CreateGroupWidget( title, sizeMode, collapsible );
		Layout.Add( group );
		return group.ContentLayout;
	}

	/// <summary>
	/// Creates a group widget parented to the given widget without adding it to this sidebar's layout.
	/// Use this to build groups that belong to a separately-controlled container.
	/// </summary>
	internal static SidebarGroupWidget CreateGroupWidget( string title, SizeMode sizeMode = SizeMode.CanShrink, bool collapsible = false )
	{
		var group = new SidebarGroupWidget();
		group.Title = title;
		group.VerticalSizeMode = sizeMode;
		group.Collapsible = collapsible;
		group.RestoreState();
		return group;
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

	/// <summary>
	/// Add a collapsible "Shortcuts" section listing the given shortcut identifiers.
	/// </summary>
	public void AddShortcuts( params string[] identifiers )
	{
		AddShortcuts( identifiers
			.Select( id => EditorShortcuts.Entries.FirstOrDefault( e => e.Identifier == id ) )
			.Where( e => e is not null )
			.Select( e => (e.Name, e.DisplayKeys) )
			.ToArray() );
	}

	/// <summary>
	/// Add a collapsible "Shortcuts" section with manual name/key pairs for shortcuts
	/// that don't have a [Shortcut] attribute (e.g. "Lasso", "Alt + Click").
	/// </summary>
	public void AddShortcuts( params (string Name, string Keys)[] shortcuts )
	{
		if ( shortcuts.Length == 0 ) return;

		var group = AddGroup( "Shortcuts", collapsible: true );
		for ( var i = 0; i < shortcuts.Length; i++ )
			group.Add( new ShortcutRow( shortcuts[i].Name, shortcuts[i].Keys, i ) );
	}
}

/// <summary>
/// A single shortcut row: name on the left, keys on the right, with alternating background.
/// </summary>
file class ShortcutRow : Widget
{
	readonly string _name;
	readonly string _keys;
	readonly int _index;

	public ShortcutRow( string name, string keys, int index )
	{
		_name = name;
		_keys = keys;
		_index = index;
		FixedHeight = 20;
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( _index % 2 == 1 ? Theme.WidgetBackground.Darken( 0.1f ) : Theme.WidgetBackground );
		Paint.DrawRect( LocalRect, 2 );

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;
		Paint.SetDefaultFont( 7 );

		var rect = LocalRect.Shrink( 8, 0 );
		Paint.SetPen( Theme.Text.WithAlpha( 0.7f ) );
		Paint.DrawText( rect, _name, TextFlag.LeftCenter );
		Paint.SetPen( Theme.Text.WithAlpha( 0.4f ) );
		Paint.DrawText( rect, _keys, TextFlag.RightCenter );
	}
}

internal class SidebarGroupWidget : Widget
{
	public readonly Layout ContentLayout;

	public string Title { get; set; }
	public bool Collapsible { get; set; }

	Widget _contentWidget;
	bool _collapsed;
	bool _titleHovered;

	bool Collapsed
	{
		get => _collapsed;
		set
		{
			_collapsed = value;
			_contentWidget.Visible = !value;
			Layout.Margin = value ? new Margin( 8, 16, 8, 0 ) : new Margin( 8, 16, 8, 8 );
			if ( Collapsible ) EditorCookie.Set( $"SidebarGroup.{Title}", value );
		}
	}

	public SidebarGroupWidget( Widget parent = null ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 2;
		Layout.Margin = new Margin( 8, 16, 8, 8 );

		_contentWidget = new Widget( this );
		_contentWidget.Layout = Layout.Column();
		_contentWidget.Layout.Spacing = 4;
		Layout.Add( _contentWidget );

		ContentLayout = _contentWidget.Layout;

		HorizontalSizeMode = SizeMode.Expand | SizeMode.CanGrow;
		VerticalSizeMode = SizeMode.CanShrink;
		MouseTracking = true;
	}

	public void RestoreState()
	{
		if ( !Collapsible ) return;
		_collapsed = EditorCookie.Get( $"SidebarGroup.{Title}", false );
		_contentWidget.Visible = !_collapsed;
		Layout.Margin = _collapsed ? new Margin( 8, 16, 8, 0 ) : new Margin( 8, 16, 8, 8 );
	}

	protected override Vector2 SizeHint()
	{
		return 0;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		var wasHovered = _titleHovered;
		_titleHovered = Collapsible && e.LocalPosition.y < 14;
		if ( wasHovered != _titleHovered ) Update();
	}

	protected override void OnMouseLeave()
	{
		if ( _titleHovered )
		{
			_titleHovered = false;
			Update();
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( !Collapsible ) return;
		if ( e.LocalPosition.y > 14 ) return;

		Collapsed = !Collapsed;
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		var controlRect = Paint.LocalRect;
		controlRect.Top += 6;
		controlRect = controlRect.Shrink( 0, 0, 1, 1 );

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		if ( !Collapsed )
		{
			Paint.SetBrushAndPen( Theme.Text.WithAlpha( 0.01f ), Theme.Text.WithAlpha( 0.1f ) );
			Paint.DrawRect( controlRect, 4 );
		}

		var textAlpha = _titleHovered ? 1.0f : 0.6f;
		Paint.SetPen( Theme.TextControl.WithAlpha( textAlpha ) );
		Paint.SetDefaultFont( 7, 500 );

		if ( Collapsible )
		{
			var icon = Collapsed ? "chevron_right" : "expand_more";
			Paint.DrawIcon( new Rect( 2, -2, 12, 14 ), icon, 10 );
			Paint.DrawText( new Vector2( 16, 0 ), Title );
			Cursor = CursorShape.Finger;
		}
		else
		{
			Paint.DrawText( new Vector2( 12, 0 ), Title );
		}
	}
}
