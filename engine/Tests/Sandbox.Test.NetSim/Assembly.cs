global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Sandbox;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
global using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Sandbox.Engine;
using Sandbox.Internal;

// Peers swap process wide networking globals, nothing here can run in parallel
[assembly: DoNotParallelize]

namespace NetSim;

[TestClass]
public class TestInit
{
	[AssemblyInitialize]
	public static void AssemblyInitialize( TestContext context )
	{
		var gameFolder = Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE", EnvironmentVariableTarget.Process );
		if ( gameFolder is null ) throw new Exception( "FACEPUNCH_ENGINE not found" );

		Environment.CurrentDirectory = gameFolder;

		NetCore.InitializeInterop( gameFolder );

		Api.Init();

		System.IO.Directory.CreateDirectory( ".source2/" );

		Application.IsUnitTest = true;
		GlobalContext.Current.TypeLibrary = new TypeLibrary();
		GlobalContext.Current.UISystem = new UISystem();

		// Like Game.Local: private RPC methods (the GameObject ownership messages) must be found by identity
		GlobalContext.Current.TypeLibrary.ShouldExposePrivateMember = m => m.HasAttribute( typeof( RpcAttribute ) );
		GlobalContext.Current.TypeLibrary.AddIntrinsicTypes();
		GlobalContext.Current.TypeLibrary.AddAssembly( typeof( Vector3 ).Assembly, false );
		GlobalContext.Current.TypeLibrary.AddAssembly( typeof( EngineLoop ).Assembly, false );
		GlobalContext.Current.TypeLibrary.AddAssembly( typeof( Facepunch.ActionGraphs.ActionGraph ).Assembly, false );
		GlobalContext.Current.TypeLibrary.AddAssembly( typeof( TestInit ).Assembly, true );
	}
}
