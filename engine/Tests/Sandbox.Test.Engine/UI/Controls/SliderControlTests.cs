using Sandbox.UI;
using System.Collections.Generic;
using static UITests.UiTesting;

namespace UITests.Controls;

/// <summary>
/// Exercises ranged property editors used by Sandbox Mode.
/// </summary>
[TestClass]
public class SliderControlTests
{
	/// <summary>
	/// Server settings preview snapped edits and commit once, including when the control is rebound.
	/// </summary>
	[TestMethod]
	public void RangedPropertyBinding()
	{
		ThreadSafe.MarkMainThread();
		var root = CreateRoot();
		try
		{
			var writes = new List<int>();
			var property = Game.TypeLibrary.CreateProperty<int>( "Limit", () => 2, writes.Add,
				[new RangeAttribute( 0, 10 ), new StepAttribute( 2 ), new ApplyOnReleaseAttribute()] );
			var slider = (SliderControl)root.AddChild( BaseControl.CreateFor( property ) );
			var entry = slider.Descendants.OfType<NumberEntry>().Single();
			Assert.IsTrue( entry.WholeNumbers );

			var track = slider.Descendants.Single( p => p.HasClass( "track" ) );
			track.Box.Rect = new Rect( 0, 0, 100, 6 );
			slider.Switch( PseudoClass.Active, true );
			root.MousePos = new Vector2( 40, 0 );
			slider.DispatchEventImmediate( new MousePanelEvent( "onmousedown", slider, "mouseleft" ) );
			slider.Tick();
			root.MousePos = new Vector2( 79, 0 );
			slider.DispatchEventImmediate( new MousePanelEvent( "onmousemove", slider, "mouseleft" ) );
			slider.Tick();
			Assert.AreEqual( 8f, slider.Value );
			Assert.AreEqual( 0, writes.Count );

			slider.DispatchEventImmediate( new MousePanelEvent( "onmouseup", slider, "mouseleft" ) );
			slider.Switch( PseudoClass.Active, false );
			CollectionAssert.AreEqual( new[] { 8 }, writes );

			entry.OnTextEdited( "100" );
			double temperature = 0.5;
			var light = Game.TypeLibrary.CreateProperty<double>( "Temperature", () => temperature, v => temperature = v,
				[new RangeAttribute( 0, 1 ), new StepAttribute( 0.1f ), new SliderGradientAttribute( "#f00", "#00f" )] );
			Assert.IsInstanceOfType<SliderControl>( root.AddChild( BaseControl.CreateFor( light ) ) );
			slider.Property = light;
			CollectionAssert.AreEqual( new[] { 8, 10 }, writes );
			Assert.IsFalse( entry.WholeNumbers );
			Assert.AreEqual( 2, track.Style.BackgroundGradient.ColorOffsets.Length );
			entry.OnTextEdited( "0.72" );
			Assert.AreEqual( 0.7, temperature, 0.00001 );

			slider.Property = property;
			Assert.IsTrue( track.Style.BackgroundGradient.ColorOffsets.IsDefaultOrEmpty );
		}
		finally
		{
			root.Delete( true );
		}
	}
}
