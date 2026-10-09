using Sandbox.UI;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class CurveControlTests
{
	[TestMethod]
	public void SavedPresetsPersistAndCanBeReplacedAndDeleted()
	{
		ThreadSafe.MarkMainThread();
		var previous = Game.Cookies;
		var files = new MemoryFileSystem();
		var cookies = new CookieContainer( "curve-test", true, files );
		Game.Cookies = cookies;
		try
		{
			var editor = new CurveEditor { Value = Curve.EaseIn };
			editor.SavePreset();
			cookies.Dispose();
			Game.Cookies = cookies = new CookieContainer( "curve-test", true, files );
			var reopened = new CurveEditor();
			Assert.AreEqual( 1, reopened.SavedPresets.Count );
			Assert.AreEqual( Curve.EaseIn.Evaluate( 0.5f ), reopened.SavedPresets[0].Evaluate( 0.5f ) );
			reopened.Value = Curve.Linear;
			reopened.ReplacePreset( 0 );
			Assert.AreEqual( 0.5f, reopened.SavedPresets[0].Evaluate( 0.5f ), 0.001f );
			reopened.ApplyPreset( Curve.EaseOut );
			reopened.Undo();
			Assert.AreEqual( 0.5f, reopened.Value.Evaluate( 0.5f ), 0.001f );
			reopened.DeletePreset( 0 );
			Assert.AreEqual( 0, reopened.SavedPresets.Count );
			editor.Delete( true ); reopened.Delete( true );
		}
		finally { cookies.Dispose(); Game.Cookies = previous; }
	}

	public class Target
	{
		public Curve Response { get; set; } = Curve.Ease;
		public CurveRange Range { get; set; } = new( Curve.Ease, Curve.Linear );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CompactPreviewOpensBoundEditorAndUpdatesProperty( bool range )
	{
		ThreadSafe.MarkMainThread();
		var previous = UiTesting.DisableTextRendering();
		try
		{
			using var surface = new UISurface { Size = new Vector2( 1200, 900 ), DpiScale = 1, MouseInside = true };
			var target = new Target();
			CurveControl control = range ? new CurveRangeControl() : new CurveControl();
			control.Parent = surface.Root; control.Style.Width = 500;
			control.Property = Game.TypeLibrary.GetSerializedObject( target ).GetProperty( range ? nameof( Target.Range ) : nameof( Target.Response ) );
			void Frame() { for ( int i = 0; i < 4; i++ ) UiTesting.Frame( surface ); }
			Frame();
			Assert.IsTrue( control.Box.Rect.Height <= 34 );
			Assert.IsFalse( control.Descendants.OfType<CurveEditor>().Any() );
			surface.MouseMoved( control.Box.Rect.Position + control.Box.Rect.Size * 0.5f ); Frame();
			surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, true, default ); Frame();
			surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, false, default ); Frame();
			var editor = surface.Root.Descendants.OfType<CurveEditor>().Single();
			Assert.AreEqual( range, editor.IsRange );
			editor.SetViewBounds( new Vector2( -10, -10 ), new Vector2( 10, 10 ) ); Frame();
			Assert.AreEqual( new Vector2( 0, -10 ), editor.ViewMin );
			Assert.AreEqual( new Vector2( 1, 10 ), editor.ViewMax );
			editor.MoveKey( 0, 0.1f, 0.2f ); Frame();
			Assert.AreEqual( 0.2f, (range ? target.Range.A : target.Response).Frames[0].Value );
			editor.Undo(); Frame();
			Assert.AreEqual( 0f, (range ? target.Range.A : target.Response).Frames[0].Value );
			control.OpenEditor(); Frame();
			Assert.AreEqual( 1, surface.Root.Descendants.OfType<CurveEditor>().Count() );
			editor.Ancestors.OfType<Popup>().First().Delete( true ); Frame();
			control.OnButtonTyped( new ButtonEvent( "enter", true, 0, default ) ); Frame();
			Assert.AreEqual( 1, surface.Root.Descendants.OfType<CurveEditor>().Count() );
			// Native popup windows dispose their root, recursively deleting the popup without
			// setting IsDeleting on each child. The property must still be able to reopen it.
			var popup = surface.Root.Descendants.OfType<Popup>().Single();
			var window = surface.Root.AddChild<Panel>();
			popup.Parent = window;
			window.Delete( true ); Frame();
			Assert.IsFalse( popup.IsValid );
			control.OpenEditor(); Frame();
			Assert.AreEqual( 1, surface.Root.Descendants.OfType<CurveEditor>().Count() );
			control.Delete( true ); Frame();
			Assert.IsFalse( surface.Root.Descendants.OfType<CurveEditor>().Any() );
		}
		finally { TextBlock.ui_rendertext = previous; }
	}
}
