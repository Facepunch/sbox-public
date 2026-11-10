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
		var context = GlobalContext.Current;
		context.TypeLibrary?.Dispose();

		var typeLibrary = context.TypeLibrary = new Internal.TypeLibrary();

		typeLibrary.ShouldExposePrivateMember = m => m.HasAttribute( typeof( RpcAttribute ) );
		typeLibrary.AddIntrinsicTypes();
		typeLibrary.AddAssembly( typeof( Vector3 ).Assembly, false );
		typeLibrary.AddAssembly( Game.GameAssembly, false );
		typeLibrary.AddAssembly( typeof( EngineLoop ).Assembly, false );
		typeLibrary.AddAssembly( typeof( Facepunch.ActionGraphs.ActionGraph ).Assembly, false );

		if ( !context.HasNodeLibrary )
		{
			NodeLibrary = new Facepunch.ActionGraphs.NodeLibrary( new TypeLoader( () => context.TypeLibrary ), new GraphLoader() );

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
