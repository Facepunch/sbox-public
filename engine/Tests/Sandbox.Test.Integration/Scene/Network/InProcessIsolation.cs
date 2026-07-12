using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox;
using Sandbox.Internal;
using Sandbox.Network;

namespace SceneTests;

/// <summary>
/// Exercises the per-client assembly isolation of docked in-process clients
/// (<see cref="InProcessTenant"/>): each session loads private copies of the game
/// assemblies in a collectible AssemblyLoadContext, so game statics are per-client,
/// scenes instantiate tenant-typed components from the real snapshot, sessions rebuild
/// on host code changes, and everything unloads when the session dies.
/// </summary>
[TestClass]
public class InProcessIsolationTests
{
	/// <summary>
	/// A minimal "game assembly": a component with static state, compiled at test time.
	/// Statics in game code are exactly what isolation exists to separate.
	/// </summary>
	const string ProbeSource = """
		public class ProbeComponent : Sandbox.Component
		{
			public static int Counter = 0;
			public static string Owner = "default";
		}
		""";

	static byte[] _probeBytes;

	static byte[] ProbeBytes => _probeBytes ??= Compile( "test.probe", ProbeSource );

	internal static byte[] DiagProbeBytes => ProbeBytes;

	/// <summary>
	/// Compile source against everything the test host has loaded (trusted platform
	/// assemblies include both the framework and the engine assemblies).
	/// </summary>
	static byte[] Compile( string assemblyName, string source )
	{
		var references = ((string)AppContext.GetData( "TRUSTED_PLATFORM_ASSEMBLIES" ))
			.Split( Path.PathSeparator )
			.Where( p => !string.IsNullOrWhiteSpace( p ) && File.Exists( p ) )
			.Select( p => (MetadataReference)MetadataReference.CreateFromFile( p ) )
			.ToList();

		var compilation = CSharpCompilation.Create(
			assemblyName,
			new[] { CSharpSyntaxTree.ParseText( source ) },
			references,
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );

		using var ms = new MemoryStream();
		var result = compilation.Emit( ms );

		Assert.IsTrue( result.Success, string.Join( "\n", result.Diagnostics.Where( d => d.Severity == DiagnosticSeverity.Error ) ) );

		return ms.ToArray();
	}

	static void SetStatic( Type type, string field, object value ) => type.GetField( field ).SetValue( null, value );
	static object GetStatic( Type type, string field ) => type.GetField( field ).GetValue( null );

	static void PumpUntilConnected( IEnumerable<InProcessClientSession> sessions, NetworkSystem host, int maxIterations = 256 )
	{
		var list = sessions.ToList();

		for ( var i = 0; i < maxIterations && !list.All( s => s.IsConnected ); i++ )
		{
			foreach ( var s in list )
				s.Tick();

			host.Tick();
		}
	}

	/// <summary>
	/// Everything a host-side test needs, torn down in reverse in Dispose.
	/// </summary>
	internal sealed class HostFixture : IDisposable
	{
		public NetworkSystem Host;
		public Scene HostScene;
		public Assembly HostProbe;
		public Type HostProbeType;

		readonly NetworkSystem _prevSystem = Networking.System;
		readonly SceneNetworkSystem _prevInstance = SceneNetworkSystem.Instance;
		readonly Connection _prevLocal = Connection.Local;
		readonly Scene _prevScene = Game.ActiveScene;
		readonly List<InProcessClientSession> _sessions = new();

		public HostFixture( string scenePath, bool withProbeComponent )
		{
			// The host gets its own copy of the probe assembly in the default context -
			// this plays the role of the game code the editor host runs.
			HostProbe = Assembly.Load( ProbeBytes );
			HostProbeType = HostProbe.GetType( "ProbeComponent" );
			GlobalGameNamespace.TypeLibrary.AddAssembly( HostProbe, true );

			Host = new NetworkSystem( "server", GlobalGameNamespace.TypeLibrary );
			Networking.System = Host;
			Host.InitializeHost();
			Host.GameSystem = new SceneNetworkSystem( GlobalGameNamespace.TypeLibrary, Host );

			// The camera matters: the docked client tab renders the scene through
			// Scene.Camera, so the snapshot must deliver a working camera or the tab is black.
			var objectJson = withProbeComponent
				? $$"""{ "Id": "{{Guid.NewGuid()}}", "Name": "ProbeObject", "Enabled": true, "Components": [ { "__type": "ProbeComponent", "__guid": "{{Guid.NewGuid()}}" } , { "__type": "CameraComponent", "__guid": "{{Guid.NewGuid()}}" } ] }"""
				: $$"""{ "Id": "{{Guid.NewGuid()}}", "Name": "ProbeObject", "Enabled": true }""";

			HostScene = Helpers.LoadSceneFromJson( scenePath, objectJson );

			Game.ActiveScene = HostScene;
			Game.IsPlaying = true;
		}

