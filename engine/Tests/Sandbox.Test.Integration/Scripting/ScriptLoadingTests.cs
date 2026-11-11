using Sandbox.Engine;
using Sandbox.Internal;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ScriptingTests;

/// <summary>
/// File execution, TypeLibrary enrollment, and real component behavior.
/// </summary>
[TestClass, DoNotParallelize]
public partial class ScriptLoadingTests
{
	private static ScriptLoadingTests active;

	private GlobalContext previous;
	private GlobalContext previousGame;
	private TypeLibrary previousEditor;
	private System.Text.Json.JsonSerializerOptions previousFilesystemJsonOptions;
	private GlobalContext context;
	private PackageLoader scriptPackageLoader;
	private MemoryFileSystem files;
	private Scene scene;
	private object[] reloadRoots;

	/// <summary>
	/// Each test owns its type libraries because script resources add and remove types.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		ThreadSafe.MarkMainThread();
		previous = GlobalContext.Current;
		previousGame = GlobalContext.Game;
		previousEditor = TypeLibrary.Editor;
		previousFilesystemJsonOptions = BaseFileSystem.JsonSerializerOptions;
		files = new MemoryFileSystem();
		context = new GlobalContext { TypeLibrary = CreateLibrary(), FileMount = files };
		GlobalContext.Game = context;
		GlobalContext.Current = context;
		TypeLibrary.Editor = CreateLibrary();
		scriptPackageLoader = new PackageLoader( "ScriptTest", GetType().Assembly );
		scriptPackageLoader.HotloadWatch( GetType().Assembly );
		scriptPackageLoader.OnAfterHotload = () =>
		{
			context.OnHotload();
			scene?.OnHotload();
		};
		context.ReplaceScriptAssembly = scriptPackageLoader.ReplaceScriptAssembly;

		// Match the serializer and query-cache setup performed by ResetEnvironment.
		Json.Initialize();
		ReflectionQueryCache.ClearTypeCache();

