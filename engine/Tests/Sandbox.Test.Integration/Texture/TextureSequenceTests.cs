using System;
using System.Linq;

namespace TextureTests;

[TestClass]
public class TextureSequenceTests
{
	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void SequencesHaveIndependentGpuMetadata( bool anonymous )
	{
		using var first = Create( 1, anonymous );
		using var second = Create( 2, anonymous );
		using var plain = Texture.Create( 4, 4 ).WithData( new byte[64] ).WithStaticUsage().Finish();
		Assert.AreEqual( 1, first.SequenceCount );
		Assert.AreEqual( 2, second.SequenceCount );
		Assert.AreEqual( 1f, first.SequenceData.w );
		Assert.AreEqual( 2f, second.SequenceData.w );
		Assert.AreNotEqual( first.SequenceData.x, second.SequenceData.x );
		Assert.AreEqual( Vector4.Zero, plain.SequenceData );
		Assert.AreSame( first, Json.Deserialize<Texture>( Json.Serialize( first ) ) );
	}

	static Texture Create( int count, bool anonymous )
	{
		var builder = Texture.Create( 4, 4 )
			.WithData( new byte[64] ).WithStaticUsage()
			.WithName( $"runtime/sequence_test_{Guid.NewGuid():N}.vtex" ).WithAnonymous( anonymous );
		for ( var i = 0; i < count; i++ )
		{
			builder = builder.AddSequence( [new Rect( i / (float)count, 0, 1 / (float)count, 1 )], 1 );
		}

		return builder.Finish();
	}
}