		public InProcessClientSession CreateIsolated( string name )
		{
			var session = InProcessClientSession.Create( name, new[] { ("test.probe", ProbeBytes) } );
			_sessions.Add( session );
			return session;
		}

		public void DropSession( InProcessClientSession session ) => _sessions.Remove( session );

		public InProcessClientSession CreateShared( string name )
		{
			// An explicitly empty assembly list forces shared mode - the test host has a
			// real game instance whose assemblies would otherwise be picked up.
			var session = InProcessClientSession.Create( name, Array.Empty<(string Name, byte[] Bytes)>() );
			_sessions.Add( session );
			return session;
		}

		public void Dispose()
		{
			foreach ( var s in _sessions )
			{
				try { s.Dispose(); } catch { }
			}

			try { Host?.Disconnect(); } catch { }

			HostScene?.Destroy();

			GlobalGameNamespace.TypeLibrary.RemoveAssembly( HostProbe );
			GlobalGameNamespace.TypeLibrary.ClearRemovedTypes();

			Networking.System = _prevSystem;
			SceneNetworkSystem.Instance = _prevInstance;
			Connection.Local = _prevLocal;
			Game.ActiveScene = _prevScene;
		}
	}

	[TestMethod]
	public void IsolatedTenants_HaveIndependentGameStatics()
	{
		using var fx = new HostFixture( "scenes/isolation_statics.scene", withProbeComponent: true );

		var s1 = fx.CreateIsolated( "Iso One" );
		var s2 = fx.CreateIsolated( "Iso Two" );

		Assert.IsTrue( s1.IsIsolated, "Session 1 should be isolated" );
		Assert.IsTrue( s2.IsIsolated, "Session 2 should be isolated" );

		PumpUntilConnected( new[] { s1, s2 }, fx.Host );

		Assert.IsTrue( s1.IsConnected, "Session 1 should connect" );
		Assert.IsTrue( s2.IsConnected, "Session 2 should connect" );

		//
		// Each tenant has its own copy of the game assembly: three distinct Types.
		//
		var t1 = s1.Tenant.Assemblies.Single().GetType( "ProbeComponent" );
		var t2 = s2.Tenant.Assemblies.Single().GetType( "ProbeComponent" );

		Assert.IsNotNull( t1 );
		Assert.IsNotNull( t2 );
		Assert.AreNotEqual( t1, t2, "Tenants must have distinct type identities" );
		Assert.AreNotEqual( t1, fx.HostProbeType, "Tenant type must differ from the host's" );

		// And each tenant's TypeLibrary resolves its own copy, not the host's.
		Assert.IsNotNull( s1.Tenant.TypeLibrary.GetType( t1 ), "Tenant 1 TypeLibrary should know its own type" );
		Assert.IsNull( s1.Tenant.TypeLibrary.GetType( fx.HostProbeType ), "Tenant 1 TypeLibrary should not know the host's type" );

		//
		// The whole point: statics are per-tenant. Write three different values through
		// three type identities, then read all three back - nothing bleeds.
		//
		SetStatic( fx.HostProbeType, "Counter", 1000 );
		SetStatic( t1, "Counter", 111 );
		SetStatic( t2, "Counter", 222 );

		SetStatic( fx.HostProbeType, "Owner", "host" );
		SetStatic( t1, "Owner", "client one" );
		SetStatic( t2, "Owner", "client two" );

		Assert.AreEqual( 1000, GetStatic( fx.HostProbeType, "Counter" ) );
		Assert.AreEqual( 111, GetStatic( t1, "Counter" ) );
		Assert.AreEqual( 222, GetStatic( t2, "Counter" ) );

		Assert.AreEqual( "host", GetStatic( fx.HostProbeType, "Owner" ) );
		Assert.AreEqual( "client one", GetStatic( t1, "Owner" ) );
		Assert.AreEqual( "client two", GetStatic( t2, "Owner" ) );

		//
		// End to end through the REAL snapshot: the host scene's ProbeComponent arrived
		// in each client scene as an instance of the TENANT's type, not the host's.
		//
		AssertSceneHasTenantComponent( s1, t1 );
		AssertSceneHasTenantComponent( s2, t2 );

		//
		// The camera survived the snapshot too - the docked tab renders through
		// Scene.Camera, so a missing camera means a black tab.
		//
		Assert.IsTrue( s1.Scene.Camera.IsValid(), "Tenant scene should have an active camera from the snapshot" );
		Assert.IsTrue( s2.Scene.Camera.IsValid(), "Tenant scene should have an active camera from the snapshot" );

		// Host globals restored.
		Assert.AreEqual( fx.Host, Networking.System, "Networking.System leaked" );
		Assert.AreEqual( fx.HostScene, Game.ActiveScene, "Game.ActiveScene leaked" );
	}

