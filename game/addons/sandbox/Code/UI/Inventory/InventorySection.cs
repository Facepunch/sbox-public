using Sandbox.Utility;

namespace Sandbox;

/// <summary>
/// Paints the weapon buckets at the top of the screen
/// </summary>
public sealed class InventorySection : HudSection
{
	public override int Order => 10;

	public PlayerInventory Inventory { get; set; }

	public BaseSandboxWeapon Selected { get; set; }

	/// <summary>
	/// Should the menu be open right now?
	/// </summary>
	public bool Open { get; set; }

	const string IndexFont = "SandboxID"; // custom, supports only digits at the moment, don't use for anything else

	const float TileWidth = 230;
	const float TileHeight = 120;
	const float IconAreaHeight = 108; // the icon is centred in the top part, the rest holds the name
	const float CollapsedTileSize = 60; // unselected tile
	const float CollapsedHeight = 6f; // child tiles of unselected tiles
	const float Gap = 7; // between buckets
	const float IndexInset = 1; // from the edge of the bucket
	const float IndexBoxSize = 15;
	const float IndexDigitSize = 7; // height of the SandboxID digit, centred in the box
	const float NameFontSize = 16;
	const float NameLetterSpacing = 0.05f; // of the font size
	const float NameBottom = 10;
	const float IconScale = 0.8f;

	const float SlideDuration = 0.1f;
	const float ExpandDuration = 0.1f;
	const float ContentFadeDuration = 0.05f;

	const float EmptyOpacity = 0.5f; // buckets with no weapon keep this much of their opacity
	// flat greys that are never tinted by the HUD colour
	static readonly Color TileColor = HudStyle.PanelColor;
	static readonly Color HighlightTileColor = new Color( 0.29f, 0.29f, 0.29f, HudStyle.PanelColor.a ); // 4A4A4A
	static readonly Color DimContent = Color.White.WithAlpha( 0.51f ); // icon and name of unselected slots

	float _slide;
	float[] _expand = Array.Empty<float>();
	float[] _expandEased = Array.Empty<float>(); // _expand through the easing curve, worked out once per tick
	float[] _contentFade = Array.Empty<float>();

	int _expandedBucket = -1;
	BaseSandboxWeapon _shownSelection;

	readonly Dictionary<string, Texture> _iconCache = new();
	readonly List<List<BaseSandboxWeapon>> _columns = new();
	readonly Dictionary<string, string> _nameLabels = new();

	Rect _menuRect;
	float _gap;

	int ColumnCount => Inventory.IsValid() ? Inventory.MaxSlots : 0;

	public override void Tick()
	{
		var columns = ColumnCount;
		if ( _expand.Length != columns || _expandEased.Length != columns )
		{
			_expand = new float[columns];
			_expandEased = new float[columns];
			_contentFade = new float[columns];
		}

		if ( Open )
		{
			_expandedBucket = Selected.IsValid() ? Selected.Slot : -1;
			_shownSelection = Selected;
		}

		// the menu is about to slide in
		if ( Open && _slide <= 0 ) _nameLabels.Clear();

		var dt = RealTime.Delta;
		_slide = Approach( _slide, Open ? 1 : 0, dt / SlideDuration );

		for ( int i = 0; i < columns; i++ )
		{
			var expanded = i == _expandedBucket;
			_expand[i] = Approach( _expand[i], expanded ? 1 : 0, dt / ExpandDuration );
			_expandEased[i] = Easing.QuadraticInOut( _expand[i] );

			_contentFade[i] = expanded && _expand[i] >= 1 ? Approach( _contentFade[i], 1, dt / ContentFadeDuration ) : 0;
		}

		// nothing is drawn while the menu is shut, so skip walking the inventory for it
		if ( Open || _slide > 0 ) RefreshColumns();

		UpdateLayout();
	}

	static float Approach( float value, float target, float step ) => value < target ? MathF.Min( value + step, target ) : MathF.Max( value - step, target );

