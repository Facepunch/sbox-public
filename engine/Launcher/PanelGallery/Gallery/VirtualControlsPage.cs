namespace Sandbox.PanelGallery;

/// <summary>
/// Exercises visible-cell creation, selection, filtering and mutation with a large dataset.
/// </summary>
public class VirtualControlsPage : GalleryPage
{
	readonly BaseVirtualPanel _view;
	readonly List<object> _items = [];
	readonly Sandbox.UI.Label _status;
	int _nextId;
	int _selected = -1;
	string _query = "";

	public VirtualControlsPage( bool grid ) : base( grid ? "Virtual Grid" : "Virtual List",
		"10,000 items, with panels created only for visible cells. Scroll, select an item, filter the data or append more rows." )
	{
		AddClass( "virtual-control-demo" );
		var actions = Add.Panel( "demo-actions" );
		var search = new Sandbox.UI.TextEntry { Placeholder = "Filter item numbers…" };
		search.OnTextEdited = value => { _query = value; Refresh(); };
		actions.AddChild( search );
		actions.AddChild( new Sandbox.UI.Button( "Add 100", "add", "primarybutton", () => { AddItems( 100 ); Refresh(); } ) );
		actions.AddChild( new Sandbox.UI.Button( "Reset", "restart_alt", "flatbutton", () =>
		{
			_items.Clear(); _nextId = 0; _selected = -1; _query = ""; search.Text = "";
			AddItems( 10000 ); Refresh();
		} ) );

		_view = grid ? new VirtualGrid { ItemSize = new Vector2( 150, 100 ) } : new VirtualList { ItemHeight = 38 };
		_view.AddClass( "virtual-demo-view" );
		_view.OnCreateCell = ( cell, value ) =>
		{
			var id = (int)value;
			var button = new Sandbox.UI.Button( $"Item {id:00000}", grid ? "inventory_2" : "description", "virtual-demo-item", () => _selected = id );
			button.Value = id;
			button.SetClass( "tile", grid );
			cell.AddChild( button );
		};
		AddChild( _view );
		_status = Add.Label( "", "page-blurb" );
		AddItems( 10000 );
		Refresh();
	}

	void AddItems( int count )
	{
		for ( int i = 0; i < count; i++ ) _items.Add( _nextId++ );
	}

	void Refresh()
	{
		_view.Items = _items.Where( item => ((int)item).ToString( "00000" ).Contains( _query, StringComparison.OrdinalIgnoreCase ) );
		_view.ScrollOffset = Vector2.Zero;
	}

	public override void Tick()
	{
		base.Tick();
		int visible = 0;
		foreach ( var cell in _view.Children )
			foreach ( var button in cell.Children.OfType<Sandbox.UI.Button>() )
			{
				button.Active = (int)button.Value == _selected;
				visible++;
			}
		_status.Text = $"{_items.Count:N0} source items · {visible} live item panels · " + (_selected < 0 ? "Nothing selected" : $"Selected item {_selected:00000}");
	}
}
