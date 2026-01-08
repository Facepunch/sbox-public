using System.IO;

namespace TestCompiler;

[TestClass]
public partial class CompilerTest
{
	/// <summary>
	/// As simple as things could be. Create a compile group with a single compiler.
	/// </summary>
	[TestMethod]
	public async Task SingleCompiler()
	{
		bool compileSuccessCallback = false;

		var codePath = System.IO.Path.GetFullPath( "data/code/base" );
		var group = new CompileGroup( "Test" );
		group.OnCompileSuccess = () => compileSuccessCallback = true;

		var compilerSettings = new Compiler.Configuration();
		compilerSettings.Clean();

		var compiler = group.CreateCompiler( "test", codePath, compilerSettings );
		Assert.IsTrue( compiler.NeedsBuild );
		Assert.IsFalse( compileSuccessCallback );
		await group.BuildAsync();
		Assert.IsTrue( compileSuccessCallback );
		Assert.IsFalse( compiler.NeedsBuild );

		Assert.IsNotNull( group.BuildResult );
		Assert.IsTrue( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );
		Assert.AreEqual( group.BuildResult.Output.Count(), 1 );

		// test multiple compiles

		Assert.IsFalse( compiler.NeedsBuild );
		compiler.MarkForRecompile();
		Assert.IsTrue( compiler.NeedsBuild );

		await group.BuildAsync();

		Assert.IsFalse( compiler.NeedsBuild );
		Assert.AreEqual( group.BuildResult.Output.Count(), 1 );
	}

	/// <summary>
	/// A compiler can add a reference to itself, and it'll just get ignored
	/// </summary>
	[TestMethod]
	public async Task SelfReferencing()
	{
		bool compileSuccessCallback = false;

		var codePath = System.IO.Path.GetFullPath( "data/code/base" );
		var group = new CompileGroup( "Test" );
		group.OnCompileSuccess = () => compileSuccessCallback = true;

		var compilerSettings = new Compiler.Configuration();
		compilerSettings.Clean();

		var compiler = group.CreateCompiler( "test", codePath, compilerSettings );
		compiler.AddReference( compiler );

		Assert.IsTrue( compiler.NeedsBuild );
		Assert.IsFalse( compileSuccessCallback );
		await group.BuildAsync();
		Assert.IsTrue( compileSuccessCallback );
		Assert.IsFalse( compiler.NeedsBuild );

		Assert.IsNotNull( group.BuildResult );
		Assert.IsTrue( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );
		Assert.AreEqual( group.BuildResult.Output.Count(), 1 );
	}

	/// <summary>
	/// A bunch of unrelated compilers at the same time
	/// </summary>
	[TestMethod]
	public async Task MultipleUnrelatedCompilers()
	{
		bool compileSuccessCallback = false;

		var codePath = System.IO.Path.GetFullPath( "data/code/base" );
		var group = new CompileGroup( "Test" );
		group.OnCompileSuccess = () => compileSuccessCallback = true;

		var compilerSettings = new Compiler.Configuration();
		compilerSettings.Clean();

		for ( int i = 0; i < 7; i++ )
			group.CreateCompiler( $"test{i}", codePath, compilerSettings );

		Assert.IsFalse( compileSuccessCallback );
		await group.BuildAsync();
		Assert.IsTrue( compileSuccessCallback );
		Assert.IsFalse( group.IsBuilding );

		Assert.IsNotNull( group.BuildResult );
		Assert.IsTrue( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );
		Assert.AreEqual( group.BuildResult.Output.Count(), 7 );

		var randomCompiler = group.FindCompilerByPackageName( "test3" );

		// compiling without changes should do nothing
		await group.BuildAsync();
		Assert.IsFalse( group.IsBuilding );
		Assert.IsNotNull( group.BuildResult );
		Assert.IsTrue( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );
		Assert.AreEqual( group.BuildResult.Output.Count(), 7 );

		// Compiling should output only one compiler since they have no dependancies with each other
		randomCompiler.MarkForRecompile();
		await group.BuildAsync();
		Assert.IsFalse( group.IsBuilding );
		Assert.IsNotNull( group.BuildResult );
		Assert.IsTrue( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );
		Assert.AreEqual( group.BuildResult.Output.Count(), 1 );
	}

	/// <summary>
	/// One compiler needs another
	/// </summary>
	[TestMethod]
	public async Task DependantCompilers()
	{
		bool compileSuccessCallback = false;

		var codePath = System.IO.Path.GetFullPath( "data/code/" );
		var group = new CompileGroup( "Test" );
		group.OnCompileSuccess = () => compileSuccessCallback = true;

		var compilerSettings = new Compiler.Configuration();
		compilerSettings.Clean();

		var depnCompiler = group.CreateCompiler( $"depend", codePath + "/dependant", compilerSettings );
		var baseCompiler = group.CreateCompiler( $"base", codePath + "/base", compilerSettings );

		depnCompiler.AddReference( baseCompiler );

		Assert.IsFalse( compileSuccessCallback );
		await group.BuildAsync();

		Assert.IsTrue( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );
		Assert.IsTrue( compileSuccessCallback );
		Assert.AreEqual( group.BuildResult.Output.First().Compiler, baseCompiler );
		Assert.AreEqual( group.BuildResult.Output.Last().Compiler, depnCompiler );

		Assert.IsNotNull( group.BuildResult );
		Assert.AreEqual( group.BuildResult.Output.Count(), 2 );

		// Should only return what it's had to compile
		depnCompiler.MarkForRecompile();

		await group.BuildAsync();

		Assert.IsTrue( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );
		Assert.AreEqual( group.BuildResult.Output.Count(), 1 );

	}

