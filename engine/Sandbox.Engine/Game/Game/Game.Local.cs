using Sandbox.ActionGraphs;
using Sandbox.Engine;
using System.Reflection;

namespace Sandbox;

public static partial class Game
{
	internal static Assembly GameAssembly => GlobalContext.Current.LocalAssembly;
	internal static ResourceSystem Resources => GlobalContext.Current.ResourceSystem;

	internal static void InitHost()
	{
		if ( Bootstrap.ConsumePreparedTypeLibrary() ) return;

		InitTypeLibrary();
	}

	internal static void InitTypeLibrary()
	{
		TypeLibrary?.Dispose();

		GlobalContext.Current.TypeLibrary = new Internal.TypeLibrary();

		TypeLibrary.ShouldExposePrivateMember = m => m.HasAttribute( typeof( RpcAttribute ) );
		TypeLibrary.AddIntrinsicTypes();
		TypeLibrary.AddAssembly( typeof( Vector3 ).Assembly, false );
		TypeLibrary.AddAssembly( Game.GameAssembly, false );
		TypeLibrary.AddAssembly( typeof( EngineLoop ).Assembly, false );
		TypeLibrary.AddAssembly( typeof( Facepunch.ActionGraphs.ActionGraph ).Assembly, false );

		var context = GlobalContext.Current;
		if ( !context.HasNodeLibrary )
		{
			NodeLibrary = new Facepunch.ActionGraphs.NodeLibrary( new TypeLoader( () => TypeLibrary ), new GraphLoader() );

			// Immediately log exceptions thrown by async tasks returning void,
			// instead of spamming TaskScheduler.UnobservedTaskExceptions

			NodeLibrary.VoidTaskFaulted += ( _, e ) => Log.Error( e );
		}

		context.ResetNodeLibrary();

		AddNodesFromAssembly( typeof( Vector3 ).Assembly );
		AddNodesFromAssembly( typeof( LogNodes ).Assembly );
		AddNodesFromAssembly( Game.GameAssembly );

		Json.Initialize();
	}

}
