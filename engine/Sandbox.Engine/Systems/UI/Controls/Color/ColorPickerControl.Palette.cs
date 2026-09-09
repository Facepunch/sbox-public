using Sandbox.UI.Construct;

namespace Sandbox.UI;

//
// The palette - colours picked recently, and the ones the user has kept. Recent fills itself
// as a drag ends or a value is typed. Saved starts with the everyday colours and lives in a
// cookie; right-clicking a swatch replaces, removes or clears.
//
public partial class ColorPickerControl
{
	const string SavedCookie = "colorpicker.saved";

	/// <summary>
	/// How many recent colours to keep.
	/// </summary>
	const int RecentLimit = 12;

	/// <summary>
	/// The palette before the user has kept anything. The everyday colours, no primaries.
	/// </summary>
	static readonly string[] DefaultSaved =
	[
		"#ffffff", "#000000", "#00000000", "#d0d3d8", "#7d828c", "#3a3d44",
		"#3273eb", "#5cc8ff", "#4ecdc4", "#7ed491", "#f0b34c", "#e8632b", "#ff7b7b", "#b07cd8", "#8b5a2b"
	];

	/// <summary>
	/// Shared between every picker, for the session.
	/// </summary>
	static readonly List<PickerColor> Recent = new();

	List<PickerColor> _saved;
	Panel _recentRow;
	Panel _savedRow;
	Menu _swatchMenu;
	int _menuSwatch;

	void BuildPalette()
	{
		_recentRow = Section( "Recent" );
		_savedRow = Section( "Saved" );

		_swatchMenu = new Menu();
		_swatchMenu.AddOption( "Replace with current", () => { _saved[_menuSwatch] = _color; SavePalette(); } );
		_swatchMenu.AddOption( "Remove", () => { _saved.RemoveAt( _menuSwatch ); SavePalette(); } );
		_swatchMenu.AddOption( "Clear all", () => { _saved.Clear(); SavePalette(); } );

		_saved = LoadPalette();
		RenderRecent();
		RenderSaved();
	}

	Panel Section( string title )
	{
		var section = Add.Panel( "section" );
		section.Add.Label( title, "title" );
		return section.Add.Panel( "swatches" );
	}

	/// <summary>
	/// The colour has settled - a drag ended or a value was typed - so it goes in recent.
	/// </summary>
	void Commit()
	{
		var text = _color.ToText();
		Recent.RemoveAll( x => x.ToText() == text );
		Recent.Insert( 0, _color );

		if ( Recent.Count > RecentLimit )
			Recent.RemoveRange( RecentLimit, Recent.Count - RecentLimit );

		RenderRecent();
	}

	void RenderRecent()
	{
		_recentRow.DeleteChildren( true );
		foreach ( var color in Recent )
		{
			PaletteSwatch( _recentRow, color );
		}
	}

	void RenderSaved()
	{
		_savedRow.DeleteChildren( true );
		for ( int i = 0; i < _saved.Count; i++ )
		{
			var index = i;
			var swatch = PaletteSwatch( _savedRow, _saved[i] );
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

	ColorSwatch PaletteSwatch( Panel row, PickerColor color )
	{
		var swatch = row.AddChild( new ColorSwatch { Compact = true } );
		swatch.AddClass( "compact" );
		swatch.Set( color, HasAlpha, IsHdr );
		swatch.AddEventListener( "onclick", () => PickColor( color ) );
		return swatch;
	}

	void SyncPaletteUsage()
	{
		foreach ( var swatch in _recentRow.Children.Concat( _savedRow.Children ).OfType<ColorSwatch>() )
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
