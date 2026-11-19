using System.Linq;

namespace CompilingTests.CodeAccess;

/// <summary>
/// Mechanism 1 of 3: the compiler REFERENCE whitelist (Compiler.References._references). Sandboxed package
/// code only compiles against the fixed base set (Sandbox.* + the razor mock) plus explicitly added package
/// references; it can't even NAME a type in a non-whitelisted assembly - even one loaded in this very
/// process. This is the coarse first gate, ahead of the source hard blocks and the Access allow-list.
///
/// These drive a REAL <see cref="CompileGroup"/> build (production's BuildReferencesAsync), not the
/// CodeAccessGate harness, so that if production ever widens the reference set the boundary test reacts.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CodeAccessReferenceTest
{
	[TestMethod]
	public async Task WhitelistedAssembly_Binds()
	{
		var codePath = System.IO.Path.GetFullPath( "data/code/ref_boundary_ok" );
		using var group = new CompileGroup( "RefBoundaryOk" );

		var compiler = group.CreateCompiler( "test", codePath, new Compiler.Configuration() );
		await group.BuildAsync();

		Assert.IsTrue( compiler.Output.Successful,
			"a whitelisted Sandbox type must bind: " + string.Join( ", ", compiler.Diagnostics.Select( d => d.GetMessage() ) ) );
	}

	[TestMethod]
	public async Task NonWhitelistedAssembly_DoesNotBind()
	{
		var codePath = System.IO.Path.GetFullPath( "data/code/ref_boundary" );
		using var group = new CompileGroup( "RefBoundaryBad" );

		var compiler = group.CreateCompiler( "test", codePath, new Compiler.Configuration() );
		await group.BuildAsync();

		Assert.IsFalse( compiler.Output.Successful,
			"package code must not bind a type from a non-whitelisted assembly" );

		Assert.IsTrue( compiler.Diagnostics.Any( d => d.Id is "CS0234" or "CS0246" ),
			"expected the non-whitelisted Mono.Cecil type to remain unbindable: "
			+ string.Join( ", ", compiler.Diagnostics.Select( d => d.GetMessage() ) ) );
	}
}
