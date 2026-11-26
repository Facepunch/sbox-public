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
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => SoundFile.FromOpusPackets( path, 2, [silence], 44100 ) );
		var result = SoundFile.FromOpusPackets( path, 2, [silence, silence] );
		Assert.IsNotNull( result );
		result.Preload();
		Assert.AreEqual( 2, result.Channels );
		Assert.AreEqual( 48000, result.Rate );
		Assert.AreEqual( 0.04f, result.Duration, 0.00001f );
	}

	[DataTestMethod]
	[DataRow( 8000 )]
	[DataRow( 12000 )]
	[DataRow( 16000 )]
	[DataRow( 24000 )]
	[DataRow( 48000 )]
	public void OpusOutputRatePreservesDuration( int rate )
	{
		ReadOnlyMemory<byte> silence = new byte[] { 0xf8, 0xff, 0xfe };
		var sound = SoundFile.FromOpusPackets( $"tests/procedural_opus_{rate}.vsnd", 1, [silence], rate );
		sound.Preload();
		Assert.AreEqual( rate, sound.Rate );
		Assert.AreEqual( 0.02f, sound.Duration, 0.00001f );
	}

	[TestMethod]
	public void EventLimitCountsSerializedBytes()
	{
		Game.TypeLibrary.AddAssembly( typeof( ProceduralTestEvent ).Assembly, false );
		var evt = new ProceduralTestEvent { Message = "" };
		var empty = new System.Buffers.ArrayBufferWriter<byte>();
		using ( var writer = new System.Text.Json.Utf8JsonWriter( empty ) )
			Json.Serialize( writer, evt, evt.GetType() );

		var builder = new ClipBuilder();
		evt.Message = new string( 'x', 64 * 1024 - empty.WrittenCount );
		builder.AddEvent( 0, evt );
		evt.Message += "x";
		Assert.ThrowsException<ArgumentException>( () => builder.AddEvent( 0, evt ) );

		var name = evt.GetType().FullName;
		var json = "{\"Message\":\"" + new string( 'x', AnimationEvent.MaxJsonBytes - "{\"Message\":\"\"}".Length ) + "\"}";
		Assert.IsNotNull( AnimationEvent.Deserialize<ProceduralTestEvent>( name, json ) );
		Assert.ThrowsException<InvalidOperationException>( () => AnimationEvent.Deserialize<ProceduralTestEvent>( name, json + " " ) );
		Assert.ThrowsException<InvalidOperationException>( () => AnimationEvent.Deserialize<ProceduralTestEvent>( name, json.Replace( 'x', 'é' ) ) );
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
