using System;
using Sandbox.Rendering;

namespace UITests;

[TestClass]
public class PainterLifetimeTest
{
	sealed class CountingContext : Painter.Context
	{
		internal int Submissions { get; private set; }

		internal CountingContext() : base( new CommandList() ) { }

		protected override void OnEnd()
		{
			base.OnEnd();
			Submissions++;
		}
	}

	[TestMethod]
	public void CopiesShareContext()
	{
		var context = new CountingContext();
		var painter = context.Begin( new Rect( 0, 0, 100, 100 ) );
		var copy = painter;
		try
		{
			copy.Fill = Color.Red;
			Assert.AreEqual( Fill.Solid( Color.Red ), painter.Fill );
			using ( var scope = painter.Scope() ) { copy.Fill = Color.Blue; }
			Assert.AreEqual( Fill.Solid( Color.Red ), painter.Fill );
		}
		finally { context.End(); }
		context.End();
		Assert.AreEqual( 1, context.Submissions );
		AssertDisposed( painter );
		AssertDisposed( copy );
	}

	[TestMethod]
	public void ContextsHaveIndependentState()
	{
		var context = new Painter.Context( new CommandList() );
		var painter = context.Begin( default );
		try
		{
			painter.Fill = Color.Green;
			var other = new Painter.Context( new CommandList() );
			var otherPainter = other.Begin( default );
			try { otherPainter.Fill = Color.Blue; }
			finally { other.End(); }
			Assert.AreEqual( Fill.Solid( Color.Green ), painter.Fill );
			var replacement = context.Begin( default );
			AssertDisposed( painter );
			Assert.AreEqual( Fill.None, replacement.Fill );
		}
		finally { context.End(); }
	}

	[TestMethod]
	public void PainterIsARefStructWithDispose()
	{
		Assert.IsTrue( typeof( Painter ).IsByRefLike );
		Assert.IsNotNull( typeof( Painter ).GetMethod( "Dispose" ) );
	}

	[TestMethod]
	public void LayerNamesAreReusedAcrossRecordings()
	{
		var first = new PainterBatcher( new CommandList() );
		var second = new PainterBatcher( new CommandList() );
		var names = new string[32];
		for ( int i = 0; i < names.Length; i++ )
		{
			names[i] = first.NextLayerName();
			Assert.AreSame( names[i], second.NextLayerName() );
		}
		Assert.AreEqual( names.Length, names.Distinct().Count() );

		first.Clear();
		for ( int i = 0; i < names.Length; i++ )
			Assert.AreSame( names[i], first.NextLayerName() );
		first.Clear();
		var checkpoint = first.GetCheckpoint();
		for ( int recording = 0; recording < 2; recording++ )
		{
			Assert.AreSame( names[0], first.NextLayerName() );
			first.Rewind( checkpoint );
		}
	}

	[TestMethod]
	public void CopiesShareRecordingOwnership()
	{
		var context = new CountingContext();
		var painter = context.Begin( default );
		var copy = painter;
		painter.Dispose();
		copy.Dispose();
		Assert.AreEqual( 1, context.Submissions );
		using var next = context.Begin( default );
		copy.Dispose();
		AssertDisposed( copy );
		next.Fill = Color.Green;
		Assert.AreEqual( 1, context.Submissions );
	}

	[TestMethod]
	public void PanelOwnsRecording()
	{
		var context = new Painter.Context( new CommandList() );
		context.Begin( default );
		var painter = context.Painter;
		painter.Dispose();
		painter.Fill = Color.Red;
		Assert.AreEqual( Fill.Solid( Color.Red ), painter.Fill );
		context.End();
	}

	static void AssertDisposed( Painter painter )
	{
		try { _ = painter.Bounds; }
		catch ( ObjectDisposedException ) { return; }
		Assert.Fail( "An ended Painter must reject access." );
	}
}
