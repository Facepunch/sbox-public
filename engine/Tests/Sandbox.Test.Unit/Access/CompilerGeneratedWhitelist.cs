using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox;
using System.IO;
using System.Linq;

namespace AccessTests;

[TestClass]
public class CompilerGeneratedWhitelistTest
{
	static AccessControlResult Verify( string source, bool allowUnsafe = false )
	{
		var corePath = Path.GetDirectoryName( typeof( object ).Assembly.Location );
		var refs = new[] { typeof( object ).Assembly.Location, Path.Combine( corePath, "System.Runtime.dll" ), Path.Combine( corePath, "System.Memory.dll" ) }
			.Select( x => MetadataReference.CreateFromFile( x ) );

		var compilation = CSharpCompilation.Create( "package.compilergeneratedwhitelist", [CSharpSyntaxTree.ParseText( source )], refs,
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ).WithAllowUnsafe( allowUnsafe ) );

		using var ms = new MemoryStream();
		Assert.IsTrue( compilation.Emit( ms ).Success );
		ms.Position = 0;

		using var ac = new AccessControl();
		var result = ac.VerifyAssembly( ms, out var trusted );
		trusted?.Dispose();
		return result;
	}

	[TestMethod]
	public void Pointer_Span_Factory_Is_Rejected()
	{
		var result = Verify( "public static unsafe class C { public static int L( byte* t ) => System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpanFromNullTerminated( t ).Length; }", allowUnsafe: true );

		Assert.IsTrue( result.Errors.Any( x => x.Contains( "MemoryMarshal.CreateReadOnlySpanFromNullTerminated" ) ), string.Join( "\n", result.Errors ) );
	}

	[TestMethod]
	public void Span_Collection_Expression_Passes()
	{
		var result = Verify( "public static class C { public static int L( int a, int b ) { System.ReadOnlySpan<int> v = [a, b]; return v.Length; } }" );

		Assert.IsTrue( result.Success, string.Join( "\n", result.Errors ) );
	}
}
