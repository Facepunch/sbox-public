using System.Linq;

namespace CompilingTests.CodeAccess;

// Regression test for the ComImport sandbox escape: a [ComImport] type can spin up arbitrary
// COM objects and escape the sandbox. The compiler-time analyzer must reject it before emit.
// https://hackerone.com/reports/3775841
[TestClass]
[DoNotParallelize]
public class ComImportEscapeTest
{
	[TestMethod]
	public void ComImport_Type_Is_Rejected()
	{
		var result = CodeAccessGate.Verify( """
			using System.Runtime.InteropServices;
			[ComImport]
			[Guid( "00020400-0000-0000-C000-000000000046" )]
			interface IEscape { }
			""" );

		Assert.IsFalse( result.EmitFailed, $"snippet should compile: {result}" );
		Assert.IsTrue( result.Denied.Any( x => x.Contains( "ComImport" ) ),
			$"expected a ComImport access error: {result}" );
	}

	// Same shape without ComImport, so the rejection above is the attribute and not the Guid.
	[TestMethod]
	public void NonComImport_Type_Is_Allowed()
	{
		var result = CodeAccessGate.Verify( """
			using System.Runtime.InteropServices;
			[Guid( "00020400-0000-0000-C000-000000000046" )]
			interface IControl { }
			""" );

		Assert.IsFalse( result.EmitFailed, $"snippet should compile: {result}" );
		Assert.AreEqual( 0, result.Denied.Count, $"control type should be allowed: {result}" );
	}
}
