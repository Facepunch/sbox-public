using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace MountingTests;

[TestClass]
public class MountedPrefabReferenceTests
{
	[TestInitialize]
	public async Task RegisterWithoutMounting()
	{
		Sandbox.Mounting.Directory.AddAssembly( GetType().Assembly );

		// An earlier test may have left it to be mounted again
		await Sandbox.Mounting.Directory.SetMountState( "testgame", false );
		Assert.IsFalse( Sandbox.Mounting.Directory.Get( "testgame" ).IsMounted );
	}

	[TestCleanup]
	public void Unregister()
	{
		Sandbox.Mounting.Directory.RemoveAssembly( GetType().Assembly );
	}

	[TestMethod]
	[DataRow( "mount://testgame/prefabs/thing.prefab" )]
	[DataRow( "mount://nosuchgame/prefabs/thing.prefab" )]
	public void ItResolvesToNothing( string path )
	{
		var scene = new Scene();
		using var scope = scene.Push();

		Assert.IsNull( GameObjectReference.FromPrefabPath( path ).Resolve( scene ) );
	}

	[TestMethod]
	public void AnObjectThatNamesOneArrivesWithTheReferenceEmpty()
	{
		var messages = new List<string>();
		void OnMessage( LogEvent e ) => messages.Add( e.Message );

		var scene = new Scene();
		using var scope = scene.Push();

		var sent = scene.CreateObject();
		sent.Components.Create<BaseCombatWeapon>().DisplayName = "Sent";
		var json = sent.Serialize();
		var component = json["Components"].AsArray()[0].AsObject();
		component["ViewModelPrefab"] = new JsonObject { ["_type"] = "gameobject", ["prefab"] = "mount://testgame/prefabs/viewmodel.prefab" };
		sent.DestroyImmediate();

		Sandbox.Diagnostics.Logging.OnMessage += OnMessage;

		try
		{
			var received = scene.CreateObject();
			received.Deserialize( json );

			var weapon = received.Components.Get<BaseCombatWeapon>();
			Assert.IsNotNull( weapon );
			Assert.AreEqual( "Sent", weapon.DisplayName );
			Assert.IsNull( weapon.ViewModelPrefab );
			Assert.IsFalse( messages.Any( x => x.Contains( "Error when deserializing" ) ), string.Join( " | ", messages ) );
		}
		finally
		{
			Sandbox.Diagnostics.Logging.OnMessage -= OnMessage;
		}
	}
}
