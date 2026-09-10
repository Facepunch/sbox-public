using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox;
using System.IO;
using System.Linq;

namespace AccessTests;

// Regression test for the ComImport sandbox escape: a [ComImport] type can spin up arbitrary
// COM objects and escape the sandbox, and its metadata flag slipped past access control.
// https://hackerone.com/reports/3775841
[TestClass]
[DoNotParallelize]
public class ComImportEscapeTest
{
	// Compile a package.* assembly from source and run it through access control.
	static AccessControlResult Verify( string source )
	{
		var tree = CSharpSyntaxTree.ParseText( source );

		var corePath = Path.GetDirectoryName( typeof( object ).Assembly.Location );
		var refs = new[]
		{
			MetadataReference.CreateFromFile( typeof( object ).Assembly.Location ),
			MetadataReference.CreateFromFile( Path.Combine( corePath, "System.Runtime.dll" ) ),
		};

		var compilation = CSharpCompilation.Create( "package.comimporttest", new[] { tree }, refs,
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

	[TestMethod]
	public void ComImport_Type_Is_Rejected()
	{
		var result = Verify( """
			using System.Runtime.InteropServices;
			[ComImport]
			[Guid( "00020400-0000-0000-C000-000000000046" )]
			interface IEscape { }
			""" );

		Assert.IsFalse( result.Success, "ComImport type must not pass access control" );
		Assert.IsTrue( result.Errors.Any( x => x.Contains( "ComImport" ) ),
			"Expected a ComImport whitelist error, got:\n" + string.Join( "\n", result.Errors ) );
	}

	// Same shape without ComImport, so the rejection above is the flag and not the Guid.
	[TestMethod]
	public void NonComImport_Type_Is_Allowed()
	{
		var result = Verify( """
			using System.Runtime.InteropServices;
			[Guid( "00020400-0000-0000-C000-000000000046" )]
			interface IEscape { }
			""" );

		Assert.IsTrue( result.Success, "Control type must pass:\n" + string.Join( "\n", result.Errors ) );
	}
}
