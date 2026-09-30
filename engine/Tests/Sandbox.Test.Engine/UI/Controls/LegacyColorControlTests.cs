using Sandbox.UI;
using System;
using static UITests.UiTesting;

namespace UITests.Controls;

#pragma warning disable CS0618 // These tests exercise the obsolete compatibility API.

[TestClass]
[DoNotParallelize] // Modifies UI system globals.
public class LegacyColorControlTests
{
	RootPanel root;

	public class Target
	{
		public Color Colour { get; set; }
	}

	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();
		root = CreateRoot();
	}

	[TestCleanup]
	public void Cleanup() => root.Delete( true );

	static SerializedProperty Property( Target target ) => Game.TypeLibrary.GetSerializedObject( target ).GetProperty( nameof( Target.Colour ) );

	static void Press( BaseControl control, Vector2 position )
	{
		control.Box.Rect = new Rect( 0, 0, 100, 100 );
		control.DispatchEventImmediate( new MousePanelEvent( "onmousedown", control, "mouseleft" ) { LocalPosition = position } );
	}

	static void AssertColor( Color expected, Color actual )
	{
		Assert.AreEqual( expected.r, actual.r, 0.0001f );
		Assert.AreEqual( expected.g, actual.g, 0.0001f );
		Assert.AreEqual( expected.b, actual.b, 0.0001f );
		Assert.AreEqual( expected.a, actual.a, 0.0001f );
	}

	[TestMethod]
	public void AlphaControlEditsAlphaWithoutChangingRgb()
	{
		var original = new Color( 2.0f, 0.5f, 0.25f, 0.8f );
		var target = new Target { Colour = original };
		var control = new ColorAlphaControl { Parent = root, Property = Property( target ) };
		control.Tick();

		Press( control, new Vector2( 25, 0 ) );

		AssertColor( original.WithAlpha( 0.25f ), target.Colour );
	}

	[TestMethod]
	public void HueControlPreservesSaturationValueAndAlpha()
	{
		var original = new ColorHsv( 120, 0.6f, 0.8f, 0.25f );
		var target = new Target { Colour = original.ToColor() };
		var control = new ColorHueControl { Parent = root, Property = Property( target ) };
		control.Tick();

		Press( control, new Vector2( 50, 0 ) );

		AssertColor( (original with { Hue = 180 }).ToColor(), target.Colour );
	}

	[TestMethod]
	public void SaturationValueControlPreservesHueAndAlpha()
	{
		var target = new Target { Colour = new ColorHsv( 120, 0.6f, 0.8f, 0.25f ).ToColor() };
		var control = new ColorSaturationValueControl { Parent = root, Property = Property( target ) };
		control.Tick();

		Press( control, new Vector2( 50, 25 ) );

		AssertColor( new ColorHsv( 120, 0.5f, 0.75f, 0.25f ).ToColor(), target.Colour );
	}

	[TestMethod]
	[DataRow( typeof( ColorAlphaControl ) )]
	[DataRow( typeof( ColorHueControl ) )]
	[DataRow( typeof( ColorSaturationValueControl ) )]
	public void ControlsCanBeUnboundAndRebound( Type type )
	{
		var control = (BaseControl)Activator.CreateInstance( type );
		control.Parent = root;
		control.Tick();

		var first = new Target { Colour = Color.Red };
		control.Property = Property( first );
		control.Tick();
		control.Property = null;
		control.Tick();
		Press( control, new Vector2( 25, 25 ) );
		AssertColor( Color.Red, first.Colour );

		var second = new Target { Colour = Color.Green };
		control.Property = Property( second );
		control.Tick();
		Press( control, new Vector2( 25, 25 ) );
		Assert.AreNotEqual( Color.Green, second.Colour );
		AssertColor( Color.Red, first.Colour );
	}
}

#pragma warning restore CS0618