	void RefreshColumns()
	{
		var columns = ColumnCount;
		while ( _columns.Count < columns ) _columns.Add( new() );
		if ( _columns.Count > columns ) _columns.RemoveRange( columns, _columns.Count - columns );

		foreach ( var column in _columns ) column.Clear();
		if ( !Inventory.IsValid() ) return;

		foreach ( var weapon in Inventory.Weapons )
		{
			if ( weapon.Slot >= 0 && weapon.Slot < columns )
				_columns[weapon.Slot].Add( weapon );
		}
	}

	/// <summary>
	/// Collapsed columns are small squares, the selected one grows to 2:1 as it expands
	/// </summary>
	float ColumnWidth( int column, float scale ) => MathF.Round( MathX.Lerp( CollapsedTileSize, TileWidth, _expandEased[column] ) * scale );

	float HeadHeight( int column, float scale ) => MathF.Round( MathX.Lerp( CollapsedTileSize, TileHeight, _expandEased[column] ) * scale );

	float ColumnHeight( int column, float scale )
	{
		var items = Math.Max( _columns[column].Count, 1 );
		var child = MathX.Lerp( CollapsedHeight, TileHeight, _expandEased[column] );
		return MathX.Lerp( CollapsedTileSize, TileHeight, _expandEased[column] ) * scale + (items - 1) * (child + Gap) * scale;
	}

	/// <summary>
	/// Works out where the menu sits this frame, so the composite regions are known before anything is painted.
	/// Leaves <see cref="_menuRect"/> empty while the menu is not visible
	/// </summary>
	void UpdateLayout()
	{
		_menuRect = default;
		if ( _slide <= 0 || ColumnCount == 0 ) return;

		var scale = ScaleToScreen;
		var columns = ColumnCount;

		_gap = MathF.Max( 1, MathF.Round( Gap * scale ) );

		var totalWidth = (columns - 1) * _gap;
		for ( int c = 0; c < columns; c++ ) totalWidth += ColumnWidth( c, scale );
		var left = MathF.Floor( (Canvas.PixelSize.x - totalWidth) * 0.5f );

		var tallest = 0f;
		for ( int c = 0; c < columns; c++ ) tallest = MathF.Max( tallest, ColumnHeight( c, scale ) );

		var top = MathF.Round( Hud.Margin );
		var hidden = -(top + tallest + GlowRadius * scale * 2);
		var y0 = MathF.Round( MathX.Lerp( hidden, top, Easing.QuadraticInOut( _slide ) ) );

		_menuRect = new Rect( left, y0, totalWidth, tallest );
	}

	public override void Paint( Painter painter )
	{
		if ( _menuRect.Width <= 0 ) return;

		var scale = ScaleToScreen;
		var columns = ColumnCount;
		var gap = _gap;
		var left = _menuRect.Left;
		var y0 = _menuRect.Top;

		var x = left;
		for ( int c = 0; c < columns; c++ )
		{
			var tileWidth = ColumnWidth( c, scale );
			var tileHeight = HeadHeight( c, scale );
			var items = _columns[c];
			var expand = _expandEased[c];
			var childHeight = MathF.Round( MathX.Lerp( CollapsedHeight, TileHeight, expand ) * scale );

			var head = items.Count > 0 ? items[0] : null;
			DrawTile( painter, new Rect( x, y0, tileWidth, tileHeight ), head, c, IsHighlighted( head ), _contentFade[c], scale );

			var y = y0 + tileHeight + gap;
			for ( int i = 1; i < items.Count; i++ )
			{
				var rect = new Rect( x, y, tileWidth, childHeight );
				if ( expand <= 0 )
					DrawBar( painter, rect, scale );
				else
					DrawTile( painter, rect, items[i], -1, IsHighlighted( items[i] ), _contentFade[c], scale );

				y += childHeight + gap;
			}

			x += tileWidth + gap;
		}
	}

	bool IsHighlighted( BaseSandboxWeapon weapon ) => weapon.IsValid() && weapon == _shownSelection;

