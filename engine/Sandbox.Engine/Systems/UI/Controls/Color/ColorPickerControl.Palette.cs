using Sandbox.UI.Construct;

namespace Sandbox.UI;

// Saved colours persist in a cookie and can be managed from a swatch's context menu.
public partial class ColorPickerControl
{
	const string SavedCookie = "colorpicker.saved";

	/// <summary>
	/// The palette before the user has kept anything. The everyday colours, no primaries.
	/// </summary>
	static readonly string[] DefaultSaved =
	[
		"#ffffff", "#000000", "#00000000", "#d0d3d8", "#7d828c", "#3a3d44",
		"#3273eb", "#5cc8ff", "#4ecdc4", "#7ed491", "#f0b34c", "#e8632b", "#ff7b7b", "#b07cd8", "#8b5a2b"
	];

	List<PickerColor> _saved;
	Panel _savedRow;
	Menu _swatchMenu;
	int _menuSwatch;

	void BuildPalette()
	{
		var section = Add.Panel( "section" );
		section.Add.Label( "Saved", "title" );
		_savedRow = section.Add.Panel( "swatches" );

		_swatchMenu = new Menu();
		_swatchMenu.AddOption( "Replace with current", () => { _saved[_menuSwatch] = _color; SavePalette(); } );
		_swatchMenu.AddOption( "Remove", () => { _saved.RemoveAt( _menuSwatch ); SavePalette(); } );
		_swatchMenu.AddOption( "Clear all", () => { _saved.Clear(); SavePalette(); } );

		_saved = LoadPalette();
		RenderSaved();
	}

	void RenderSaved()
	{
		_savedRow.DeleteChildren( true );
		for ( int i = 0; i < _saved.Count; i++ )
		{
			var index = i;
			var color = _saved[i];
			var swatch = _savedRow.AddChild( new ColorSwatch { Compact = true } );
			swatch.AddClass( "compact" );
			swatch.Set( color, HasAlpha, IsHdr );
			swatch.AddEventListener( "onclick", () => PickColor( color ) );
			swatch.AddEventListener( "onrightclick", () =>
			{
				_menuSwatch = index;
				_swatchMenu.Open( swatch, Popup.PositionMode.UnderMouse );
			} );
		}

		var add = _savedRow.Add.Icon( "add", "add" );
		add.AddEventListener( "onclick", () =>
		{
			var text = _color.ToText();
			if ( _saved.All( x => x.ToText() != text ) ) _saved.Add( _color );
			SavePalette();
		} );
	}

	void SyncPaletteUsage()
	{
		foreach ( var swatch in _savedRow.Children.OfType<ColorSwatch>() )
			swatch.Set( swatch.Color, HasAlpha, IsHdr );
	}

	void SavePalette()
	{
		Game.Cookies?.Set( SavedCookie, _saved.Select( x => x.ToText() ).ToList() );
		RenderSaved();
	}

	static List<PickerColor> LoadPalette()
	{
		var texts = Game.Cookies?.Get<List<string>>( SavedCookie, null ) ?? DefaultSaved.ToList();

		var colors = new List<PickerColor>();
		foreach ( var text in texts )
		{
			if ( PickerColor.TryParse( text, 0, out var color ) ) colors.Add( color );
		}

		return colors;
	}
}
