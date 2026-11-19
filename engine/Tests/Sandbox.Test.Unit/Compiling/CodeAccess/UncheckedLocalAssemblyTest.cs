using Sandbox;

namespace CompilingTests.CodeAccess;

// Remote code can't reference or replace a local assembly built without the access check.
// Projects write their assemblies to /.bin, so the check has to find them there.
[TestClass]
public class UncheckedLocalAssemblyTest
{
	static MemoryFileSystem Assemblies( params string[] names )
	{
		var fs = new MemoryFileSystem();
		fs.CreateDirectory( "/.bin" );
		foreach ( var name in names )
			fs.WriteAllBytes( $"/.bin/{name}.dll", [0] );
		return fs;
	}

	[TestMethod]
	public void PrecompiledAssembly_IsUnchecked()
	{
		var fs = Assemblies( "package.local.menu" );
		Assert.IsTrue( PackageManager.ActivePackage.IsUncheckedAssembly( fs, null, "package.local.menu" ) );
	}

	[TestMethod]
	public void CheckedGameAssembly_IsNotUnchecked()
	{
		using var group = new CompileGroup( "test" );
		var compiler = group.GetOrCreateCompiler( "local.game" );

		var fs = Assemblies( "package.local.game", "package.local.game.editor" );
		Assert.IsFalse( PackageManager.ActivePackage.IsUncheckedAssembly( fs, compiler, "package.local.game" ) );
		Assert.IsTrue( PackageManager.ActivePackage.IsUncheckedAssembly( fs, compiler, "package.local.game.editor" ) );
	}

	[TestMethod]
	public void UnsandboxedAssembly_IsUnchecked()
	{
		using var group = new CompileGroup( "test" );
		var compiler = group.GetOrCreateCompiler( "local.toolbase" );
		var config = compiler.GetConfiguration();
		config.Whitelist = false;
		compiler.SetConfiguration( config );

		var fs = Assemblies( "package.local.toolbase" );
		Assert.IsTrue( PackageManager.ActivePackage.IsUncheckedAssembly( fs, compiler, "package.local.toolbase" ) );
	}

	[TestMethod]
	public void MissingAssembly_IsNotUnchecked()
	{
		var fs = Assemblies();
		Assert.IsFalse( PackageManager.ActivePackage.IsUncheckedAssembly( fs, null, "package.local.menu" ) );
	}
}
