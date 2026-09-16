using System.Linq;

namespace CompilingTests.CodeAccess;

// ArrayPool<T>.Shared is process wide. Raw source must be rejected if the PublicArrayPool
// redirect did not run, regardless of how the property was spelled.
[TestClass]
[DoNotParallelize]
public class ArrayPoolSharedEscapeTest
{
	static CodeAccessGate.Result Verify( string source )
	{
		var result = CodeAccessGate.Verify( source, runGenerator: false );
		Assert.IsFalse( result.EmitFailed, $"fixture must be valid C#: {result}" );
		return result;
	}

	// Every spelling folds back onto the same get_Shared call.
	[DataTestMethod]
	[DataRow( "using System.Buffers; public static class P { public static int[] M() => ArrayPool<int>.Shared.Rent( 1 ); }" )]
	[DataRow( "using AP = System.Buffers.ArrayPool<int>; public static class P { public static int[] M() => AP.Shared.Rent( 1 ); }" )]
	[DataRow( "using static System.Buffers.ArrayPool<int>; public static class P { public static int[] M() => Shared.Rent( 1 ); }" )]
	public void ArrayPoolShared_Is_Rejected( string source )
	{
		var result = Verify( source );

		Assert.IsTrue( result.Denied.Any( x => x.Contains( "get_Shared" ) ),
			$"Expected an ArrayPool.Shared access error: {result}" );
	}

	[TestMethod]
	public void ArrayPool_Without_Shared_Is_Allowed()
	{
		var result = Verify( "using System.Buffers; public static class P { public static int[] M( ArrayPool<int> pool ) => pool.Rent( 1 ); }" );

		Assert.AreEqual( 0, result.Denied.Count, $"Control must pass: {result}" );
	}
}
