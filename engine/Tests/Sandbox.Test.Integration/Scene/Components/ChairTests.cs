using Sandbox.Movement;
using Sandbox.Network;
using System;

namespace SceneTests.Components;

/// <summary>
/// Pins the BaseChair contract that's reachable without networking: the property
/// surface, press/enter/leave gates, Sit parenting the player into the seat,
/// occupancy, Eject placement through the exit point scoring, the eye transform
/// priority chain with pitch/yaw clamping, the SitMoveMode handover, and
/// serialization. The networked entry points (Press -&gt; EnterChair, AskToLeave)
/// are ownership-gated and out of scope.
/// <br/><br/>
/// Sit and Eject are [Rpc.Broadcast( NetFlags.HostOnly )] methods: the codegen
/// wrapper routes them through Rpc.OnCallInstanceRpc, which silently skips the
/// local invocation unless Connection.Local.IsHost - and Rpc.Resume swallows any
/// exception the body throws. Other tests in this assembly (ClientAndHost,
/// NetworkTest) reassign Connection.Local / Networking.System and never restore
/// them, so this fixture pins a host connection around every test to stay
/// deterministic regardless of run order.
/// </summary>
[TestClass]
public class BaseChairTest
{
	Connection _previousLocalConnection;
	NetworkSystem _previousNetworkSystem;

	/// <summary>
	/// Pins Connection.Local to a host connection and clears Networking.System so the
	/// HostOnly broadcast RPCs (Sit, Eject) execute locally instead of being silently
	/// dropped when another test has leaked a non-host client connection.
	/// </summary>
	[TestInitialize]
	public void PinHostNetworkingState()
	{
		_previousLocalConnection = Connection.Local;
		_previousNetworkSystem = Networking.System;

		Connection.Local = new TestConnection( Guid.NewGuid(), isHost: true );
		Networking.System = null;
	}

	/// <summary>
	/// Restores whatever global networking state existed before the test, leaving the
	/// rest of the assembly exactly as it was.
	/// </summary>
	[TestCleanup]
	public void RestoreNetworkingState()
	{
		Connection.Local = _previousLocalConnection;
		Networking.System = _previousNetworkSystem;
	}

	/// <summary>
	/// Creates a large static floor whose top surface sits at z = 0.
	/// </summary>
	static GameObject CreateFloor( Scene scene )
	{
		var go = scene.CreateObject();
		go.Name = "Floor";
		go.WorldPosition = new Vector3( 0, 0, -50 );

		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 4000, 4000, 100 );
		box.Static = true;

