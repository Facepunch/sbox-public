using System;
using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.UI;

namespace UITests.Controls;

[TestClass, DoNotParallelize]
public class ScriptControlTests
{
	[TestMethod]
	[DataRow( "y", false )]
	[DataRow( "z", true )]
	public void KeyboardUndoAndRedoDismissCompletion( string redoKey, bool shift )
	{
		WithEditor( ( control, surface ) =>
		{
			control.Entry.OnKeyTyped( 's' );
			Assert.IsTrue( control.CompletionVisible );
			control.Entry.OnButtonTyped( new ButtonEvent( "z", true, 0, KeyboardModifiers.Ctrl ) );
			Assert.AreEqual( "", control.Source );
			Assert.IsFalse( control.CompletionVisible );

			control.ShowCompletions();
			Assert.IsTrue( control.CompletionVisible );
			var modifiers = KeyboardModifiers.Ctrl | (shift ? KeyboardModifiers.Shift : KeyboardModifiers.None);
			control.Entry.OnButtonTyped( new ButtonEvent( redoKey, true, 0, modifiers ) );
			Assert.AreEqual( "s", control.Source );
			Assert.IsFalse( control.CompletionVisible );
		} );
	}

	[TestMethod]
	public void CommentShortcutUsesTheNativeKeyName()
	{
		WithEditor( ( control, surface ) =>
		{
			control.Source = "return 1;";
			UiTesting.Frame( surface );
			var key = NativeEngine.ButtonCode.KEY_SLASH.ToString();
			control.Entry.OnButtonTyped( new ButtonEvent( key, true, 0, KeyboardModifiers.Ctrl ) );
			Assert.AreEqual( "// return 1;", control.Source );
			UiTesting.Frame( surface );
			control.Entry.OnButtonTyped( new ButtonEvent( key, true, 0, KeyboardModifiers.Ctrl ) );
			Assert.AreEqual( "return 1;", control.Source );
			control.ReadOnly = true;
			control.Entry.OnButtonTyped( new ButtonEvent( key, true, 0, KeyboardModifiers.Ctrl ) );
			Assert.AreEqual( "return 1;", control.Source );
		} );
	}

	static void WithEditor( Action<ScriptControl, UISurface> check )
	{
		var previous = GlobalContext.Current;
		var renderText = UiTesting.DisableTextRendering();
		using var surface = new UISurface { Size = new Vector2( 1000, 800 ) };
		ScriptControl control = null;
		try
		{
			var types = new TypeLibrary();
			types.AddIntrinsicTypes();
			types.AddAssembly( typeof( ScriptControl ).Assembly, false );
			GlobalContext.Current = new GlobalContext { TypeLibrary = types };
			control = new ScriptControl { Parent = surface.Root, Inputs = [new( "speed", typeof( float ) )] };
			UiTesting.Frame( surface );
			check( control, surface );
		}
		finally
		{
			control?.Delete( true );
			GlobalContext.Current = previous;
			TextBlock.ui_rendertext = renderText;
		}
	}

	[TestMethod]
	public void CompletionShortcutDoesNotInsertTextAndWheelZoomDoesNotEdit()
	{
		var previous = GlobalContext.Current;
		var renderText = UiTesting.DisableTextRendering();
		using var surface = new UISurface { Size = new Vector2( 1000, 800 ) };
		ScriptControl control = null;
		try
		{
			var types = new TypeLibrary();
			types.AddIntrinsicTypes();
			types.AddAssembly( typeof( ScriptControl ).Assembly, false );
			GlobalContext.Current = new GlobalContext { TypeLibrary = types };
			control = new ScriptControl { Parent = surface.Root, Source = "sp", Inputs = [new( "speed", typeof( float ) )] };
			control.Style.Width = 300;
			control.Style.Height = 100;
			UiTesting.Frame( surface );
			control.CaretOffset = 2;
			control.FocusEditor();
			UiTesting.Frame( surface );

			// Use the input queue: text and button events are delivered separately.
			surface.SetKey( "space", true, 32, KeyboardModifiers.Ctrl );
			surface.TypeText( " " );
			surface.SetKey( "space", false, 32, KeyboardModifiers.None );
			UiTesting.Frame( surface );
			Assert.AreEqual( "sp", control.Source );
			Assert.IsTrue( control.CompletionVisible );

			surface.SetKey( "space", true, 32, KeyboardModifiers.None );
			surface.TypeText( " " );
			UiTesting.Frame( surface );
			Assert.AreEqual( "sp ", control.Source );

			control.Entry.OnKeyTyped( ' ', KeyboardModifiers.Ctrl | KeyboardModifiers.Alt );
			Assert.AreEqual( "sp  ", control.Source, "AltGr text must still be accepted." );
			control.Entry.OnKeyTyped( ' ', KeyboardModifiers.Ctrl | KeyboardModifiers.Shift );
			Assert.AreEqual( "sp  ", control.Source );

			var size = control.ComputedStyle.FontSize.Value.GetPixels( 100 );
			var caret = control.CaretOffset;
			surface.System.Input.AddMouseWheel( new Vector2( 0, 1 ), KeyboardModifiers.Ctrl );
			control.Entry.OnMouseWheel( new Vector2( 0, -1 ) );
			Assert.AreEqual( size + 1, control.Style.FontSize.Value.GetPixels( 100 ) );
			Assert.AreEqual( caret, control.CaretOffset );
			Assert.AreEqual( "sp  ", control.Source );
			surface.System.Input.AddMouseWheel( new Vector2( 0, 1 ), KeyboardModifiers.None );
			control.Entry.OnMouseWheel( new Vector2( 0, -1 ) );
			Assert.AreEqual( size + 1, control.Style.FontSize.Value.GetPixels( 100 ), "Ordinary scrolling must not zoom." );
			UiTesting.Frame( surface );
			Assert.AreEqual( size + 1, control.Entry.Descendants.Single( x => x.HasClass( "content-label" ) ).ComputedStyle.FontSize.Value.GetPixels( 100 ) );
			Assert.AreEqual( size + 1, control.Descendants.Single( x => x.HasClass( "line-gutter" ) ).ComputedStyle.FontSize.Value.GetPixels( 100 ) );
			surface.System.Input.AddMouseWheel( new Vector2( 0, -1 ), KeyboardModifiers.Ctrl );
			control.Entry.OnMouseWheel( new Vector2( 0, 1 ) );
			Assert.AreEqual( size, control.Style.FontSize.Value.GetPixels( 100 ), "Wheel down must reduce the font size." );
			control.Entry.OnMouseWheel( new Vector2( 0, 100 ) );
			Assert.AreEqual( 8f, control.Style.FontSize.Value.GetPixels( 100 ) );
			control.Entry.OnMouseWheel( new Vector2( 0, -100 ) );
			Assert.AreEqual( 48f, control.Style.FontSize.Value.GetPixels( 100 ) );
		}
		finally
		{
			control?.Delete( true );
			GlobalContext.Current = previous;
			TextBlock.ui_rendertext = renderText;
		}
	}

