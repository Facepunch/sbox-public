using Microsoft.AspNetCore.Components;
using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// A selectable tab belonging to a <see cref="TabBar"/>.
/// </summary>
[Library( "tab" )]
public class Tab : Panel
{
	readonly Label _title;
	readonly Label _badge;
	int? _count;
	readonly IconPanel _icon;
	readonly Image _image;
	readonly Button _close;
	Menu _menu;
	bool _leftPressed;
	string _iconSource;

	/// <summary>
	/// The bar containing this tab.
	/// </summary>
	public TabBar Bar => Parent as TabBar;

	/// <summary>
	/// Text displayed on the tab.
	/// </summary>
	[Parameter]
	public string Text
	{
		get => _title.Text;
		set
		{
			if ( string.IsNullOrEmpty( Tooltip ) || Tooltip == _title.Text ) Tooltip = value;
			_title.Text = value;
		}
	}

	/// <summary>
	/// Optional count badge. Null hides the badge; zero is displayed.
	/// </summary>
	[Parameter]
	public int? Count
	{
		get => _count;
		set
		{
			_count = value;
			_badge.Text = value?.ToString( System.Globalization.CultureInfo.InvariantCulture ) ?? "";
			_badge.Style.Display = value.HasValue ? DisplayMode.Flex : DisplayMode.None;
		}
	}

	/// <summary>
	/// Whether the bar is currently hiding this tab's title to save space.
	/// </summary>
	public bool IsIconOnly => HasClass( "icon-only" );
	internal bool HasIcon => IconTexture is not null || !string.IsNullOrWhiteSpace( Icon );

	internal void SetIconOnly( bool value )
	{
		SetClass( "icon-only", value && HasIcon );
		_title.Style.Display = IsIconOnly ? DisplayMode.None : DisplayMode.Flex;
	}

	/// <summary>
	/// Optional Material icon.
	/// </summary>
	[Parameter] public string Icon { get => _iconSource; set { _iconSource = value; _icon.Text = value; UpdateIcon(); } }

	/// <summary>
	/// Optional texture, taking precedence over the icon. The caller owns the texture.
	/// </summary>
	[Parameter] public Texture IconTexture { get => _image.Texture; set { _image.Texture = value; UpdateIcon(); } }

	/// <summary>
	/// Whether to show the close button and allow middle-click closing.
	/// </summary>
	[Parameter] public bool CanClose { get => _close.Style.Display != DisplayMode.None; set => _close.Style.Display = value ? DisplayMode.Flex : DisplayMode.None; }

	/// <summary>
	/// Whether this is the selected tab.
	/// </summary>
	public bool Selected => HasClass( "selected" );

	/// <summary>
	/// Populate the right-click menu. A Close option is supplied for closable tabs.
	/// </summary>
	public Action<Menu> BuildContextMenu { get; set; }

	public Tab()
	{
		AddClass( "tab" );
		AcceptsFocus = true;
		CanDragScroll = false;
		_icon = Add.Icon( null, "tab-icon" );
		_image = AddChild( new Image() );
		_image.AddClass( "tab-icon" );
		_title = Add.Label( "", "tab-title" );
		_badge = Add.Label( "", "tab-count" );
		Count = null;
		_close = AddChild( new Button( "", "close" ) );
		_close.RemoveClass( "button" );
		_close.AddClass( "tab-close" );
		_close.Tooltip = "Close tab";
		_close.AcceptsFocus = false;
		_close.AddEventListener( "onmousedown", e => e.StopPropagation() );
		_close.AddEventListener( "onclick", e => { e.StopPropagation(); Bar?.CloseTab( this ); } );
		CanClose = false;
		UpdateIcon();
	}

	void UpdateIcon()
	{
		_icon.Style.Display = IconTexture is null && !string.IsNullOrWhiteSpace( Icon ) ? DisplayMode.Flex : DisplayMode.None;
		_image.Style.Display = IconTexture is not null ? DisplayMode.Flex : DisplayMode.None;
	}

	/// <inheritdoc/>
	public override bool WantsDrag => Bar?.AllowReorder == true;

	protected override void OnMouseDown( MousePanelEvent e )
	{
		_leftPressed = e.Button == "mouseleft";
		// The input system only starts drags for a propagating mouse-down event.
		if ( !_leftPressed ) e.StopPropagation();
		if ( _leftPressed ) Bar?.SelectTab( this );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		e.StopPropagation();
		Bar?.SelectTab( this );
	}

	protected override void OnMiddleClick( MousePanelEvent e )
	{
		e.StopPropagation();
		Bar?.CloseTab( this );
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		e.StopPropagation();
		_menu?.Delete( true );
		var menu = new Menu();
		_menu = menu;
		BuildContextMenu?.Invoke( menu );
		if ( CanClose ) menu.AddOption( "Close", "close", () => Bar?.CloseTab( this ) );
		if ( menu.Options.Count == 0 ) { menu.Delete( true ); _menu = null; return; }
		menu.Closed += _ => { if ( _menu == menu ) _menu = null; menu.Delete( true ); };
		menu.Open( this, Popup.PositionMode.UnderMouse );
	}

	protected override void OnDragStart( DragEvent e )
	{
		e.StopPropagation();
		if ( _leftPressed ) Bar?.BeginReorder( this );
	}
	protected override void OnDrag( DragEvent e ) { e.StopPropagation(); Bar?.UpdateReorder( e.ScreenPosition ); }
	protected override void OnDragEnd( DragEvent e ) { e.StopPropagation(); Bar?.EndReorder( e.ScreenPosition ); }
	protected override void OnEscape( PanelEvent e )
	{
		if ( Bar?.IsReordering == true )
		{
			Bar.CancelReorder();
			e.StopPropagation();
			return;
		}
		base.OnEscape( e );
	}
	protected override void OnBlur( PanelEvent e ) { Bar?.CancelReorder(); base.OnBlur( e ); }

	/// <inheritdoc/>
	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( e.Button == "escape" && Bar?.IsReordering == true ) { Bar.CancelReorder(); e.StopPropagation = true; return; }
		if ( TryClickFromKeyboard( e ) ) return;
		base.OnButtonTyped( e );
	}

	/// <inheritdoc/>
	public override void OnDeleted()
	{
		_menu?.Delete( true );
		_menu = null;
		base.OnDeleted();
	}
}
