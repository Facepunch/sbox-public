using Sandbox.Animation;
using System;
using System.Linq;

namespace ResourceTests;

[Expose]
public sealed class ProceduralTestEvent : AnimationEvent
{
	public string Message { get; set; }
}

[TestClass]
public class ProceduralMediaTests
{
	[TestMethod]
	public void DecodeOpusAndRejectInvalidInput()
	{
		const string path = "tests/procedural_opus.vsnd";
		ReadOnlyMemory<byte> silence = new byte[] { 0xf8, 0xff, 0xfe };
		Assert.ThrowsException<ArgumentException>( () => SoundFile.FromOpusPackets( path, 1, [ReadOnlyMemory<byte>.Empty] ) );
		Assert.ThrowsException<ArgumentException>( () => SoundFile.FromOpusPackets( path, 1, [new byte[65536]] ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => SoundFile.FromOpusPackets( path, 1, new ReadOnlyMemory<byte>[16385] ) );
		Assert.ThrowsException<ArgumentException>( () => SoundFile.FromOpusPackets( path, 1, [new byte[] { 3 }] ) );
		Assert.ThrowsException<ArgumentException>( () => SoundFile.FromOpusPackets( path, 2, Enumerable.Repeat( silence, 9000 ).ToArray() ) );
		var result = SoundFile.FromOpusPackets( path, 2, [silence, silence] );
		Assert.IsNotNull( result );
		result.Preload();
		Assert.AreEqual( 2, result.Channels );
		Assert.AreEqual( 0.04f, result.Duration, 0.00001f );
	}

	[TestMethod]
	public void AnimationEventsRespectBudgetsAndRoundTrip()
	{
		Game.TypeLibrary.AddAssembly( typeof( ProceduralTestEvent ).Assembly, false );
		var large = new ProceduralTestEvent { Message = new string( 'x', 4096 ) };
		var limited = new ClipBuilder();
		Assert.ThrowsException<InvalidOperationException>( () =>
		{
			for ( int i = 0; i < 260; i++ ) limited.AddEvent( 0f, large );
		} );
		var frame = new[] { Transform.Zero };
		var skeleton = new SkeletonBuilder();
		skeleton.AddBone( "root", Transform.Zero );
		using var rig = skeleton.Create( "tests/procedural_events.skel" );
		var builder = new ClipBuilder { Skeleton = rig };
		builder.AddFrame( frame ).AddFrame( frame );
		var evt = new ProceduralTestEvent { Message = new string( 'x', 65537 ) };
		Assert.ThrowsException<ArgumentException>( () => builder.AddEvent( 0, evt ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => builder.AddEvent( float.NaN, evt ) );
		evt.Message = "footstep";
		builder.AddEvent( 0.5f, evt );
		using var clip = builder.Create( "tests/procedural_events.anim" );
		Assert.AreEqual( "footstep", clip.GetEvents<ProceduralTestEvent>().Single().Data.Message );
		for ( int i = 1; i < 1024; i++ ) builder.AddEvent( 0, evt );
		Assert.ThrowsException<InvalidOperationException>( () => builder.AddEvent( 0, evt ) );
	}
}
