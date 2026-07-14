using System.Linq;

namespace CompilingTests.CodeAccess;

/// <summary>
/// Sanity checks that the <see cref="CodeAccessGate"/> harness actually drives the analyzer in the test
/// environment (framework/Sandbox assemblies resolve, generators run). If these fail the whole corpus is
/// meaningless, so they're the canary.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CodeAccessGateSmokeTest
{
	static string Method( string body ) =>
		$$"""
		using Sandbox;
		public static class Probe
		{
			public static void Run()
			{
				{{body}}
			}
		}
		""";

	[TestMethod]
	public void PlainMath_IsAllowed()
	{
		var r = CodeAccessGate.Verify( Method( "var x = System.Math.Abs( -1.0 );" ) );

		Assert.IsFalse( r.EmitFailed, $"snippet should compile: {r}" );
		Assert.AreEqual( 0, r.Denied.Count, $"plain math should be allowed: {r}" );
	}

	[TestMethod]
	public void FileRead_IsDenied()
	{
		var r = CodeAccessGate.Verify( Method( "var s = System.IO.File.ReadAllText( \"x\" );" ) );

		Assert.IsFalse( r.EmitFailed, $"snippet should compile: {r}" );
		Assert.IsTrue( r.Denied.Any( x => x.Contains( "System.IO.File" ) ),
			$"analyzer must deny System.IO.File: {r}" );
	}

	[TestMethod]
	public void UnsafeAs_IsDenied()
	{
		// The compiler emits Unsafe.As for safe code, so the allow-list permits the emitted overload
		// (CompilerGenerated.cs), but naming it in source is a blacklist hit (SB500). The single walker
		// enforces both, so it's denied.
		var r = CodeAccessGate.Verify( Method(
			"var v = System.Runtime.CompilerServices.Unsafe.As<System.Version>( new System.Version() );" ) );

		Assert.IsFalse( r.EmitFailed, $"snippet should compile: {r}" );
		Assert.IsTrue( r.Denied.Any( x => x.Contains( "Unsafe.As" ) ), $"analyzer must deny Unsafe.As: {r}" );
	}
}
