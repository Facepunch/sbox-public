using Borealis.Source2;

namespace BorealisGpuTests;

/// <summary>
/// The memory content streams through is kept between uses, shared by reads of every size, and let go once nothing has
/// streamed for a while (a map has gone): up to 128 MiB stayed at the menu after a map.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ContentBuffersTests
{
	[TestMethod]
	public void KeptArraysGoOnceNothingStreams()
	{
		ContentBuffers.Clear();
		ContentBuffers.Return( ContentBuffers.Rent( 1 << 20 ) );
		Assert.IsTrue( ContentBuffers.KeptBytes >= 1 << 20, "a returned piece is kept for the next rent" );

		ContentBuffers.TrimIdle( 3600 );
		Assert.IsTrue( ContentBuffers.KeptBytes >= 1 << 20, "kept while content streamed lately" );

		ContentBuffers.TrimIdle( 0 );
		Assert.AreEqual( 0L, ContentBuffers.KeptBytes, "let go once nothing has streamed for the idle time" );
	}

	/// <summary>
	/// Reads of one size given back serve reads of others: a texture's file, its mips and the next texture's of other sizes
	/// came from one slab, where size classes of their own made and let go hundreds of megabytes over a map's load.
	/// </summary>
	[TestMethod]
	public void MixedSizesShareOneSlab()
	{
		ContentBuffers.Clear();
		try
		{
			var made = ContentBuffers.MadeBytes;
			int[][] rounds = [[5 << 20, 4 << 20, 1 << 20, 256 << 10, 64 << 10, 16 << 10], [8 << 20, 2 << 20, 2 << 20, 3 << 20], [6 << 20, 100, 70 << 10, 3 << 20], [16 << 20]];
			foreach ( var sizes in rounds )
			{
				var pieces = sizes.Select( ContentBuffers.Rent ).ToArray();
				foreach ( var piece in pieces ) Assert.IsTrue( piece.Span.Length == piece.Length && piece.Capacity >= piece.Length );
				foreach ( var piece in pieces ) ContentBuffers.Return( piece );
			}

			Assert.AreEqual( (long)ContentBuffers.SlabBytes, ContentBuffers.MadeBytes - made, "every round from the one slab" );
			Assert.AreEqual( (long)ContentBuffers.SlabBytes, ContentBuffers.KeptBytes, "all of it free again, its halves joined" );
		}
		finally
		{
			ContentBuffers.Clear();
		}
	}

	/// <summary>Pieces out at once never overlap.</summary>
	[TestMethod]
	public void PiecesOutAtOnceDontOverlap()
	{
		ContentBuffers.Clear();
		try
		{
			var random = new Random( 7 );
			var pieces = new List<ContentBuffer>();
			for ( int i = 0; i < 400; i++ )
			{
				if ( pieces.Count > 0 && random.Next( 3 ) == 0 )
				{
					var at = random.Next( pieces.Count );
					ContentBuffers.Return( pieces[at] );
					pieces.RemoveAt( at );
				}

				var piece = ContentBuffers.Rent( random.Next( 1, 3 << 20 ) );
				piece.Span.Fill( (byte)i );
				pieces.Add( piece );
				foreach ( var other in pieces )
				{
					if ( ReferenceEquals( other.Array, piece.Array ) && other.Offset != piece.Offset )
						Assert.IsTrue( other.Offset + other.Capacity <= piece.Offset || piece.Offset + piece.Capacity <= other.Offset, "pieces of one slab overlap" );
				}
			}

			foreach ( var piece in pieces ) ContentBuffers.Return( piece );
			Assert.AreEqual( ContentBuffers.HeldBytes, ContentBuffers.KeptBytes, "everything back" );
		}
		finally
		{
			ContentBuffers.Clear();
		}
	}

	/// <summary>
	/// While content streams it holds what a load reads at once; once nothing has been rented for a while it lets go down
	/// to what it keeps between uses, and past the busy room pieces are made and let go.
	/// </summary>
	[TestMethod]
	public void BusyRoomGoesOnceQuiet()
	{
		var (max, busy, quiet) = (ContentBuffers.MaxKeptBytes, ContentBuffers.BusyKeptBytes, ContentBuffers.QuietSeconds);
		ContentBuffers.Clear();
		try
		{
			ContentBuffers.MaxKeptBytes = ContentBuffers.SlabBytes;
			ContentBuffers.BusyKeptBytes = 3L * ContentBuffers.SlabBytes;
			var pieces = Enumerable.Range( 0, 4 ).Select( _ => ContentBuffers.Rent( ContentBuffers.SlabBytes ) ).ToArray();
			Assert.AreEqual( 3L * ContentBuffers.SlabBytes, ContentBuffers.HeldBytes, "three slabs, the fourth piece made past the room" );
			foreach ( var piece in pieces ) ContentBuffers.Return( piece );
			Assert.AreEqual( 3L * ContentBuffers.SlabBytes, ContentBuffers.HeldBytes, "the piece past the room let go" );

			ContentBuffers.TrimIdle( 3600 );
			Assert.AreEqual( 3L * ContentBuffers.SlabBytes, ContentBuffers.HeldBytes, "held while rented lately" );

			ContentBuffers.QuietSeconds = 0;
			ContentBuffers.TrimIdle( 3600 );
			Assert.AreEqual( (long)ContentBuffers.SlabBytes, ContentBuffers.HeldBytes, "down to what it keeps once quiet" );
		}
		finally
		{
			(ContentBuffers.MaxKeptBytes, ContentBuffers.BusyKeptBytes, ContentBuffers.QuietSeconds) = (max, busy, quiet);
			ContentBuffers.Clear();
		}
	}
}