		// Retain the fixture through ordinary watched static fields, as game and editor assemblies do.
		active = this;
	}

	/// <summary>
	/// Removes registrations and restores the engine's original contexts.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current = context;
		scene?.Destroy();
		context.Shutdown();
		scriptPackageLoader.Dispose();
		Json.WarmUpCts?.Cancel();
		ReflectionQueryCache.ClearTypeCache();
		BaseFileSystem.JsonSerializerOptions = previousFilesystemJsonOptions;
		active = null;
		TypeLibrary.Editor = previousEditor;
		GlobalContext.Game = previousGame;
		GlobalContext.Current = previous;
		files.Dispose();
	}

	private static TypeLibrary CreateLibrary()
	{
		var library = new TypeLibrary();
		library.AddIntrinsicTypes();
		library.AddAssembly( typeof( Vector3 ).Assembly, false );
		library.AddAssembly( typeof( Component ).Assembly, false );
		library.AddAssembly( typeof( TypeLibrary ).Assembly, false );
		return library;
	}

	/// <summary>
	/// Loading and reloading a script resource reads source without compiling or executing it.
	/// </summary>
	[TestMethod]
	public void ScriptResourceLoadsSourceWithoutExecuting()
	{
		const string path = "source.csx";
		const string source = "[Global] int ResourceLoads = 0; ResourceLoads++; class SourceComponent : Component { }";
		Game.Scripting.Globals.Set( "ResourceLoads", 0 );
		files.WriteAllText( path, "\uFEFF" + source );
		Assert.IsFalse( new ScriptResource().IsValid );

		var resource = Resource.Load( typeof( ScriptResource ), path ) as ScriptResource;
		Assert.IsNotNull( resource );
		Assert.IsTrue( resource.IsValid );
		Assert.AreEqual( source, resource.Source );
		Assert.AreEqual( 0, Game.Scripting.Globals.Get<int>( "ResourceLoads" ) );
		Assert.IsNull( Game.TypeLibrary.GetType( "SourceComponent" ) );

		Assert.IsTrue( Game.Scripting.Load( resource.Source, resource.ResourcePath ), Game.Scripting.LastError?.ToString() );
		Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "ResourceLoads" ) );
		Assert.IsNotNull( Game.TypeLibrary.GetType( "SourceComponent" ) );

		var assetType = Game.TypeLibrary.GetType<ScriptResource>().GetAttribute<AssetTypeAttribute>();
		foreach ( var replacement in new[] { "invalid !!!", "" } )
		{
			files.WriteAllText( path, replacement );
			Assert.AreSame( resource, Game.Resources.LoadUncompiledResource( assetType, path, files, reloadExisting: true ) );
			Assert.AreEqual( replacement, resource.Source );
			Assert.IsTrue( resource.IsValid );
			Assert.IsNull( Game.Scripting.LastError );
			Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "ResourceLoads" ) );
		}
	}

	/// <summary>
	/// Bulk resource loading executes current script source in path order on every scan, without compiled counterparts.
	/// </summary>
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task StartupExecutesSourceInPathOrderOnEveryScan( bool asynchronous )
	{
		files.WriteAllText( "scripts/b.csx", "[Global] string Order = \"\"; Order += \"b\";" );
		files.WriteAllText( "scripts/a.csx", "Log.Info(\"Script resource startup\"); [Global] string Order = \"\"; Order += \"a\";" );
		files.WriteAllText( "scripts/ignored.csx_c", "this is not a script resource" );

		if ( asynchronous )
		{
			await ResourceLoader.LoadAllGameResourceAsync( files,
				preEnumeratedFiles: Task.FromResult( new List<string> { "scripts/b.csx", "scripts/a.csx", "scripts/ignored.csx_c" } ) );
		}
		else
		{
			ResourceLoader.LoadAllGameResource( files );
		}

		files.WriteAllText( "scripts/a.csx", "[Global] string Order = \"\"; Order += \"A\";" );
		if ( asynchronous )
		{
			await ResourceLoader.LoadAllGameResourceAsync( files );
		}
		else
		{
			ResourceLoader.LoadAllGameResource( files );
		}

		Assert.IsNull( Game.Scripting.LastError );
		Assert.AreEqual( "abAb", Game.Scripting.Globals.Get<string>( "Order" ) );
		Assert.AreEqual( files.ReadAllText( "scripts/a.csx" ), ResourceLibrary.Get<ScriptResource>( "scripts/a.csx" ).Source );
		Assert.IsFalse( files.FileExists( "scripts/a.csx_c" ) );
	}

	/// <summary>
	/// One bad script does not stop the resource scan from loading later scripts.
	/// </summary>
	[TestMethod]
	public void ResourceScanContinuesAfterScriptFailure()
	{
		files.WriteAllText( "a.csx", "class UniqueScriptComponent : Component { }" );
		files.WriteAllText( "c.csx", "this is invalid" );
		files.WriteAllText( "d.csx", "[Global] int Loaded = 0; Loaded++;" );

		ResourceLoader.LoadAllGameResource( files );

		Assert.IsNull( Game.Scripting.LastError );
		Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "Loaded" ) );
		Assert.AreEqual( 1, Game.TypeLibrary.GetTypes<Component>().Count( type => type.Name == "UniqueScriptComponent" ) );
	}

	private ScriptSystem LoadSource( string path, string source )
	{
		files.WriteAllText( path, source );
		Assert.IsTrue( Game.Scripting.Load( source, path ), Game.Scripting.LastError?.ToString() );
		return Game.Scripting;
	}

	/// <summary>
	/// Execution and analysis share loaded declarations and the engine's imported globals.
	/// </summary>
	[TestMethod]
	public void CommandsUseTheResourceEnvironment()
	{
		LoadSource( "command.csx", "class CommandData {} string ReadLogName() { return Log.Name; }" );
		Assert.AreEqual( GlobalGameNamespace.Log.Name, Game.Scripting.Run<string>( "return ReadLogName();" ) );
		var command = Game.Scripting.CreateScript( "return ReadLogName();" );
		Assert.AreEqual( GlobalGameNamespace.Log.Name, command.RunAndReturn<string>(), command.LastError?.ToString() );
		const string source = "return new CommandData();";
		var analysis = Game.Scripting.Analyze( source );
		Assert.IsTrue( analysis.GetCompletions( source.IndexOf( '(' ) ).Items.Any( item => item.Label == "CommandData" ) );
		Assert.IsTrue( Game.Scripting.Run<bool>( "return TypeLibrary.GetType(\"Sandbox.Component\") != null;" ) );
	}

	/// <summary>
	/// The public engine error preserves the compiler's filename and source coordinates.
	/// </summary>
	[TestMethod]
	public void LoadErrorsPreserveSourceCoordinates()
	{
		const string source = "class DiagnosticComponent : Component\n{\n    public int Read() { return (1).Missing(); }\n}";
		Assert.IsFalse( Game.Scripting.Load( source, "diagnostic.csx" ) );
		var error = Game.Scripting.LastError;
		Assert.IsNotNull( error );
		Assert.AreEqual( "diagnostic.csx", error.FilePath );
		Assert.AreEqual( 3, error.Line );
		Assert.AreEqual( 32, error.Column );
		Assert.AreEqual( source.IndexOf( "(1).Missing()", System.StringComparison.Ordinal ), error.Start );
	}
}