	[TestMethod]
	public void UnattachedControlCanAnalyzeAndEditWithoutAUiSystem()
	{
		var previous = GlobalContext.Current;
		ScriptControl control = null;
		try
		{
			var types = new TypeLibrary();
			types.AddIntrinsicTypes();
			types.AddAssembly( typeof( ScriptControl ).Assembly, false );
			GlobalContext.Current = new GlobalContext { TypeLibrary = types };

			// PanelGallery constructs controls before attaching them to a window's UI system.
			control = new ScriptControl();
			Assert.AreEqual( "", control.Analysis.Source );
			Assert.AreEqual( 1, control.Descendants.Count( p => p.HasClass( "content-label" ) ) );
			control.Source = "return 1;";
			control.CaretOffset = control.Source.Length;
			control.InsertText( " // comment" );
			Assert.AreEqual( control.Source, control.Analysis.Source );
			control.Theme = ScriptControl.ThemeDefinition.Default with { Comment = Color.Red };
			control.Undo();
			Assert.AreEqual( "return 1;", control.Source );
		}
		finally
		{
			control?.Delete( true );
			GlobalContext.Current = previous;
		}
	}
	[TestMethod]
	public void CompletionUsesThePopupHostAndClosesWithTheEditor()
	{
		var previous = GlobalContext.Current;
		var renderText = UiTesting.DisableTextRendering();
		using var surface = new UISurface { Size = new Vector2( 1000, 800 ) };
		var host = new RecordingPopupHost();
		surface.System.PopupHost = host;
		surface.Root.PanelBounds = new Rect( 0, 0, 1000, 800 );
		ScriptControl control = null;
		try
		{
			var types = new TypeLibrary();
			types.AddIntrinsicTypes();
			types.AddAssembly( typeof( ScriptControl ).Assembly, false );
			GlobalContext.Current = new GlobalContext { TypeLibrary = types };
			control = new ScriptControl { Parent = surface.Root, Source = "sp", Inputs = [new( "speed", typeof( float ) )] };
			control.Style.Width = 300;
			control.Style.Height = 100;
			UiTesting.Frame( surface );
			control.CaretOffset = control.Source.Length;
			control.FocusEditor();
			UiTesting.Frame( surface );
			control.ShowCompletions();
			UiTesting.Frame( surface );
			control.Tick();
			Assert.AreEqual( 1, host.Shown.Count, $"focus={control.Entry.HasFocus} completion={control.CompletionVisible} entry={control.Entry.Box.Rect} root={surface.Root.Box.Rect}" );
			var popup = host.Shown[0];
			Assert.AreEqual( host.Window, popup.Parent );
			Assert.IsNotNull( popup.AnchorRect );
			Assert.IsFalse( popup.TakesKeyboardFocus );
			Assert.IsFalse( popup.IgnoresInput );
			Assert.IsTrue( control.Entry.HasFocus );
			control.Entry.OnKeyTyped( 'e' );
			control.ShowCompletions();
			Assert.AreSame( popup, host.Shown[0] );
			Assert.AreEqual( "spe", control.Source );
			popup.Delete( true );
			Assert.IsFalse( control.CompletionVisible );
			control.ShowCompletions();
			UiTesting.Frame( surface );
			control.Tick();
			Assert.AreEqual( 1, host.Shown.Count, $"focus={control.Entry.HasFocus} completion={control.CompletionVisible} entry={control.Entry.Box.Rect} root={surface.Root.Box.Rect}" );
			control.Delete( true );
			Assert.AreEqual( 0, host.Shown.Count );
		}
		finally
		{
			control?.Delete( true );
			GlobalContext.Current = previous;
			TextBlock.ui_rendertext = renderText;
		}
	}

}