		return go;
	}

	/// <summary>
	/// Creates a PlayerController at the given position, configured for headless
	/// ticking: no input controls and no footstep sounds.
	/// </summary>
	static PlayerController CreatePlayer( Scene scene, Vector3 position )
	{
		var go = scene.CreateObject();
		go.Name = "Player";
		go.WorldPosition = position;

		var pc = go.Components.Create<PlayerController>();
		pc.UseInputControls = false;
		pc.EnableFootstepSounds = false;

		return pc;
	}

	/// <summary>
	/// Creates a chair at the given position.
	/// </summary>
	static BaseChair CreateChair( Scene scene, Vector3 position )
	{
		var go = scene.CreateObject();
		go.Name = "Chair";
		go.WorldPosition = position;

		return go.Components.Create<BaseChair>();
	}

	/// <summary>
	/// Serializes a GameObject to json, destroys the original, then deserializes the
	/// json back into the scene and enables it - the standard round trip idiom.
	/// </summary>
	static GameObject SerializeRoundTrip( Scene scene, GameObject go )
	{
		var json = go.Serialize().ToJsonString();

		go.Destroy();
		scene.ProcessDeletes();

		var jsonObject = Json.ParseToJsonObject( json );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var clone = new GameObject( false );
		clone.Deserialize( jsonObject );
		clone.Enabled = true;

		return clone;
	}

	/// <summary>
	/// Pins the property defaults of a fresh chair, that it starts unoccupied, and
	/// that the tooltip is offered while free.
	/// </summary>
	[TestMethod]
	public void DefaultsAndTooltip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var chair = CreateChair( scene, Vector3.Zero );

		Assert.IsNull( chair.SeatPosition );
		Assert.IsNull( chair.EyePosition );
		Assert.IsNull( chair.ExitPoints );
		Assert.AreEqual( BaseChair.AnimatorSitPose.Chair, chair.SitPose );
		Assert.AreEqual( 0f, chair.SitHeight );
		Assert.AreEqual( new Vector2( -90, 70 ), chair.PitchRange );
		Assert.AreEqual( new Vector2( -120, 120 ), chair.YawRange );
		Assert.AreEqual( "Sit", chair.TooltipTitle );
		Assert.AreEqual( "airline_seat_recline_normal", chair.TooltipIcon );
		Assert.AreEqual( "", chair.TooltipDescription );

		Assert.IsFalse( chair.IsOccupied );
		Assert.IsNull( chair.GetOccupant() );

		var tooltip = chair.GetTooltip( new Component.IPressable.Event( null ) );
		Assert.IsTrue( tooltip.HasValue, "a free chair should offer its tooltip" );
		Assert.AreEqual( "Sit", tooltip.Value.Title );
		Assert.AreEqual( "airline_seat_recline_normal", tooltip.Value.Icon );

		chair.TooltipTitle = "";
		chair.TooltipIcon = "";
		Assert.IsFalse( chair.GetTooltip( new Component.IPressable.Event( null ) ).HasValue,
			"clearing title and icon disables the tooltip" );
	}

	/// <summary>
	/// CanPress only accepts a PlayerController source, and CanEnter refuses null
	/// players while CanLeave always allows leaving.
	/// </summary>
	[TestMethod]
	public void PressAndEnterGates()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var chair = CreateChair( scene, Vector3.Zero );
		var player = CreatePlayer( scene, new Vector3( 50, 0, 0 ) );

		Assert.IsFalse( chair.CanPress( new Component.IPressable.Event( chair ) ),
			"a non-player source can't press the chair" );
		Assert.IsFalse( chair.CanPress( new Component.IPressable.Event( null ) ) );
		Assert.IsTrue( chair.CanPress( new Component.IPressable.Event( player ) ),
			"a player can press a free chair" );

		Assert.IsFalse( chair.CanEnter( null ) );
		Assert.IsTrue( chair.CanEnter( player ) );
		Assert.IsTrue( chair.CanLeave( player ) );
	}

	/// <summary>
	/// Sit parents the player to the seat at local zero, disables the player's body
	/// and colliders, and the chair becomes occupied: the occupant is reported,
	/// further players are refused and the tooltip disappears. Sit only executes
	/// locally because the fixture guarantees Connection.Local.IsHost.
	/// </summary>
	[TestMethod]
	public void SitSeatsThePlayer()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var chair = CreateChair( scene, new Vector3( 100, 50, 20 ) );
		var player = CreatePlayer( scene, Vector3.Zero );
		var other = CreatePlayer( scene, new Vector3( 0, 200, 0 ) );

		chair.Sit( player );

		Assert.AreEqual( chair.GameObject, player.GameObject.Parent,
			"without a SeatPosition the player is parented to the chair itself" );
		Assert.AreEqual( global::Transform.Zero, player.GameObject.LocalTransform );
		Assert.IsTrue( player.WorldPosition.Distance( chair.WorldPosition ) < 0.01f );
		Assert.IsFalse( player.Body.Enabled, "sitting disables the player's rigidbody" );
		Assert.IsFalse( player.ColliderObject.Enabled, "sitting disables the player's colliders" );

		Assert.IsTrue( chair.IsOccupied );
		Assert.AreEqual( player, chair.GetOccupant() );
		Assert.IsFalse( chair.CanEnter( other ), "an occupied chair refuses other players" );
		Assert.IsFalse( chair.CanPress( new Component.IPressable.Event( other ) ) );
		Assert.IsFalse( chair.GetTooltip( new Component.IPressable.Event( other ) ).HasValue,
			"an occupied chair offers no tooltip" );
	}

	/// <summary>
	/// With a SeatPosition assigned the player is parented to that GameObject
	/// instead of the chair root, and lands exactly on its world transform. Relies
	/// on the fixture's pinned host connection so Sit runs locally.
	/// </summary>
	[TestMethod]
	public void SitUsesSeatPosition()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var chair = CreateChair( scene, new Vector3( 100, 0, 0 ) );

		var seat = scene.CreateObject();
		seat.Name = "Seat";
		seat.Parent = chair.GameObject;
		seat.LocalPosition = new Vector3( 0, 10, 14 );
		chair.SeatPosition = seat;

		var player = CreatePlayer( scene, Vector3.Zero );

		chair.Sit( player );

		Assert.AreEqual( seat, player.GameObject.Parent );
		Assert.IsTrue( player.WorldPosition.Distance( seat.WorldPosition ) < 0.01f );
		Assert.AreEqual( player, chair.GetOccupant(), "the occupant search includes seat children" );
	}

	/// <summary>
	/// Eject only acts on the current occupant: a non-occupant is ignored, while the
	/// occupant is unparented and placed at the seat position when no exit points
	/// are configured. Like Sit, Eject is a HostOnly broadcast RPC and relies on the
	/// fixture's pinned host connection to run locally.
	/// </summary>
	[TestMethod]
	public void EjectRequiresTheOccupant()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var chair = CreateChair( scene, new Vector3( 100, 50, 20 ) );
		var player = CreatePlayer( scene, Vector3.Zero );
		var bystander = CreatePlayer( scene, new Vector3( 0, 300, 0 ) );

		chair.Sit( player );

		chair.Eject( bystander );
		Assert.AreEqual( player, chair.GetOccupant(), "ejecting a non-occupant does nothing" );
		Assert.AreEqual( new Vector3( 0, 300, 0 ), bystander.WorldPosition );

		chair.Eject( player );

		Assert.IsFalse( chair.IsOccupied );
		Assert.AreNotEqual( chair.GameObject, player.GameObject.Parent, "ejecting unparents the player" );
		Assert.IsTrue( player.WorldPosition.Distance( chair.WorldPosition ) < 0.01f,
			"without exit points the player is placed at the seat" );
	}

	/// <summary>
	/// FindBestExitPoint scores exit points by how much they're in front of the
	/// chair (no occupant means the chair's own forward) - the point ahead wins over
	/// points behind or to the side, and no points falls back to the seat position.
	/// </summary>
	[TestMethod]
	public void ExitPointSelection()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var chair = CreateChair( scene, new Vector3( 10, 20, 30 ) );

		Assert.AreEqual( chair.WorldPosition, chair.FindBestExitPoint(),
			"no exit points falls back to the seat position" );

		GameObject MakeExit( string name, Vector3 position )
		{
			var go = scene.CreateObject();
			go.Name = name;
			go.Parent = chair.GameObject;
			go.WorldPosition = position;
			return go;
		}

		var behind = MakeExit( "Behind", chair.WorldPosition + new Vector3( -100, 0, 0 ) );
		var front = MakeExit( "Front", chair.WorldPosition + new Vector3( 100, 0, 0 ) );
		var side = MakeExit( "Side", chair.WorldPosition + new Vector3( 0, 100, 0 ) );

		chair.ExitPoints = new[] { behind, front, side };

		Assert.AreEqual( front.WorldPosition, chair.FindBestExitPoint(),
			"the most-forward exit point should win" );
	}

	/// <summary>
	/// GetEyeTransform prefers EyePosition, then SeatPosition, then the chair
	/// itself - and CalculateEyeTransform clamps the player's eye angles into
	/// PitchRange/YawRange and composes the rotation with the chair's rotation.
	/// </summary>
	[TestMethod]
	public void EyeTransformPriorityAndClamping()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var chair = CreateChair( scene, new Vector3( 50, 60, 70 ) );
		chair.GameObject.WorldRotation = Rotation.FromYaw( 90 );

		var seat = scene.CreateObject();
		seat.Parent = chair.GameObject;
		seat.LocalPosition = new Vector3( 10, 0, 5 );

		var eyes = scene.CreateObject();
		eyes.Parent = chair.GameObject;
		eyes.LocalPosition = new Vector3( 0, 0, 24 );

		Assert.IsTrue( chair.GetEyeTransform().Position.Distance( chair.WorldPosition ) < 0.01f,
			"with nothing assigned the chair's own transform is the eye transform" );

		chair.SeatPosition = seat;
		Assert.IsTrue( chair.GetEyeTransform().Position.Distance( seat.WorldPosition ) < 0.01f,
			"the seat is used when no eye position exists" );

		chair.EyePosition = eyes;
		Assert.IsTrue( chair.GetEyeTransform().Position.Distance( eyes.WorldPosition ) < 0.01f,
			"an explicit eye position wins over the seat" );

		var player = CreatePlayer( scene, Vector3.Zero );

		player.EyeAngles = new Angles( 89, 130, 0 );
		var tx = chair.CalculateEyeTransform( player );

		Assert.AreEqual( 70f, player.EyeAngles.pitch, 0.01f, "pitch clamps to the PitchRange maximum" );
		Assert.AreEqual( 120f, player.EyeAngles.yaw, 0.01f, "yaw clamps to the YawRange maximum" );
		Assert.IsTrue( tx.Position.Distance( eyes.WorldPosition ) < 0.01f );

		var expected = chair.WorldRotation * new Angles( 70, 120, 0 ).ToRotation();
		Assert.IsTrue( tx.Rotation.Distance( expected ) < 0.1f,
			"the eye rotation composes the chair rotation with the clamped eye angles" );

		player.EyeAngles = new Angles( -95, -130, 0 );
		chair.CalculateEyeTransform( player );

		Assert.AreEqual( -90f, player.EyeAngles.pitch, 0.01f, "pitch clamps to the PitchRange minimum" );
		Assert.AreEqual( -120f, player.EyeAngles.yaw, 0.01f, "yaw clamps to the YawRange minimum" );
	}

	/// <summary>
	/// A player with a SitMoveMode hands control to it once seated (it scores 10000
	/// while parented under an ISitTarget), and ejecting hands control back to the
	/// walk mode, re-enabling the body and colliders. The Sit/Eject parenting only
	/// happens because the fixture pins a host connection for the HostOnly RPCs.
	/// </summary>
	[TestMethod]
	public void SitModeTakesOverWhileSeated()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );
		var chair = CreateChair( scene, new Vector3( 100, 0, 1 ) );
		var player = CreatePlayer( scene, new Vector3( 0, 0, 10 ) );

		player.GameObject.Components.Create<SitMoveMode>();

		for ( int i = 0; i < 60 && !player.IsOnGround; i++ )
		{
			scene.GameTick();
		}

		Assert.IsTrue( player.IsOnGround );
		Assert.IsTrue( player.Mode is MoveModeWalk );

		chair.Sit( player );

		for ( int i = 0; i < 10 && player.Mode is not SitMoveMode; i++ )
		{
			scene.GameTick();
		}

		Assert.IsTrue( player.Mode is SitMoveMode, $"sitting should hand control to the sit mode: {player.Mode}" );
		Assert.IsTrue( chair.IsOccupied );

		chair.Eject( player );

		for ( int i = 0; i < 10 && player.Mode is not MoveModeWalk; i++ )
		{
			scene.GameTick();
		}

		Assert.IsTrue( player.Mode is MoveModeWalk, $"ejecting should hand control back to walking: {player.Mode}" );
		Assert.IsFalse( chair.IsOccupied );
		Assert.IsTrue( player.Body.Enabled, "leaving the sit mode re-enables the body" );
		Assert.IsTrue( player.ColliderObject.Enabled, "leaving the sit mode re-enables the colliders" );
	}

	/// <summary>
	/// The chair's tuned properties - seat/eye/exit GameObject references included -
	/// survive a serialize/deserialize round trip, with the references resolving to
	/// the cloned children.
	/// </summary>
	[TestMethod]
	public void SerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var chair = CreateChair( scene, new Vector3( 5, 6, 7 ) );
		var go = chair.GameObject;

		var seat = scene.CreateObject();
		seat.Name = "Seat";
		seat.Parent = go;
		seat.LocalPosition = new Vector3( 0, 0, 14 );

		var eyes = scene.CreateObject();
		eyes.Name = "Eyes";
		eyes.Parent = go;
		eyes.LocalPosition = new Vector3( 0, 0, 40 );

		var exitA = scene.CreateObject();
		exitA.Name = "ExitA";
		exitA.Parent = go;
		exitA.LocalPosition = new Vector3( 30, 0, 0 );

		var exitB = scene.CreateObject();
		exitB.Name = "ExitB";
		exitB.Parent = go;
		exitB.LocalPosition = new Vector3( -30, 0, 0 );

		chair.SeatPosition = seat;
		chair.EyePosition = eyes;
		chair.ExitPoints = new[] { exitA, exitB };
		chair.SitPose = BaseChair.AnimatorSitPose.GroundCrossed;
		chair.SitHeight = 0.5f;
		chair.PitchRange = new Vector2( -10, 10 );
		chair.YawRange = new Vector2( -20, 20 );
		chair.TooltipTitle = "Take a seat";
		chair.TooltipIcon = "chair";
		chair.TooltipDescription = "comfy";

		var clone = SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<BaseChair>();

		Assert.IsNotNull( loaded, "the deserialized GameObject should have a BaseChair" );
		Assert.AreEqual( BaseChair.AnimatorSitPose.GroundCrossed, loaded.SitPose );
		Assert.AreEqual( 0.5f, loaded.SitHeight );
		Assert.AreEqual( new Vector2( -10, 10 ), loaded.PitchRange );
		Assert.AreEqual( new Vector2( -20, 20 ), loaded.YawRange );
		Assert.AreEqual( "Take a seat", loaded.TooltipTitle );
		Assert.AreEqual( "chair", loaded.TooltipIcon );
		Assert.AreEqual( "comfy", loaded.TooltipDescription );

		Assert.IsTrue( loaded.SeatPosition.IsValid(), "the seat reference should resolve" );
		Assert.AreEqual( "Seat", loaded.SeatPosition.Name );
		Assert.AreEqual( clone, loaded.SeatPosition.Parent, "the seat should resolve to the cloned child" );

		Assert.IsTrue( loaded.EyePosition.IsValid() );
		Assert.AreEqual( "Eyes", loaded.EyePosition.Name );

		Assert.AreEqual( 2, loaded.ExitPoints.Length );
		Assert.AreEqual( "ExitA", loaded.ExitPoints[0].Name );
		Assert.AreEqual( "ExitB", loaded.ExitPoints[1].Name );

		clone.Destroy();
		scene.ProcessDeletes();
	}
}
