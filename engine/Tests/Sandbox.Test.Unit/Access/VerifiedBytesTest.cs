using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox;
using System.IO;

namespace AccessTests;

// Assemblies that passed are let through again by their hash without being checked. Only the exact bytes that passed
// may be, never another assembly sharing their name.
[TestClass]
public class VerifiedBytesTest
{
	const string Clean = "public static class P { public static int M() => 1; }";
	const string Escape = "public static class P { public static void M() => System.IO.File.Delete( \"x\" ); }";

	static byte[] Compile( string source )
	{
		var refs = new[] { MetadataReference.CreateFromFile( typeof( object ).Assembly.Location ) };
		var compilation = CSharpCompilation.Create( "package.verifiedbytes", new[] { CSharpSyntaxTree.ParseText( source ) }, refs,
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );

		using var ms = new MemoryStream();
		var emit = compilation.Emit( ms );
		Assert.IsTrue( emit.Success, string.Join( "\n", emit.Diagnostics ) );
		return ms.ToArray();
	}

	static bool Verify( AccessControl ac, byte[] bytes )
	{
		using var stream = new MemoryStream( bytes );
		var result = ac.VerifyAssembly( stream, out var trusted );
		trusted?.Dispose();
		return result.Success;
	}

	[TestMethod]
	public void Same_Name_Different_Bytes_Is_Checked()
	{
		using var ac = new AccessControl();

		Assert.IsTrue( Verify( ac, Compile( Clean ) ) );
		Assert.IsFalse( Verify( ac, Compile( Escape ) ), "An assembly named like one that passed must still be checked" );
	}

	[TestMethod]
	public void Rejected_Stays_Rejected()
	{
		using var ac = new AccessControl();
		var bytes = Compile( Escape );

		Assert.IsFalse( Verify( ac, bytes ) );
		Assert.IsFalse( Verify( ac, bytes ), "A rejected assembly must not pass when asked again" );
	}

	[TestMethod]
	public void Passed_Passes_Again()
	{
		using var ac = new AccessControl();
		var bytes = Compile( Clean );

		Assert.IsTrue( Verify( ac, bytes ) );
		Assert.IsTrue( Verify( ac, bytes ) );
	}
}
