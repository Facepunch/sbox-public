using Borealis.Backend;
using System.Threading;
using System.Threading.Tasks;

namespace BorealisGpuTests;

/// <summary>
/// Draw records' index: each key made once, however many threads ask for it at once, found again after the shards grow,
/// and forgotten with its index page while others' stay.
/// </summary>
[TestClass]
public class DrawRecordIndexTests
{
	static DrawRecordKey Key( int i, int page ) => new( (uint)(i % 7), 1, 2, 0, (uint)i * 3, 3, i % 5, page );

	[TestMethod]
	public void EachKeyIsMadeOnceAndFoundAgain()
	{
		var index = new DrawRecordIndex();
		var next = 0;
		var made = 0;
		const int Keys = 150_000;

		Parallel.For( 0, Keys * 2, new ParallelOptions { MaxDegreeOfParallelism = 8 }, n =>
		{
			var i = n % Keys;
			var value = index.FindOrAdd( Key( i, i % 3 ), 0, _ => (uint)Interlocked.Increment( ref next ), out var added );
			if ( added ) Interlocked.Increment( ref made );
			Assert.AreNotEqual( 0u, value );
		} );

		Assert.AreEqual( Keys, made, "each key made once" );
		Assert.AreEqual( Keys, index.Count );
		var seen = new HashSet<uint>();
		for ( int i = 0; i < Keys; i++ ) Assert.IsTrue( seen.Add( index.Find( Key( i, i % 3 ) ) ), $"key {i} has a record of its own" );

		index.RemovePage( 1 );
		for ( int i = 0; i < Keys; i++ ) Assert.AreEqual( i % 3 == 1, index.Find( Key( i, i % 3 ) ) == 0, $"key {i} on page {i % 3}" );
		Assert.AreEqual( Keys - Keys / 3, index.Count );

		var again = index.FindOrAdd( Key( 1, 1 ), 0, _ => 999_999u, out var remade );
		Assert.IsTrue( remade && again == 999_999u, "a removed key is made again" );
	}

	[TestMethod]
	public void RemovedMaterialsTakeOnlyTheDrawRecordsNamingThem()
	{
		var index = new DrawRecordIndex();
		const int Keys = 20_000;
		static DrawRecordKey Named( int i ) => new( 1, (uint)(i % 500) * 16, (uint)(i % 300 + 1000) * 16, 0, (uint)i, 3, 0, 0 );
		for ( int i = 0; i < Keys; i++ ) index.FindOrAdd( Named( i ), (uint)(i + 1), static value => value, out _ );

		// A few records, by the vertex material or the pixel one
		var gone = new HashSet<uint> { 3 * 16, 7 * 16, 1004 * 16 };
		var expected = Enumerable.Range( 0, Keys ).Count( i => gone.Contains( Named( i ).Vertex ) || gone.Contains( Named( i ).Pixel ) );
		Assert.AreEqual( expected, index.RemoveMaterials( gone ) );
		Assert.AreEqual( Keys - expected, index.Count );
		for ( int i = 0; i < Keys; i++ ) Assert.AreEqual( gone.Contains( Named( i ).Vertex ) || gone.Contains( Named( i ).Pixel ), index.Find( Named( i ) ) == 0, $"key {i}" );

		// Many: past what the smallest filter of their bits holds
		var many = Enumerable.Range( 0, 400 ).Select( m => (uint)m * 16 ).ToHashSet();
		var left = Enumerable.Range( 0, Keys ).Count( i => index.Find( Named( i ) ) != 0 && many.Contains( Named( i ).Vertex ) );
		Assert.AreEqual( left, index.RemoveMaterials( many ) );
		for ( int i = 0; i < Keys; i++ ) Assert.IsFalse( many.Contains( Named( i ).Vertex ) && index.Find( Named( i ) ) != 0, $"key {i}" );
		Assert.AreEqual( 0, index.RemoveMaterials( new HashSet<uint> { 5_000_000 } ), "nothing names it" );
	}
}
