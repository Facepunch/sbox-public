using Sandbox.UI;
using System;

namespace EngineTests;

[TestClass]
public class BorderStyleRenderingTest
{
	[TestMethod]
	public void StyleChangesUpdateLayoutAndDescriptors()
	{
		var root = new RootPanel { RenderedManually = true, PanelBounds = new Rect( 0, 0, 200, 200 ) };
		var panel = root.AddChild<Panel>();
		panel.Style.Width = 100;
		panel.Style.Height = 80;
		panel.Style.BorderWidth = 8;
		panel.Style.BorderColor = Color.Red;
		panel.Style.BackgroundColor = Color.Blue;
		try
		{
			foreach ( var style in new[] { BorderStyle.Solid, BorderStyle.Dashed, BorderStyle.None, BorderStyle.Dotted, BorderStyle.Hidden, BorderStyle.Double } )
			{
				panel.Style.BorderStyle = style;
				root.Layout();
				var frame = PanelDrawSnapshot.Build( root );
				float width = style is BorderStyle.None or BorderStyle.Hidden ? 0 : 8;
				Assert.AreEqual( width, panel.Box.Border.Left );
				Assert.AreEqual( width, panel.Box.Border.Top );
				Assert.AreEqual( 8f, panel.ComputedStyle.BorderLeftWidth.Value.Value );
				var box = frame.Instances.Single();
				Assert.AreEqual( new Vector4( width ), box.BorderSize );
				Assert.AreEqual( (int)style, box.BorderStyle );
			}
		}
		finally { root.Delete( true ); }
	}

	[TestMethod]
	public void BorderStylesParticipateInTheCascade()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 200, 200 ) };
		var panel = root.Add.Panel( "child" );
		try
		{
			root.StyleSheet.Parse( "RootPanel { border: 8px dashed red; } .child { width: 100px; height: 80px; border: inherit; }" );
			root.Layout();
			Assert.AreEqual( BorderStyle.Dashed, panel.ComputedStyle.BorderStyle );
			Assert.AreEqual( 8f, panel.Box.Border.Top );
			Assert.AreEqual( 8f, panel.Box.Border.Right );
			panel.Style.Set( "border-style", "initial" );
			root.Layout();
			Assert.AreEqual( BorderStyle.Solid, panel.ComputedStyle.BorderStyle );
			Assert.AreEqual( 8f, panel.Box.Border.Right );
			panel.Style.Set( "border-left", "unset" );
			root.Layout();
			Assert.AreEqual( BorderStyle.Solid, panel.ComputedStyle.BorderStyle );
			Assert.AreEqual( 0f, panel.Box.Border.Left );
		}
		finally { root.Delete( true ); }
	}
}
