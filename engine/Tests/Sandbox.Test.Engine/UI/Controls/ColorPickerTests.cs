using Sandbox.UI;
using static UITests.UiTesting;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize] // Focus, popups and recent colours are shared UI state
public class ColorPickerTests
{
	RootPanel root;
	bool previousRenderText;
	CookieContainer previousCookies;

	public class Target
	{
		[ColorUsage( hasAlpha: false, isHDR: false )]
		public Color Restricted { get; set; } = Color.White;

		[ColorUsage]
		public Color ExplicitUsage { get; set; } = Color.White;

		public Color Colour { get; set; } = Color.White;
	}

	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();
		previousRenderText = DisableTextRendering();
		previousCookies = Game.Cookies;
		Game.Cookies = null;
		InputFocus.Clear();
		InputFocus.Tick();
		BasePopup.CloseAll();
		root = CreateRoot();
	}

	[TestCleanup]
	public void Cleanup()
	{
		root.Delete( true );
		InputFocus.Clear();
		InputFocus.Tick();
		TextBlock.ui_rendertext = previousRenderText;
		Game.Cookies = previousCookies;
	}

	ColorPickerControl Picker( Target target, Panel parent = null ) => new()
	{
		Parent = parent ?? root,
		Property = Game.TypeLibrary.GetSerializedObject( target ).GetProperty( nameof( Target.Colour ) )
	};

	static Panel Palette( ColorPickerControl picker, string title ) => picker.Children
		.First( section => section.Children.OfType<Label>().Any( label => label.Text == title ) )
		.Children.First( panel => panel.HasClass( "swatches" ) );

	static Panel Numbers( ColorPickerControl picker ) => picker.Children.First( p => p.HasClass( "numbers" ) );
	static ColorTextEntry Hex( ColorPickerControl picker ) => Numbers( picker ).Children.OfType<ColorTextEntry>().Single();

	void SettleFocus()
	{
		root.UISystem.TickFocus();
		root.TickInternal();
	}

	void Type( TextEntry entry, string text )
	{
		entry.Focus();
		SettleFocus();
		Assert.IsTrue( entry.HasFocus );
		entry.Text = text;
		entry.OnValueChanged();
	}

	static void MouseEvent( Panel panel, string name, string button = "mouseleft", Vector2 position = default )
	{
		panel.DispatchEventImmediate( new MousePanelEvent( name, panel, button ) { LocalPosition = position } );
	}

	void ClickSwatch( ColorSwatch swatch )
	{
		// Input requests focus on press, then queues the click on release. In particular, a
		// recent swatch must survive the focus tick between those two events.
		swatch.Focus();
		MouseEvent( swatch, "onmousedown" );
		SettleFocus();
		Assert.IsFalse( swatch.IsDeleting );
		MouseEvent( swatch, "onmouseup" );
		MouseEvent( swatch, "onclick" );
		SettleFocus();
	}

	[TestMethod]
	public void SavedSwatchMenuSurvivesTextBlur()
	{
		var popup = new Popup( new Panel { Parent = root }, Popup.PositionMode.BelowLeft, 0 );
		var picker = Picker( new Target(), popup );
		Type( Hex( picker ), "red" );

		var saved = Palette( picker, "Saved" );
		var swatch = saved.Children.OfType<ColorSwatch>().First();
		var count = saved.Children.OfType<ColorSwatch>().Count();
		MouseEvent( swatch, "onrightclick", "mouseright" );
		SettleFocus();

		Assert.IsFalse( swatch.IsDeleting, "committing text must preserve the menu's anchor" );
		var menu = root.Children.OfType<Popup>().Single( p => p != popup );
		BasePopup.CloseAll( menu );
		Assert.IsFalse( popup.IsDeleting, "the menu is still part of the picker's popup chain" );

		var remove = menu.Children.OfType<Menu>().Single( option => option.Text == "Remove" );
		MouseEvent( remove, "onclick" );
		Assert.AreEqual( count - 1, saved.Children.OfType<ColorSwatch>().Count() );
		Assert.IsFalse( popup.IsDeleting );
	}

	[TestMethod]
	[DataRow( "Saved" )]
	[DataRow( "Recent" )]
	public void SwatchSelectionReplacesFocusedText( string palette )
	{
		var target = new Target();
		var picker = Picker( target );
		ClickSwatch( Palette( picker, "Saved" ).Children.OfType<ColorSwatch>().First() );
		var entry = Hex( picker );
		Type( entry, "red" );

		ClickSwatch( Palette( picker, palette ).Children.OfType<ColorSwatch>().First() );

		Assert.AreEqual( Color.White, target.Colour );
		Assert.AreEqual( "#FFFFFF", entry.Text );
		Assert.IsFalse( entry.HasFocus );
	}

	[TestMethod]
	public void SwatchSelectionRefreshesNumericFieldsAfterBlur()
	{
		var target = new Target { Colour = Color.Black };
		var picker = Picker( target );
		var tabs = picker.Children.SelectMany( p => p.Children ).OfType<ButtonGroup>().Single();
		MouseEvent( tabs.Children.OfType<Button>().Single( p => p.Text == "RGB" ), "onclick" );
		var entry = Numbers( picker ).Children.SelectMany( p => p.Children )
			.OfType<NumberEntry>().Single( p => p.Prefix == "R" );
		Type( entry, "255" );
		Assert.AreEqual( Color.Red, target.Colour );

		ClickSwatch( Palette( picker, "Saved" ).Children.OfType<ColorSwatch>().ElementAt( 1 ) );

		Assert.AreEqual( Color.Black, target.Colour );
		Assert.AreEqual( "0", entry.Text );
		Assert.IsFalse( entry.HasFocus );
	}

	[TestMethod]
	public void DraggingTheSquareEndsTextEditing()
	{
		var target = new Target();
		var picker = Picker( target );
		var entry = Hex( picker );
		Type( entry, "red" );
		var square = picker.Children.SelectMany( p => p.Children ).OfType<ColorSquare>().Single();
		square.Box.Rect = new Rect( 0, 0, 220, 220 );

		MouseEvent( square, "onmousedown" );
		SettleFocus();
		MouseEvent( square, "onmouseup" );

		Assert.AreEqual( Color.White, target.Colour );
		Assert.AreEqual( "#FFFFFF", entry.Text );
		Assert.IsFalse( entry.HasFocus );
	}

	[TestMethod]
	public void BrightnessSliderEndsTextEditing()
	{
		var target = new Target();
		var picker = Picker( target );
		var entry = Hex( picker );
		Type( entry, "red" );
		var slider = picker.Children.SelectMany( p => p.Children ).OfType<SliderControl>().Single();
		var track = slider.Children.First( p => p.HasClass( "inner" ) ).Children.First( p => p.HasClass( "track" ) );
		track.Box.Rect = new Rect( 0, 0, 200, 6 );
		root.MousePos = new Vector2( 100, 0 );

		MouseEvent( slider, "onmousedown" );
		SettleFocus();

		Assert.IsTrue( target.Colour.r > 1.0f );
		Assert.AreEqual( "#FF0000 * 16", entry.Text );
		Assert.IsFalse( entry.HasFocus );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ScrubbingNumbersAddsTheFinalColourToRecent( bool brightness )
	{
		var target = new Target();
		var picker = Picker( target );
		Type( Hex( picker ), brightness ? "red" : "black" );
		Hex( picker ).Blur();
		SettleFocus();
		var original = target.Colour;
		var tabs = picker.Children.SelectMany( p => p.Children ).OfType<ButtonGroup>().Single();
		MouseEvent( tabs.Children.OfType<Button>().Single( p => p.Text == "RGB" ), "onclick" );

		var entry = picker.Descendants.OfType<NumberEntry>().Single( p => p.Prefix == (brightness ? "×" : "R") );
		var prefix = entry.Children.OfType<Label>().Single( p => p.HasClass( "prefix-label" ) );
		prefix.Box.Rect = new Rect( 0, 0, 20, 20 );
		root.MousePos = new Vector2( 10, 10 );
		MouseEvent( entry, "onmousedown" );
		root.MousePos = new Vector2( 1010, 10 );
		MouseEvent( entry, "onmousemove" );
		MouseEvent( entry, "onmouseup" );

		Assert.AreNotEqual( original, target.Colour );
		var recent = Palette( picker, "Recent" ).Children.OfType<ColorSwatch>().First();
		Assert.AreEqual( target.Colour, recent.Color.ToColor(), "ending a numeric scrub must remember the final colour" );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RebindingRestoresUnannotatedColourUsage( bool inline )
	{
		var target = new Target();
		var so = Game.TypeLibrary.GetSerializedObject( target );
		BaseControl control = inline ? new ColorControl() : new ColorPickerControl();
		control.Parent = root;
		control.Property = so.GetProperty( nameof( Target.Restricted ) );
		control.Property = so.GetProperty( nameof( Target.Colour ) );

		var entry = inline ? control.Children.OfType<ColorTextEntry>().Single() : Hex( (ColorPickerControl)control );
		Type( entry, "#ff000080 * 4" );

		Assert.IsTrue( target.Colour.r > 1.0f, "the previous property's SDR restriction must not survive" );
		Assert.AreEqual( 128 / 255.0f, target.Colour.a, 0.001f );
	}

	[TestMethod]
	public void PropertyUsageDoesNotOverwriteConfiguredDefaults()
	{
		var so = Game.TypeLibrary.GetSerializedObject( new Target() );
		var picker = new ColorPickerControl { Parent = root, HasAlpha = false, IsHdr = false };
		picker.Property = so.GetProperty( nameof( Target.ExplicitUsage ) );
		Assert.IsTrue( picker.HasAlpha );
		Assert.IsTrue( picker.IsHdr );

		picker.Property = so.GetProperty( nameof( Target.Colour ) );
		Assert.IsFalse( picker.HasAlpha );
		Assert.IsFalse( picker.IsHdr );

		picker.Property = so.GetProperty( nameof( Target.ExplicitUsage ) );
		picker.Property = null;
		Assert.IsFalse( picker.HasAlpha );
		Assert.IsFalse( picker.IsHdr );
	}

	[TestMethod]
	public void ChangingUsageRefreshesPaletteWithoutReplacingSwatches()
	{
		var picker = Picker( new Target() );
		var swatch = Palette( picker, "Saved" ).Children.OfType<ColorSwatch>().ElementAt( 2 );
		Assert.IsNull( swatch.Style.BackgroundColor, "transparent swatches use a separate fill" );

		picker.HasAlpha = false;
		Assert.IsFalse( swatch.IsDeleting );
		Assert.AreEqual( 1.0f, swatch.Style.BackgroundColor.Value.a );

		picker.HasAlpha = true;
		Assert.IsNull( swatch.Style.BackgroundColor );
	}

	[TestMethod]
	public void OpeningTheInlineSwatchEndsTextEditing()
	{
		var target = new Target();
		var control = new ColorControl
		{
			Parent = root,
			Property = Game.TypeLibrary.GetSerializedObject( target ).GetProperty( nameof( Target.Colour ) )
		};
		var entry = control.Children.OfType<ColorTextEntry>().Single();
		Type( entry, "red" );
		MouseEvent( control.Children.OfType<ColorSwatch>().Single(), "onmousedown" );
		SettleFocus();

		Assert.IsFalse( entry.HasFocus );
		var picker = root.Children.OfType<Popup>().Single().Children.OfType<ColorPickerControl>().Single();
		ClickSwatch( Palette( picker, "Saved" ).Children.OfType<ColorSwatch>().First() );
		Assert.AreEqual( "#FFFFFF", entry.Text );
	}
}
