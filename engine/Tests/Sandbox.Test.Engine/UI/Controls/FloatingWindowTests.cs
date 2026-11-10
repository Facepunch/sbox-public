using Microsoft.AspNetCore.Components;
using Sandbox.UI;
using static UITests.UiTesting;

namespace UITests.Controls;

/// <summary>
/// Checks that the engine window renders on its own and retains caller-owned content across updates.
/// </summary>
[TestClass]
[DoNotParallelize]
public class FloatingWindowTests
{
	bool previousRenderText;
	UISurface surface;
	FloatingWindow window;

	/// <summary>
	/// Create a window without loading any addon styles or components.
	/// </summary>
	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();
		previousRenderText = DisableTextRendering();
		surface = new UISurface { Size = new Vector2( 1000, 800 ), DpiScale = 1 };
		window = new FloatingWindow { Parent = surface.Root, Title = "Test window", Icon = "🪟", ShowCloseButton = true, ShowMinimizeButton = true };
	}

	/// <summary>
	/// Release the surface and restore the text rendering setting.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		try
		{
			surface.Dispose();
		}
		finally
		{
			TextBlock.ui_rendertext = previousRenderText;
		}
	}

	void Frame()
	{
		// The engine test tier has no render device for the window's background gradients.
		for ( var i = 0; i < 3; i++ ) surface.System.TickPanels();
		surface.System.RunDeferredDeletion();
	}

	/// <summary>
	/// A window with no child fragment still has working, styled titlebar controls.
	/// </summary>
	[TestMethod]
	public void StandaloneWindowRendersChromeAndRoutesCloseRequest()
	{
		var closed = false;
		window.OnCloseRequested = () => closed = true;
		Frame();

		var titlebar = window.Children.OfType<FloatingWindowTitleBar>().Single();
		Assert.AreEqual( "Test window", titlebar.Children.OfType<Label>().Single( x => x.HasClass( "floating-window-title" ) ).Text );
		Assert.AreEqual( "🪟", window.Children.OfType<Button>().Single().Text );
		Assert.IsTrue( window.AllStyleSheets.Any( x => x.FileName == "inline:floatingwindow" ) );

		Click( surface.Root, titlebar.Children.OfType<Button>().Single( x => x.Icon == "close" ) );
		Assert.IsTrue( closed );
		Assert.IsTrue( window.IsValid );
	}

	/// <summary>
	/// Updating the chrome preserves an embedded control and its edited state.
	/// </summary>
	[TestMethod]
	public void UpdatingChromeRetainsContentAndClearsTitle()
	{
		window.ChildContent = tree =>
		{
			tree.OpenElement<TextEntry>( 0 );
			tree.AddAttribute( 1, "text", "Initial text" );
			tree.CloseElement();
		};
		Frame();

		var content = window.Children.Single( x => x.HasClass( "floating-window-content" ) );
		var entry = content.Children.OfType<TextEntry>().Single();
		entry.Text = "Edited text";
		window.Title = "";
		window.Icon = "🎉";
		window.ShowMinimizeButton = false;
		Frame();

		Assert.AreSame( content, window.Children.Single( x => x.HasClass( "floating-window-content" ) ) );
		Assert.AreSame( entry, content.Children.OfType<TextEntry>().Single() );
		Assert.AreEqual( "Edited text", entry.Text );
		Assert.AreEqual( "🎉", window.Children.OfType<Button>().Single().Text );
		var titlebar = window.Children.OfType<FloatingWindowTitleBar>().Single();
		Assert.AreEqual( "", titlebar.Children.OfType<Label>().Single( x => x.HasClass( "floating-window-title" ) ).Text );
		Assert.AreEqual( 1, titlebar.Children.OfType<Button>().Count() );
	}
}
