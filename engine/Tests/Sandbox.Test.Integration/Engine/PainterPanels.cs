using Sandbox.UI;
using System;

namespace EngineTests;

[TestClass]
public class PainterPanelTest
{
	[TestMethod]
	public void TextClippedBackgroundDrawsTheBorderOnce()
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Requires graphics to rasterize the text mask." );

		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var panel = root.AddChild<Panel>();
		panel.Style.Set( "position: absolute; left: 40px; top: 30px; width: 200px; height: 80px; background-color: red; border: 3px solid green; background-clip: text;" );
		var label = panel.AddChild<Label>();
		label.Style.Set( "font-family: Arial; font-size: 24px;" );
		label.Text = "Clipped background";
		try
		{
			root.Layout();
			var frame = PanelDrawSnapshot.Build( root );
			var border = frame.Instances.Single( x => x.BorderSize != Vector4.Zero );
			Assert.AreEqual( new Vector4( 3 ), border.BorderSize );
			Assert.AreEqual( 0, border.TextMaskIndex );
			var fill = frame.Instances.Single( x => x.BackgroundClip == (int)BackgroundClip.Text );
			Assert.AreEqual( Color.Red, fill.Color );
			Assert.AreEqual( Vector4.Zero, fill.BorderSize );
			Assert.IsTrue( fill.TextMaskIndex > 0 );
			Assert.AreEqual( border.Rect, fill.Rect );
			Assert.AreEqual( border.TransformIndex, fill.TransformIndex );
		}
		finally
		{
			root.Delete( true );
		}
	}

	sealed class InheritedLegacyPanel : LegacyPanel
	{
	}

	sealed class HiddenLegacyPanel : LegacyPanel
	{
		public new void OnDraw() => throw new InvalidOperationException( "This method is not a drawing override." );
	}

	sealed class NativeOnlyPanel : Panel, IPanelDraw
	{
		internal int DrawCalls;

		void IPanelDraw.Draw( Sandbox.Rendering.CommandList commands )
		{
			DrawCalls++;
			using var painter = Painter.Begin( commands, Box.Rect );
			painter.Fill = Color.Green;
			painter.Rect( Box.Rect );
		}
	}

	[TestMethod]
	public void InheritedAndNativeCallbacksRunOnEveryBuild()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var inherited = root.AddChild<InheritedLegacyPanel>();
		var hidden = root.AddChild<HiddenLegacyPanel>();
		var native = root.AddChild<NativeOnlyPanel>();
		foreach ( var panel in root.Children )
		{
			panel.Style.Set( "width: 80px; height: 40px;" );
		}

		try
		{
			root.Layout();
			for ( int i = 1; i <= 3; i++ )
			{
				if ( i == 3 ) root.OnHotloaded();
				var frame = PanelDrawSnapshot.Build( root );
				Assert.AreEqual( i, inherited.LegacyCalls );
				Assert.AreEqual( i, hidden.LegacyCalls );
				Assert.AreEqual( i, native.DrawCalls );
				Assert.AreEqual( 2, frame.Instances.Count( x => x.Color == Color.Red ) );
				Assert.AreEqual( 1, frame.Instances.Count( x => x.Color == Color.Green ) );
			}
		}
		finally
		{
			root.Delete( true );
		}
	}

	[TestMethod]
	public void PlainPanelsDrawInlineText()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var paragraph = root.AddChild<Panel>();
		paragraph.Style.Set( "display: block; width: 200px; font-family: Arial; font-size: 20px;" );
		var label = paragraph.AddChild<Label>();
		label.Style.Set( "display: inline;" );
		label.Text = "Inline text";

		try
		{
			root.Layout();
			Assert.IsNotNull( paragraph.LayoutTree.InlineContext );
			Assert.AreSame( paragraph.LayoutTree.InlineContext, label.LayoutTree.InlineContext );
			var frame = PanelDrawSnapshot.Build( root );
			Assert.IsTrue( frame.Instances.Any( x => x.TextureIndex > 0 ) );
		}
		finally
		{
			root.Delete( true );
		}
	}

	sealed class CaretEntry : TextEntry
	{
		internal Rect CaretRect => Label.GetCaretRect( CaretPosition );
	}

	[TestMethod]
	public void TextEntryCaretDrawing()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var entry = root.AddChild<CaretEntry>();
		entry.Style.Set( "position: absolute; left: 80px; top: 50px; width: 200px; height: 50px; font-size: 16px;" );
		entry.Style.CaretColor = Color.Red;
		entry.Text = "Caret";
		entry.CaretPosition = 2;
		entry.Switch( PseudoClass.Focus, true );

		try
		{
			root.Layout();
			var caret = entry.CaretRect;
			caret.Left = System.MathF.Floor( caret.Left );
			caret.Width = 1;
			var context = Painter.Context.Get( new Sandbox.Rendering.CommandList() );
			using var commands = new PainterTestOutput( context.Batcher );
			using var painter = context.Begin( new Rect( Vector2.Zero, entry.Box.Rect.Size ) );
			context.BaseTransform = Matrix.CreateTranslation( new Vector3( entry.Box.Rect.Position, 0 ) );

			try
			{
				painter.Fill = Color.Blue;
				painter.Stroke = Stroke.Solid( Color.Green, 4 );
				var state = context.State;
				entry.OnDraw( painter );
				var instance = commands.Instances.Single();
				var position = new Vector2( instance.GPU.Rect.x, instance.GPU.Rect.y ) + entry.Box.Rect.Position;
				Assert.AreEqual( caret.Position, position );
				Assert.AreEqual( 1f, instance.GPU.Rect.z );
				Assert.AreEqual( Color.Red, instance.GPU.Color );
				Assert.AreEqual( context.BaseTransform, instance.Transform );
				Assert.AreEqual( state, context.State );
			}
			finally
			{
				context.End();
				context.CommandList.Reset();
				context.Batcher.Dispose();
			}
		}
		finally
		{
			root.Delete( true );
		}
	}

	class LegacyPanel : Panel
	{
		internal int LegacyCalls;
		[Obsolete( "Verifies dispatch to the legacy parameterless callback." )]
		public override void OnDraw()
		{
			LegacyCalls++;
#pragma warning disable CS0618 // Verifies dispatch to the legacy callback.
			Draw.Rect( Box.Rect, Color.Red );
#pragma warning restore CS0618
		}
	}

	sealed class ModernPanel : LegacyPanel
	{
		internal Rect PaintBounds;
		public override void OnDraw( Painter painter )
		{
			PaintBounds = painter.Bounds;
			painter.Fill = Color.Blue;
			painter.Rect( painter.Bounds );
			base.OnDraw( painter ); // Still dispatches the existing virtual parameterless hook.
		}
	}

	sealed class StyledImagePanel : Panel
	{
		internal bool ChangeState;
		internal int DrawCalls;
		public override void OnDraw( Painter painter )
		{
			DrawCalls++;
			if ( ChangeState )
			{
				painter.Opacity = 0;
				painter.Translate( 500, 500 );
				painter.Clip( default );
			}
			DrawTexture( painter, Texture.White, Length.Auto );
		}
	}

	[TestMethod]
	public void CssDrawingIgnoresCallbackState()
	{
		var root = new RootPanel { RenderedManually = true, PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var reference = root.AddChild<StyledImagePanel>();
		var changed = root.AddChild<StyledImagePanel>();
		changed.ChangeState = true;
		foreach ( var panel in new[] { reference, changed } )
		{
			panel.Style.Set( "position", "absolute" );
			panel.Style.Left = 40;
			panel.Style.Top = 30;
			panel.Style.Width = 120;
			panel.Style.Height = 80;
			panel.Style.Opacity = 0.5f;
			panel.Style.BackgroundColor = Color.Red;
			panel.Style.BorderWidth = 4;
			panel.Style.Set( "padding", "8px" );
			panel.Style.Set( "background-clip", "content-box" );
			panel.Style.BorderColor = Color.Green;
			panel.Style.Set( "outline", "3px blue" );
			panel.Style.Set( "box-shadow", "2px 3px 4px black, inset 1px 2px 3px black" );
		}
		try
		{
			root.Layout();
			var frame = PanelDrawSnapshot.Build( root );
			Assert.IsTrue( frame.Instances.Length >= 8, "Both panels draw CSS shadows, backgrounds, images and outlines." );
			Assert.AreEqual( frame.Instances[0], frame.Instances[1], "Sibling shadows match." );
			// Headless textures can have index zero, just like an untextured CSS box.
			var image = frame.Instances.Single( x => x.Mode == 0 && x.BackgroundRect != Vector4.Zero );
			var css = frame.Instances.Where( x => x.Mode != 0 || x.BackgroundRect == Vector4.Zero ).ToArray();
			var half = (css.Length - 2) / 2;
			Assert.IsTrue( half > 0 && css.Length == 2 + half * 2, "Both panels draw CSS after their sibling shadows." );
			var expected = css.Skip( 2 ).Take( half ).ToArray();
			var actual = css.Skip( 2 + half ).ToArray();
			CollectionAssert.AreEqual( expected, actual, "CSS drawing ignores the callback's transform, clipping and opacity." );
			var background = actual.First( x => x.Color.r == 1 && x.TextureIndex == 0 );
			Assert.AreEqual( 0.5f, background.Color.a, 0.001f, "CSS opacity is applied once." );
			Assert.AreEqual( (int)BackgroundClip.ContentBox, image.BackgroundClip );
			Assert.AreEqual( background.BackgroundClipRect, image.BackgroundClipRect );
			Assert.AreEqual( new Vector4( 12 ), image.BackgroundClipRect );
			Assert.AreEqual( 0.5f, image.BackgroundTint.a, 0.001f, "Direct images inherit panel opacity." );
			var next = PanelDrawSnapshot.Build( root );
			CollectionAssert.AreEqual( frame.Instances, next.Instances );
			Assert.AreEqual( 2, changed.DrawCalls );
		}
		finally { root.Delete( true ); }
	}

	[TestMethod]
	public void PanelBoundsAndLegacyCallbacks()
	{
		var root = new RootPanel { RenderedManually = true, PanelBounds = new Rect( 0, 0, 400, 300 ) };
		var child = root.AddChild<ModernPanel>();
		child.Style.Set( "position", "absolute" );
		child.Style.Left = 80;
		child.Style.Top = 50;
		child.Style.Width = 120;
		child.Style.Height = 60;
		try
		{
			root.Layout();

			Assert.AreEqual( 0, child.LegacyCalls, "Layout does not draw." );
			var frame = PanelDrawSnapshot.Build( root );
			Assert.AreEqual( 1, child.LegacyCalls );
			Assert.AreEqual( new Rect( Vector2.Zero, child.Box.Rect.Size ), child.PaintBounds );
			var modern = frame.Instances.Single( x => x.Color == Color.Blue );
			var legacy = frame.Instances.Single( x => x.Color == Color.Red );
			Assert.AreEqual( new Vector4( 0, 0, 120, 60 ), modern.Rect );
			var rect = child.Box.Rect;
			Assert.AreEqual( new Vector4( rect.Left, rect.Top, rect.Width, rect.Height ), legacy.Rect );
			Assert.AreNotEqual( modern.TransformIndex, legacy.TransformIndex );
			root.BuildCommandList();
			Assert.AreEqual( 2, child.LegacyCalls, "Each command-list build invokes both callbacks." );
		}
		finally
		{
			root.Delete( true );
		}
	}
}
