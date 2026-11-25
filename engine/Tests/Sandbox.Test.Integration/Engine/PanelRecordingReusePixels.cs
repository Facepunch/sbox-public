using Sandbox.Rendering;
using Sandbox.UI;
using System;

namespace EngineTests;

/// <summary>
/// A kept root recording, and subtrees replayed inside a new one, draw the same pixels as recording afresh.
/// </summary>
[TestClass]
public class PanelRecordingReusePixelsTest
{
	static readonly Rect Bounds = new( 0, 0, 256, 256 );

	[TestInitialize]
	public void RequireVulkan()
	{
		TestGraphics.Require();
	}

	[TestCleanup]
	public void Cleanup()
	{
		PanelRecording.Enabled = true;
	}

	/// <summary>Draws something new every frame, so its root records every frame and the rest replays inside it.</summary>
	sealed class Ticker : Panel
	{
		public int Frame;

		public override void OnDraw( Painter painter )
		{
			painter.Fill = new Color( (Frame++ % 4) / 4f, 0, 0 );
			painter.Rect( new Rect( 0, 0, 8, 8 ) );
		}
	}

	static Panel Child( Panel parent, string style )
	{
		var panel = parent.AddChild<Panel>();
		panel.Style.Set( style );
		return panel;
	}

	static Label Card( Panel parent, string style )
	{
		var card = Child( parent, "width: 110px; height: 110px; margin: 4px; flex-direction: column; overflow: hidden; border-radius: 10px; box-shadow: 3px 3px 6px black; " + style );
		Child( card, "width: 60px; height: 20px; background: linear-gradient( to right, red, blue ); border: 2px solid white;" );
		var label = card.AddChild<Label>();
		label.Text = "Replay";
		label.Style.Set( "font-size: 14px; color: white;" );
		var image = card.AddChild<Image>();
		image.Texture = Texture.White;
		image.Style.Set( "width: 16px; height: 16px; background-tint: green;" );
		var row = Child( card, "flex-direction: row; transform: rotate( 8deg );" );
		Child( row, "width: 16px; height: 16px; background-color: yellow; border-radius: 50%;" );
		Child( row, "width: 16px; height: 16px; background-color: cyan; box-shadow: inset 2px 2px 3px black;" );
		return label;
	}

	static RootPanel Scene( UISystem system, bool ticker, out Label label )
	{
		var root = new RootPanel( system ) { PanelBounds = Bounds };
		root.Style.Set( "flex-wrap: wrap; background-color: #202830;" );
		if ( ticker ) root.AddChild<Ticker>().Style.Set( "position: absolute; left: 0px; top: 0px; width: 8px; height: 8px;" );
		label = Card( root, "background-color: #405060;" );
		Card( root, "background-color: #605040; filter: blur( 1px );" );
		Card( root, "background-color: rgba( 255, 255, 255, 0.2 ); backdrop-filter: blur( 4px );" );
		Card( root, "background-color: #304030; opacity: 0.7;" );
		return root;
	}

	static byte[] Render( RootPanel root, bool reuse )
	{
		PanelRecording.Enabled = reuse;
		root.Layout();
		root.BuildCommandList();
		using var target = Texture.CreateRenderTarget().WithSize( 256, 256 ).Create();
		target.Flags |= TextureFlags.PremultipliedAlpha;
		using ( var scope = Graphics.Scope.Create() )
		{
			scope.Attributes.Set( "UIGammaOutput", true );
			scope.Attributes.SetCombo( "D_NO_ZTEST", 1 );
			Graphics.RenderTarget = RenderTarget.From( target );
			Graphics.Clear( Color.Transparent, true, false, false );
			root.Render();
		}
		using var bitmap = target.GetBitmap();
		return bitmap.GetBuffer().ToArray();
	}

	static void AssertSame( byte[] expected, byte[] actual, string what )
	{
		int differing = 0, max = 0;
		for ( int i = 0; i < expected.Length; i++ )
		{
			var d = Math.Abs( expected[i] - actual[i] );
			if ( d == 0 ) continue;
			differing++;
			max = Math.Max( max, d );
		}

		Assert.AreEqual( 0, differing, $"{what}: {differing} bytes differ, by up to {max}" );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ReplayDrawsWhatRecordingDraws( bool ticker )
	{
		var system = new UISystem();
		var root = Scene( system, ticker, out var label );
		try
		{
			// The ticker still records its root every frame, but draws the same thing on each side
			Ticker tick = null;
			foreach ( var child in root.Children ) tick ??= child as Ticker;
			byte[] Frame( bool reuse )
			{
				if ( tick is not null ) tick.Frame = 0;
				return Render( root, reuse );
			}

			byte[] kept = null;
			for ( int frame = 0; frame < 3; frame++ ) kept = Frame( reuse: true );
			Assert.IsTrue( ticker ? root.SubtreeReplays > 0 : root.RecordingReplays > 0, "nothing was replayed" );
			AssertSame( Frame( reuse: false ), kept, "unchanged" );

			// Replayed from a fresh recording, then from a replay
			AssertSame( Frame( reuse: false ), Frame( reuse: true ), "replayed once" );
			var twice = Frame( reuse: true );
			AssertSame( Frame( reuse: false ), twice, "replayed twice" );

			Frame( reuse: true );
			label.Text = "Changed";
			var changed = Frame( reuse: true );
			AssertSame( Frame( reuse: false ), changed, "after a change" );
		}
		finally { system.Clear(); }
	}
}