	void DrawTile( Painter painter, Rect rect, BaseSandboxWeapon weapon, int index, bool highlighted, float contentAlpha, float scale )
	{
		var empty = !weapon.IsValid();
		Color Shade( Color color ) => empty ? color.WithAlpha( color.a * EmptyOpacity ) : color;

		var tile = Shade( highlighted ? HighlightTileColor : TileColor );

		HudStyle.DrawPanel( painter, rect, tile, scale );

		if ( index >= 0 )
		{
			var inset = MathF.Round( IndexInset * scale );
			// the number sits where it always did, in a box that is no longer drawn
			var boxSize = MathF.Round( IndexBoxSize * scale );
			var box = new Rect( rect.Left + inset, rect.Top + inset, boxSize, boxSize );

			var em = IndexDigitSize / 1.25f * ScaleToScreen;
			var indexColor = highlighted ? Glowing( Tinted( Color.White ) ) : Shade( Tinted( Color.White ) );
			painter.TextStyle = new TextStyle( IndexFont, em, indexColor ) { Alignment = TextFlag.Center };
			painter.Text( IndexLabel( index ), new Rect( box.Left + em * 0.125f, box.Top, box.Width, box.Height ) );
		}

		if ( !weapon.IsValid() || contentAlpha <= 0 ) return;

		using var _ = painter.Scope();
		painter.Opacity = contentAlpha;

		var glow = Glowing( Tinted( Color.White ) );
		var iconColor = highlighted ? glow : Tinted( DimContent );
		var nameColor = highlighted ? glow : Tinted( DimContent );

		if ( GetIcon( weapon ) is { } icon && icon.Width > 0 && icon.Height > 0 )
		{
			var area = new Vector2( rect.Width, IconAreaHeight * scale ) * IconScale;
			var fit = MathF.Min( area.x / icon.Width, area.y / icon.Height );
			var size = new Vector2( icon.Width, icon.Height ) * fit;
			var center = new Vector2( rect.Center.x, rect.Top + IconAreaHeight * scale * 0.5f );
			painter.Texture( icon, new Rect( center - size * 0.5f, size ), iconColor );
		}

		painter.TextStyle = new TextStyle( HudStyle.Font, NameFontSize * ScaleToScreen, nameColor ) { FontWeight = 700, LetterSpacing = NameFontSize * NameLetterSpacing * ScaleToScreen, Alignment = TextFlag.CenterHorizontally | TextFlag.Bottom };
		painter.Text( NameLabel( weapon ), new Rect( rect.Left, rect.Top, rect.Width, rect.Height - MathF.Round( NameBottom * scale ) ) );
	}

	/// <summary>
	/// A collapsed child slot: just a flat bar, CollapsedHeight tall.
	/// </summary>
	void DrawBar( Painter painter, Rect rect, float scale )
	{
		HudStyle.DrawPanel( painter, rect, TileColor, scale );
	}

	const string WeaponFallbackIcon = "ui/hud/weapon_icon_test.png";

	Texture GetIcon( BaseSandboxWeapon weapon )
	{
		if ( !string.IsNullOrEmpty( weapon.InventoryIconOverride ) ) return LoadIcon( weapon.InventoryIconOverride );
		return weapon.DisplayIcon ?? LoadIcon( WeaponFallbackIcon );
	}

	Texture LoadIcon( string path )
	{
		if ( !_iconCache.TryGetValue( path, out var texture ) )
		{
			texture = Texture.Load( path );
			_iconCache[path] = texture;
		}

		return texture;
	}

	static string Localize( string text ) => !string.IsNullOrEmpty( text ) && text.StartsWith( '#' ) ? Game.Language.GetPhrase( text[1..] ) : text ?? "";

	// label strings are built once instead of every frame
	static readonly string[] IndexLabels = Enumerable.Range( 1, 16 ).Select( x => x.ToString() ).ToArray();

	static string IndexLabel( int index ) => index < IndexLabels.Length ? IndexLabels[index] : (index + 1).ToString();

	/// <summary>
	/// Cached uppercase display name for the inventory weapon
	/// </summary>
	string NameLabel( BaseSandboxWeapon weapon )
	{
		var raw = weapon.DisplayName ?? "";
		if ( !_nameLabels.TryGetValue( raw, out var label ) )
			_nameLabels[raw] = label = Localize( raw ).ToUpperInvariant();

		return label;
	}

	public override void GetCompositeRegions( List<Rect> regions )
	{
		if ( _menuRect.Width <= 0 ) return;
		regions.Add( _menuRect.Grow( MathF.Ceiling( GlowRadius * ScaleToScreen * 2 ) ) );
	}
}