	/// <summary>
	/// One compiler needs another
	/// </summary>
	[TestMethod]
	public async Task CyclicDependencies()
	{
		bool compileSuccessCallback = false;

		var codePath = System.IO.Path.GetFullPath( "data/code/" );
		var group = new CompileGroup( "Test" );
		group.OnCompileSuccess = () => compileSuccessCallback = true;

		var compilerSettings = new Compiler.Configuration();
		compilerSettings.Clean();

		var depnCompiler = group.CreateCompiler( $"depend", codePath + "/dependant", compilerSettings );
		var baseCompiler = group.CreateCompiler( $"base", codePath + "/base", compilerSettings );

		depnCompiler.AddReference( baseCompiler );
		baseCompiler.AddReference( depnCompiler );

		Assert.IsFalse( compileSuccessCallback );
		await Assert.ThrowsExceptionAsync<System.Exception>( () => group.BuildAsync() );

		Assert.IsFalse( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );
		Assert.IsFalse( compileSuccessCallback );

		Assert.IsNotNull( group.BuildResult );
		Assert.AreEqual( group.BuildResult.Output.Count(), 0 );

	}

	/// <summary>
	/// As simple as things could be. Create a compile group with a single compiler.
	/// </summary>
	[TestMethod]
	public async Task EditorFolders()
	{
		var codePath = System.IO.Path.GetFullPath( "data/code/with_editor_folders" );
		var group = new CompileGroup( "Test" );

		//
		// Game Dll
		//
		{
			var compilerSettings = new Compiler.Configuration();
			compilerSettings.IgnoreFolders.Add( "editor" ); // ignore editor folders
			compilerSettings.IgnoreFolders.Add( "unittest" ); // ignore editor folders
			group.CreateCompiler( "test", codePath, compilerSettings );
		}

		//
		// Editor Dlls
		//
		{
			int editorFolderId = 0;
			var editorFolders = System.IO.Directory.EnumerateDirectories( codePath, "editor", SearchOption.AllDirectories ).ToArray();
			if ( editorFolders.Count() > 0 )
			{
				var compilerSettings = new Compiler.Configuration();
				var compiler = group.CreateCompiler( $"test.editor{++editorFolderId}", editorFolders[0], compilerSettings );
				compiler.AddReference( "Sandbox.Tools" );
				compiler.AddReference( "package.test" );

				for ( int i = 0; i < editorFolders.Length; i++ )
				{
					if ( i == 0 ) continue;

					compiler.AddSourcePath( editorFolders[i] );
				}
			}
			foreach ( var folder in editorFolders )
			{
				System.Console.WriteLine( folder );


			}
			//var compilerSettings = new CompilerSettings();
			//compilerSettings.IgnoreFolders.Add( "editor" ); // ignore editor folders
			///group.CreateCompiler( "test", codePath, compilerSettings );
		}

		await group.BuildAsync();

		Assert.IsNotNull( group.BuildResult );
		Assert.IsTrue( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );

		// 1 editor, 1 game
		Assert.AreEqual( 2, group.BuildResult.Output.Count() );
	}

	/*
	/// <summary>
	/// We rely on a package dll
	/// </summary>
	[TestMethod]
	public async Task PackageDependant()
	{
		var ac = new AccessControl( "game" );
		AssemblyLoadContext alc = new AssemblyLoadContext( "testSandbox", true );

		var info = await Api.Packages.Get.RunAsync( "facepunch.sandbox" );
		Assert.IsNotNull( info.Package.Revision.Manifest );

		var gameDllInfo = info.Package.Revision.Manifest.Files.Single( x => x.Path == ".assembly" );
		var gameDll = await Sandbox.Utility.Web.GrabFile( $"https://files.facepunch.com/sbox/asset{gameDllInfo.Url}", default );

		var gamePassed = ac.VerifyAssembly( new MemoryStream( gameDll ), out var trustedDll );
		Assert.IsTrue( gamePassed, string.Join( ",", ac.Errors ) );

		// try to load the game.dll
		var gameAssembly = alc.LoadFromStreamWithEmbeds( trustedDll );

		bool compileSuccessCallback = false;

		var codePath = System.IO.Path.GetFullPath( "compiler/code/" );
		var group = new CompileGroup();
		group.AddAssembly( "facepunch.sandbox", gameDll );

		group.OnCompileSuccess = ( g ) => compileSuccessCallback = true;

		var compilerSettings = new CompilerSettings();
		compilerSettings.Clean();

		var extensionCompiler = group.CreateCompiler( $"extension", codePath + "/extension", compilerSettings );
		extensionCompiler.GeneratedCode.AppendLine( "global using static Sandbox.Internal.GlobalGameNamespace;" );
		extensionCompiler.AddReference( "facepunch.sandbox" );
		extensionCompiler.AddReference( "Sandbox.Game" );

		Assert.IsFalse( compileSuccessCallback );
		await group.BuildAsync();

		Assert.IsTrue( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );
		Assert.IsTrue( compileSuccessCallback );

		Assert.IsNotNull( group.BuildResult );
		Assert.AreEqual( group.BuildResult.Output.Count(), 1 );

	}
	*/
}
