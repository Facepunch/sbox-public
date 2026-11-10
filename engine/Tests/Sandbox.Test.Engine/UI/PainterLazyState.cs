using System;
using System.Runtime.CompilerServices;
using Sandbox.Rendering;

namespace UITests;

[TestClass]
public class PainterLazyStateTest
{
	[TestMethod]
	public void ResetRestoresDrawingDefaults()
	{
		var context = new Painter.Context( new CommandList() );
		using var painter = context.Begin( new Rect( 0, 0, 100, 100 ) );
		painter.Fill = Color.Red;
		painter.Stroke = Stroke.Solid( Color.Blue, 4 );
		painter.Transform = Matrix.CreateTranslation( new Vector3( 10, 20, 0 ) );
		painter.Opacity = 0.25f;
		painter.TextStyle = new TextStyle { FontName = "Test Font", FontSize = 30 };
		painter.Clip( new Rect( 10, 10, 50, 50 ) );
		context.ResetDrawingState( BlendMode.Multiply );

		Assert.AreEqual( Fill.None, painter.Fill );
		Assert.AreEqual( Stroke.None, painter.Stroke );
		Assert.AreEqual( Matrix.Identity, painter.Transform );
		Assert.AreEqual( 1f, painter.Opacity );
		Assert.AreEqual( TextStyle.Default, painter.TextStyle );
		Assert.AreEqual( BlendMode.Multiply, painter.BlendMode );
		Assert.AreEqual( -1, context.State.ClipIndex );
	}

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void RecordingReleasesDrawingState( bool invalidate, bool resetCommands )
	{
		var commands = new CommandList();
		var context = new Painter.Context( commands );
		using var painter = context.Begin( default );
		var reference = SetTemporaryFont( painter );
		if ( invalidate ) context.ResetDrawingState( BlendMode.Normal );

		if ( resetCommands ) commands.Reset();
		else painter.Dispose();

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		Assert.IsFalse( reference.IsAlive );
		GC.KeepAlive( context );
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	static WeakReference SetTemporaryFont( Painter painter )
	{
		var font = new string( 'x', 128 );
		painter.TextStyle = new TextStyle { FontName = font };
		return new WeakReference( font );
	}
}
