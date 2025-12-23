global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Sandbox;
global using System.Linq;
global using System.Threading.Tasks;
global using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Sandbox.Engine;
using Sandbox.Internal;
using System;

[assembly: Parallelize( Workers = 0, Scope = ExecutionScope.MethodLevel )]

[TestClass]
public class TestInit
{
	[AssemblyInitialize]
	public static void AssemblyInitialize( TestContext context )
	{
		// Some really basic intialization only
		// We do not boot up the native engine

		var gameFolder = System.Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE", EnvironmentVariableTarget.Process );
		if ( gameFolder is null ) throw new Exception( "FACEPUNCH_ENGINE not found" );

		Environment.CurrentDirectory = gameFolder;

		NetCore.InitializeInterop( gameFolder );

		Api.Init();

		// Usually this, is created by native source 2, we have to do it manually here, as we don't init native source2 for this assembly.
		System.IO.Directory.CreateDirectory( ".source2/" );

		Application.IsUnitTest = true;
		GlobalContext.Current.TypeLibrary = new TypeLibrary();

		GlobalContext.Current.UISystem = new UISystem();

		GlobalContext.Current.TypeLibrary.AddIntrinsicTypes();
		GlobalContext.Current.TypeLibrary.AddAssembly( typeof( Vector3 ).Assembly, false );
		GlobalContext.Current.TypeLibrary.AddAssembly( typeof( EngineLoop ).Assembly, false );
		GlobalContext.Current.TypeLibrary.AddAssembly( typeof( Facepunch.ActionGraphs.ActionGraph ).Assembly, false );
		GlobalContext.Current.TypeLibrary.AddAssembly( typeof( TestInit ).Assembly, true );

		GlobalToolsNamespace.EditorTypeLibrary = new TypeLibrary();
		GlobalToolsNamespace.EditorTypeLibrary.AddIntrinsicTypes();
		GlobalToolsNamespace.EditorTypeLibrary.AddAssembly( typeof( Vector3 ).Assembly, false );
		GlobalToolsNamespace.EditorTypeLibrary.AddAssembly( typeof( EngineLoop ).Assembly, false );
		GlobalToolsNamespace.EditorTypeLibrary.AddAssembly( typeof( Facepunch.ActionGraphs.ActionGraph ).Assembly, false );
		GlobalToolsNamespace.EditorTypeLibrary.AddAssembly( typeof( TestInit ).Assembly, true );
	}
}
