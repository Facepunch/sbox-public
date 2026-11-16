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

	// A copy of the handler keeps its rented array after the original returns it
	[TestMethod]
	public void InterpolationHandler_Is_Rejected()
	{
		var result = Verify( "using System.Runtime.CompilerServices; public static class P { public static string M() { var h = new DefaultInterpolatedStringHandler( 0, 0 ); var c = h; h.ToStringAndClear(); return c.ToString(); } }" );

		Assert.IsTrue( result.Denied.Any( x => x.Contains( "DefaultInterpolatedStringHandler" ) ), $"Expected a handler access error: {result}" );
	}

	[TestMethod]
	public void StringInterpolation_Is_Allowed()
	{
		var result = Verify( "public static class P { public static string M( int x ) => $\"v{x}\"; }" );

		Assert.AreEqual( 0, result.Denied.Count, $"Control must pass: {result}" );
	}
}