	static void AssertSceneHasTenantComponent( InProcessClientSession session, Type expectedType )
	{
		Assert.IsNotNull( session.Scene, "Client should have a scene" );

		var obj = session.Scene.Children.FirstOrDefault( x => x.Name == "ProbeObject" );
		Assert.IsNotNull( obj, "Client scene should contain the host's object" );

		var component = obj.Components.GetAll().FirstOrDefault( c => c.GetType().Name == "ProbeComponent" );
		Assert.IsNotNull( component, "Client scene object should have the ProbeComponent" );
		Assert.AreEqual( expectedType, component.GetType(), "Component must be the tenant's type, not the host's" );
	}

	[TestMethod]
	public void SharedFallback_WhenNoGameAssemblies()
	{
		using var fx = new HostFixture( "scenes/isolation_fallback.scene", withProbeComponent: false );

		// No game instance and no injected assemblies: the tenant must fall back to
		// shared mode and still work exactly like before isolation existed.
		var session = fx.CreateShared( "Fallback" );

		Assert.IsFalse( session.IsIsolated, "Session without game assemblies should run in shared mode" );

		PumpUntilConnected( new[] { session }, fx.Host );

		Assert.IsTrue( session.IsConnected, "Shared-mode session should connect" );
		Assert.IsNotNull( session.Scene, "Shared-mode session should receive a scene" );
		Assert.AreNotEqual( fx.HostScene, session.Scene );

		// Host code changes don't affect shared sessions - they run the host's assemblies.
		InProcessClientSession.NotifyHostCodeChanged();
		Assert.IsFalse( session.NeedsRebuild, "Shared-mode sessions must not rebuild on host code changes" );
	}

	[TestMethod]
	public void NotifyHostCodeChanged_FlagsIsolatedSessionsForRebuild()
	{
		using var fx = new HostFixture( "scenes/isolation_rebuild.scene", withProbeComponent: false );

		var isolated = fx.CreateIsolated( "Rebuild Me" );
		var shared = fx.CreateShared( "Leave Me" );

		PumpUntilConnected( new[] { isolated, shared }, fx.Host );

		Assert.IsTrue( isolated.IsConnected );
		Assert.IsTrue( shared.IsConnected );

		InProcessClientSession.NotifyHostCodeChanged();

		Assert.IsTrue( isolated.NeedsRebuild, "Isolated session must be flagged for rebuild on host code change" );
		Assert.IsFalse( shared.NeedsRebuild, "Shared session must not be flagged" );

		// The rebuild flow is dispose + recreate - do exactly that and verify the new
		// session comes up isolated and connected, like the editor tab does on hotload.
		isolated.Dispose();

		var rebuilt = fx.CreateIsolated( "Rebuilt" );
		PumpUntilConnected( new[] { rebuilt }, fx.Host );

		Assert.IsTrue( rebuilt.IsIsolated );
		Assert.IsTrue( rebuilt.IsConnected, "Rebuilt session should reconnect" );
		Assert.IsFalse( rebuilt.NeedsRebuild );
	}

