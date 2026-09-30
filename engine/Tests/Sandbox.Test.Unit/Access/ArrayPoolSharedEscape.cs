using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox;
using System.Buffers;
using System.IO;
using System.Linq;

namespace AccessTests;

// ArrayPool<T>.Shared is process wide - renting from it across packages is a use after free. The
// PublicArrayPool redirect only sees source, so access control has to hold whatever IL comes out.
[TestClass]
[DoNotParallelize]
public class ArrayPoolSharedEscapeTest
{
	static AccessControlResult Verify( string source )
	{
		var tree = CSharpSyntaxTree.ParseText( source );

		var corePath = Path.GetDirectoryName( typeof( object ).Assembly.Location );
		var refs = new[]
		{
			MetadataReference.CreateFromFile( typeof( object ).Assembly.Location ),
			MetadataReference.CreateFromFile( Path.Combine( corePath, "System.Runtime.dll" ) ),
			MetadataReference.CreateFromFile( typeof( ArrayPool<int> ).Assembly.Location ),
		};

		var compilation = CSharpCompilation.Create( "package.arraypooltest", new[] { tree }, refs,
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );

		using var ms = new MemoryStream();
		var emit = compilation.Emit( ms );
		Assert.IsTrue( emit.Success, string.Join( "\n", emit.Diagnostics ) );
		ms.Position = 0;

		using var ac = new AccessControl();
		var result = ac.VerifyAssembly( ms, out var trusted );
		trusted?.Dispose();
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

		Assert.IsFalse( result.Success, "Renting from the process wide pool must not pass access control" );
		Assert.IsTrue( result.Errors.Any( x => x.Contains( "get_Shared" ) ),
			"Expected an ArrayPool.Shared whitelist error, got:\n" + string.Join( "\n", result.Errors ) );
	}

	[TestMethod]
	public void ArrayPool_Without_Shared_Is_Allowed()
	{
		var result = Verify( "using System.Buffers; public static class P { public static int[] M( ArrayPool<int> pool ) => pool.Rent( 1 ); }" );

		Assert.IsTrue( result.Success, "Control must pass:\n" + string.Join( "\n", result.Errors ) );
	}
}
