using System;
using System.Buffers;

namespace PhysicsTests;

/// <summary>
/// Pins heightfield input lengths and ownership at the managed/native boundary.
/// </summary>
[TestClass]
public class HeightfieldTest
{
	/// <summary>
	/// Supplying a buffer still requires an exact vertex count. Null materials remain optional,
	/// but empty, truncated and oversized arrays must not bypass validation.
	/// </summary>
	[TestMethod]
	public void HeightfieldRequiresExactArrayLengths()
	{
		const int resolution = 129;
		const int count = resolution * resolution;
		var world = new PhysicsWorld();
		try
		{
			var body = new PhysicsBody( world ) { BodyType = PhysicsBodyType.Static };
			var heights = new ushort[count];
			var materials = new byte[count];

			Assert.ThrowsException<ArgumentException>( () => body.AddHeightFieldShape( null, materials, resolution, resolution, 1, 1 ) );
			foreach ( var length in new[] { 0, count - 1, count + 1, 32768 } )
			{
				Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
					body.AddHeightFieldShape( new ushort[length], materials, resolution, resolution, 1, 1 ) );
				Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
					body.AddHeightFieldShape( heights, new byte[length], resolution, resolution, 1, 1 ) );
			}

			Assert.IsTrue( body.AddHeightFieldShape( heights, null, resolution, resolution, 1, 1 ).IsValid() );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// A slice is validated by its logical length and pinned at its start, and native collision
	/// retains its own copy after both source buffers have been overwritten.
	/// </summary>
	[TestMethod]
	public void HeightfieldCopiesOnlyTheSuppliedMaterialSpan()
	{
		const int resolution = 129;
		const int count = resolution * resolution;
		const int offset = 17;
		var materials = ArrayPool<byte>.Shared.Rent( count + offset );
		var world = new PhysicsWorld();
		try
		{
			var body = new PhysicsBody( world ) { BodyType = PhysicsBodyType.Static };
			var heights = new ushort[count];
			Array.Fill( heights, (ushort)100 );
			Array.Fill( materials, byte.MaxValue );
			materials.AsSpan( offset, count ).Clear();
			materials[offset + 5 * resolution + 7] = byte.MaxValue;

			Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
				body.AddHeightFieldShape( heights, materials, resolution, resolution, 1, 1, 0 ) );
			body.AddHeightFieldShape( heights, materials.AsSpan( offset, count ), resolution, resolution, 1, 1, 0 );

			Array.Clear( heights );
			Array.Fill( materials, byte.MaxValue );

			PhysicsTraceResult Drop( int x, int z ) => world.Trace.Ray(
				new Vector3( x + 0.25f, 200, z + 0.25f ),
				new Vector3( x + 0.25f, -100, z + 0.25f ) ).Run();

			foreach ( var cell in new[] { 0, resolution - 2 } )
			{
				var hit = Drop( cell, cell );
				Assert.IsTrue( hit.Hit, "Neither the dirty prefix nor the tail becomes collision material" );
				Assert.AreEqual( 100f, hit.HitPosition.y, 0.1f );
			}

			Assert.IsFalse( Drop( 5, 7 ).Hit, "The intended material entry remains a hole after copying" );
		}
		finally
		{
			world.Delete();
			ArrayPool<byte>.Shared.Return( materials );
		}
	}
}