	[TestMethod]
	public void DisposedSession_UnloadsTenantAssemblies()
	{
		using var fx = new HostFixture( "scenes/isolation_unload.scene", withProbeComponent: true );

		var weakAssembly = CreateConnectDispose( fx );

		// The tenant load context is collectible: once the session is disposed nothing
		// should root its assembly.
		AssertUnloads( weakAssembly, "Tenant assembly should unload after session disposal - something is rooting the collectible load context" );
	}

	/// <summary>
	/// Repeated GC passes because unload is asynchronous, with thread-pool flushes in
	/// between: idle pool workers can hold stale references to completed work items
	/// (TypeLibrary.AddAssembly parallelizes over the pool) in their local queues.
	/// Uses IsAlive, never TryGetTarget: materializing the target would write a strong
	/// reference into this frame's locals, which tier-0 JIT keeps alive until the method
	/// returns - rooting the assembly and making the test fail against itself.
	/// </summary>
	static void AssertUnloads( WeakReference weakAssembly, string message )
	{
		for ( var i = 0; i < 20 && weakAssembly.IsAlive; i++ )
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			var flush = new System.Threading.Tasks.Task[Environment.ProcessorCount];
			for ( var j = 0; j < flush.Length; j++ )
				flush[j] = System.Threading.Tasks.Task.Run( () => System.Threading.Thread.Sleep( 1 ) );
			System.Threading.Tasks.Task.WaitAll( flush );
		}

		Assert.IsFalse( weakAssembly.IsAlive, message );
	}

	[TestMethod]
	public void DisposedSession_UnloadsTenantAssemblies_WithoutConnecting()
	{
		// The bisect twin of the test above: no handshake, no scene - if this passes and
		// the connected variant fails, the rooting is in the connected path.
		using var fx = new HostFixture( "scenes/isolation_unload_noconnect.scene", withProbeComponent: true );

		var weakAssembly = CreateConnectDispose( fx, connect: false );

		AssertUnloads( weakAssembly, "Tenant assembly should unload after disposing a never-connected session" );
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	static WeakReference CreateConnectDispose( HostFixture fx, bool connect = true )
	{
		var session = fx.CreateIsolated( "Unload Me" );

		Assert.IsTrue( session.IsIsolated );

		if ( connect )
		{
			PumpUntilConnected( new[] { session }, fx.Host );
			Assert.IsTrue( session.IsConnected );
		}

		var weak = new WeakReference( session.Tenant.Assemblies.Single() );

		session.Dispose();

		// Mirror production: the editor widget drops its session reference right after
		// disposal - keeping the dead session rooted is not a scenario that exists.
		fx.DropSession( session );

		return weak;
	}

	[TestMethod]
	public void IsolatedSessions_ReplicatedStateStillFlows()
	{
		using var fx = new HostFixture( "scenes/isolation_network.scene", withProbeComponent: true );

		var s1 = fx.CreateIsolated( "Net One" );
		var s2 = fx.CreateIsolated( "Net Two" );

		PumpUntilConnected( new[] { s1, s2 }, fx.Host );

		Assert.IsTrue( s1.IsConnected && s2.IsConnected );

		//
		// Distinct identities on the host, exactly like the shared-mode multi-client test.
		//
		Assert.AreEqual( 2, fx.Host.Connections.Count() );
		Assert.AreEqual( 2, fx.Host.Connections.Select( c => c.Name ).Distinct().Count() );

		//
		// A new object created on the host reaches both isolated clients through the
		// normal object-create path - the wire protocol works across type worlds.
		//
		using ( fx.HostScene.Push() )
		{
			var go = new GameObject( true, "LateObject" );
			go.NetworkSpawn( null );
		}

		for ( var i = 0; i < 64; i++ )
		{
			s1.Tick();
			s2.Tick();
			fx.Host.Tick();

			if ( s1.Scene.Directory.FindByName( "LateObject" ).Any() &&
				 s2.Scene.Directory.FindByName( "LateObject" ).Any() )
				break;
		}

		Assert.IsTrue( s1.Scene.Directory.FindByName( "LateObject" ).Any(), "Client 1 should receive the late-spawned object" );
		Assert.IsTrue( s2.Scene.Directory.FindByName( "LateObject" ).Any(), "Client 2 should receive the late-spawned object" );
	}
}
