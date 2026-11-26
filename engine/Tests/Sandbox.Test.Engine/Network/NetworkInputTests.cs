using NativeEngine;
using InputRouter = Sandbox.Engine.InputRouter;
using Sandbox.Network;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace NetworkTests;

/// <summary>
/// Networked actions and analog input, including short taps between sends.
/// </summary>
[TestClass]
[DoNotParallelize]
public class NetworkInputTests
{
	/// <summary>
	/// Named actions, controller axes, camera aim and mouse deltas survive the wire format.
	/// </summary>
	[TestMethod]
	public void UserCommandRoundTrip()
	{
		var command = new UserCommand( 7 )
		{
			Actions = 1UL << 63,
			AimAngles = new Angles( 15, 90, 0 ),
			MouseDelta = new Vector2( -2, 3 ),
			MouseWheel = new Vector2( 0.25f, -0.5f )
		};

		command.SetAnalog( InputAnalog.LeftStickX, -0.75f );
		command.SetAnalog( InputAnalog.RightTrigger, 0.375f );
		command.SetAnalog( InputAnalog.LeftStickY, 2f );
		command.SetAnalog( InputAnalog.RightStickX, float.NaN );

		var received = RoundTrip( command );
		Assert.AreEqual( command.AimAngles, received.AimAngles );
		Assert.AreEqual( command.MouseDelta, received.MouseDelta );
		Assert.AreEqual( command.MouseWheel, received.MouseWheel );
		Assert.AreEqual( command.CommandNumber, received.CommandNumber );
		Assert.AreEqual( command.Actions, received.Actions );

		Assert.AreEqual( -0.75f, received.GetAnalog( InputAnalog.LeftStickX ), 0.00004f );
		Assert.AreEqual( 0.375f, received.GetAnalog( InputAnalog.RightTrigger ), 0.00004f );
		Assert.AreEqual( 1f, received.GetAnalog( InputAnalog.LeftStickY ) );
		Assert.AreEqual( 0f, received.GetAnalog( InputAnalog.RightStickX ) );
		Assert.AreEqual( 0f, received.GetAnalog( (InputAnalog)100 ) );
	}

	/// <summary>
	/// Optional sections and individual axes have the expected size and clear omitted input when reading.
	/// </summary>
	[TestMethod]
	[DataRow( false, 0, 6 )]
	[DataRow( true, 0, 14 )]
	[DataRow( false, 1, 8 )]
	[DataRow( true, 32, 16 )]
	[DataRow( false, 12, 10 )]
	[DataRow( true, 63, 26 )]
	public void OptionalInputSections( bool actions, int axisMask, int expectedBytes )
	{
		var command = new UserCommand( 42 ) { Actions = actions ? 1UL << 63 : 0 };

		for ( var i = 0; i < UserCommand.AnalogCount; i++ )
		{
			if ( (axisMask & (1 << i)) != 0 )
				command.SetAnalog( (InputAnalog)i, (i + 1) / 8f );
		}

		// Reuse a populated command: omitted fields must clear, rather than retain the previous sample.
		var received = new UserCommand( 1 ) { Actions = ulong.MaxValue };
		for ( var i = 0; i < UserCommand.AnalogCount; i++ )
		{
			received.SetAnalog( (InputAnalog)i, 1f );
		}

		received = RoundTrip( command, expectedBytes, received );
		Assert.AreEqual( command.CommandNumber, received.CommandNumber );
		Assert.AreEqual( command.Actions, received.Actions );

		for ( var i = 0; i < UserCommand.AnalogCount; i++ )
		{
			Assert.AreEqual( command.GetAnalog( (InputAnalog)i ), received.GetAnalog( (InputAnalog)i ) );
		}
	}

	/// <summary>
	/// Held keys use a count and one code per key, including when the count exceeds one byte.
	/// An empty list clears previously received keys without adding payload bytes.
	/// </summary>
	[TestMethod]
	[DataRow( 0, 6 )]
	[DataRow( 1, 9 )]
	[DataRow( 3, 13 )]
	[DataRow( 255, 517 )]
	[DataRow( 256, 520 )]
	[DataRow( 320, 648 )]
	public void HeldKeyListRoundTrip( int count, int expectedBytes )
	{
		var command = new UserCommand( 7 ) { Keys = new ushort[count] };
		for ( var i = 0; i < count; i++ )
			command.Keys[i] = (ushort)((int)ButtonCode.MOUSE_LAST - count + 1 + i);

		var previous = new UserCommand( 6 ) { Keys = [(ushort)ButtonCode.KEY_W] };
		var received = RoundTrip( command, expectedBytes, previous );
		CollectionAssert.AreEqual( command.Keys, received.Keys ?? [] );
	}

	/// <summary>
	/// Reordered and repeated codes reuse the same key snapshot. A later change preserves the earlier sample.
	/// </summary>
	[TestMethod]
	public void HeldKeySnapshots()
	{
		var command = new UserCommand( 1 );
		var k = (ushort)ButtonCode.KEY_K;
		var ctrl = (ushort)ButtonCode.KEY_LCONTROL;
		command.SetKeys( [ctrl, k], null );
		var first = command.Keys;

		command.SetKeys( [k, ctrl, k, 0, ushort.MaxValue], first );
		Assert.AreSame( first, command.Keys );
		command.SetKeys( [ctrl], first );
		CollectionAssert.AreEqual( new[] { k, ctrl }, first );
		CollectionAssert.AreEqual( new[] { ctrl }, command.Keys );
	}

	/// <summary>
	/// Mouse movement, scrolling and camera aim can each be sent independently.
	/// </summary>
	[TestMethod]
	[DataRow( true, false, false, 14 )]
	[DataRow( false, true, false, 14 )]
	[DataRow( false, false, true, 18 )]
	[DataRow( true, true, true, 34 )]
	public void OptionalMouseAndAim( bool movement, bool scroll, bool aim, int expectedBytes )
	{
		var command = new UserCommand( 1 )
		{
			MouseDelta = movement ? new Vector2( -2, 0 ) : default,
			MouseWheel = scroll ? new Vector2( 0, 0.25f ) : default,
			AimAngles = aim ? Angles.Zero : null
		};

		var received = RoundTrip( command, expectedBytes );
		Assert.AreEqual( command.MouseDelta, received.MouseDelta );
		Assert.AreEqual( command.MouseWheel, received.MouseWheel );
		Assert.AreEqual( command.AimAngles, received.AimAngles );
	}

	/// <summary>
	/// Command wrapping accepts new input, and a host reset clears held values as well as pending edges.
	/// </summary>
	[TestMethod]
	public void WrapAndReset()
	{
		var connection = new MockConnection( Guid.NewGuid() );
		connection.Input.ApplyUserCommand( new UserCommand( 0x7FFFFFFF ) );
		connection.Input.ApplyUserCommand( new UserCommand( 0xFFFFFFFE ) );

		var command = new UserCommand( uint.MaxValue ) { Actions = 1 };
		command.SetAnalog( InputAnalog.LeftStickX, -1f );
		connection.Input.ApplyUserCommand( command );
		connection.Input.ApplyUserCommand( new UserCommand( 0 ) );

		Assert.AreEqual( 1UL, connection.Input.GetCurrentContext().Released );
		command = new UserCommand( 1 ) { Actions = 1 };
		command.SetAnalog( InputAnalog.LeftStickX, 1f );
		connection.Input.ApplyUserCommand( command );
		connection.Input.Clear();

		Assert.AreEqual( 0UL, connection.Input.Command.Actions );
		Assert.AreEqual( 0f, connection.GetAnalog( InputAnalog.LeftStickX ) );
	}

	/// <summary>
	/// Frame and physics updates cannot consume action taps before the host receives them.
	/// </summary>
	[TestMethod]
	[DataRow( "keyboard" )]
	[DataRow( "controller" )]
	[DataRow( "trigger" )]
	[DataRow( "game-frame" )]
	[DataRow( "game-physics" )]
	public void ActionTapsSurviveUntilNetworkSend( string source )
	{
		using var input = new ActionCaptureScope();
		var action = source == "trigger" ? "Attack1" : "Jump";

		switch ( source )
		{
			case "keyboard":
				Input.OnButton( ButtonCode.KEY_SPACE, "space", true );
				Input.OnButton( ButtonCode.KEY_SPACE, "space", false );
				break;
			case "controller":
				InputRouter.OnGameControllerButton( input.Controller.DeviceId, GameControllerCode.A, true );
				InputRouter.OnGameControllerButton( input.Controller.DeviceId, GameControllerCode.A, false );
				break;
			case "trigger":
				InputRouter.OnGameControllerAxis( input.Controller.DeviceId, GameControllerAxis.TriggerRight, 32767 );
				InputRouter.OnGameControllerAxis( input.Controller.DeviceId, GameControllerAxis.TriggerRight, 0 );
				break;
			case "game-frame":
				Input.SetAction( action, true );
				Input.SetAction( action, false );
				break;
			case "game-physics":
				var physics = Input.Context.Create( "Test.Physics" );
				using ( physics.Push() )
				{
					Input.SetAction( action, true );
				}
				physics.Flip();
				break;
		}

		for ( var i = 0; i < 3; i++ )
		{
			input.Frame.Flip();
			input.Controller.InputContext.Flip();
		}

		Assert.IsFalse( Input.Down( action ) );
		var remote = new MockConnection( Guid.NewGuid() );
		remote.Input.ApplyUserCommand( RoundTrip( CaptureCommand( 1 ) ) );
		Assert.IsTrue( remote.Down( action ) );
		Assert.IsTrue( remote.Pressed( action ) );

		remote.Input.ClearUpdateContext();
		remote.Input.ApplyUserCommand( RoundTrip( CaptureCommand( 2 ) ) );
		Assert.IsFalse( remote.Down( action ) );
		Assert.IsTrue( remote.Released( action ) );

		remote.Input.ClearUpdateContext();
		remote.Input.ApplyUserCommand( RoundTrip( CaptureCommand( 3 ) ) );
		Assert.IsFalse( remote.Pressed( action ) );
		Assert.IsFalse( remote.Released( action ) );
	}

	/// <summary>
	/// The final press wins over an earlier release, both within a frame and between frames before a send.
	/// </summary>
	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void RepressBetweenSendsStaysHeld( bool controller, bool separateFrames )
	{
		using var input = new ActionCaptureScope();
		var jump = 1UL << Input.GetActionIndex( "Jump" );

		foreach ( var down in new[] { true, false, true } )
		{
			if ( controller )
			{
				InputRouter.OnGameControllerButton( input.Controller.DeviceId, GameControllerCode.A, down );
			}
			else
			{
				Input.OnButton( ButtonCode.KEY_SPACE, "space", down );
			}

			if ( separateFrames )
			{
				input.Frame.Flip();
				input.Controller.InputContext.Flip();
			}
		}

		for ( var i = 0; i < 3; i++ )
		{
			input.Frame.Flip();
			input.Controller.InputContext.Flip();
			var command = CaptureCommand();
			Assert.AreEqual( jump, command.Actions );
		}
	}

	/// <summary>
	/// Explicit releases also cancel controller presses waiting for a network send.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ReleaseCancelsPendingControllerActions( bool releaseAll )
	{
		using var input = new ActionCaptureScope();
		InputRouter.OnGameControllerButton( input.Controller.DeviceId, GameControllerCode.A, true );
		InputRouter.OnGameControllerButton( input.Controller.DeviceId, GameControllerCode.X, true );

		if ( releaseAll )
		{
			Input.ReleaseActions();
		}
		else
		{
			Input.ReleaseAction( "Jump" );
		}

		input.Controller.InputContext.Flip();
		var expected = releaseAll ? 0UL : 1UL << Input.GetActionIndex( "Reload" );
		Assert.AreEqual( expected, CaptureCommand().Actions );
		Assert.AreEqual( expected, CaptureCommand().Actions );
	}

	/// <summary>
	/// Releasing one physical source does not release another, and game overrides do not become sticky.
	/// </summary>
	[TestMethod]
	public void ActionSourcesAndGameOverrides()
	{
		using var input = new ActionCaptureScope();
		var jump = 1UL << Input.GetActionIndex( "Jump" );
		input.Network.AccumActionsPressed = jump;
		input.Frame.AccumActionsPressed = jump;
		input.Frame.Flip();
		InputRouter.OnGameControllerButton( input.Controller.DeviceId, GameControllerCode.A, true );
		InputRouter.OnGameControllerButton( input.Controller.DeviceId, GameControllerCode.A, false );
		Assert.AreEqual( jump, CaptureCommand().Actions );
		Assert.AreEqual( jump, CaptureCommand().Actions );

		Input.SetAction( "Jump", false );
		Assert.AreEqual( 0UL, CaptureCommand().Actions );
		input.Frame.Flip();
		Assert.AreEqual( jump, CaptureCommand().Actions );

		Input.SetAction( "Jump", true );
		Input.Clear( "Jump" );
		Assert.AreEqual( 0UL, CaptureCommand().Actions );
		Input.SetAction( "Jump", true );
		Input.ClearActions();
		Assert.AreEqual( 0UL, CaptureCommand().Actions );
		input.Frame.Flip();
		Assert.AreEqual( jump, CaptureCommand().Actions );

		Input.SetAction( "Jump", true );
		Input.ReleaseAction( "Jump" );
		Assert.AreEqual( 0UL, CaptureCommand().Actions );
		input.Frame.Flip();
		Assert.AreEqual( 0UL, CaptureCommand().Actions );

		Input.SetAction( "Jump", true );
		Input.Suppressed = true;
		Assert.AreEqual( 0UL, CaptureCommand().Actions );
		input.Frame.Flip();
		Input.Suppressed = false;
		Assert.AreEqual( 0UL, CaptureCommand().Actions );
	}

	/// <summary>
	/// Clearing a physical action overrides it, while releasing a game-set tap preserves its pending press.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ActionOverridesPreserveSourceSemantics( bool gameSet )
	{
		using var input = new ActionCaptureScope();
		Input.OnButton( ButtonCode.KEY_SPACE, "space", true );
		input.Frame.Flip();

		if ( gameSet )
			Input.SetAction( "Jump", true );

		Input.SetAction( "Jump", false );
		var expected = gameSet ? 1UL << Input.GetActionIndex( "Jump" ) : 0;
		Assert.AreEqual( expected, CaptureCommand().Actions );
		Assert.AreEqual( 0UL, CaptureCommand().Actions );
	}

	/// <summary>
	/// Actions follow the same keyboard/first-controller/explicit-controller selection as local input.
	/// </summary>
	[TestMethod]
	[DataRow( -1, "Jump" )]
	[DataRow( 0, "Forward" )]
	[DataRow( 1, "Jump" )]
	[DataRow( 2, "Reload" )]
	public void NetworkActionsRespectPlayerScope( int player, string expected )
	{
		using var input = new ActionCaptureScope();
		var second = new Controller( 4097 );
		Controller.All.Add( second );
		input.Network.AccumActionsPressed = 1UL << Input.GetActionIndex( "Forward" );
		InputRouter.OnGameControllerButton( input.Controller.DeviceId, GameControllerCode.A, true );
		InputRouter.OnGameControllerButton( second.DeviceId, GameControllerCode.X, true );

		using var scope = Input.PlayerScope( (Input.PlayerIndex)player );
		var actions = CaptureCommand().Actions;
		var expectedActions = 1UL << Input.GetActionIndex( expected );
		if ( player == -1 ) expectedActions |= 1UL << Input.GetActionIndex( "Forward" );
		Assert.AreEqual( expectedActions, actions );

		Input.ReleaseActions();
		Assert.AreEqual( 0UL, CaptureCommand().Actions );
	}

	/// <summary>
	/// Captured keys resolve against both live binding slots. Chords match exactly, independent of order.
	/// </summary>
	[TestMethod]
	public void CapturedKeyboardBinding()
	{
		using var scope = new ActionCaptureScope();
		var game = string.IsNullOrEmpty( Application.GameIdent ) ? "common" : Application.GameIdent;
		var binds = Sandbox.Engine.InputBinds.FindCollection( game );
		binds.Set( "Forward", 0, "w" );
		binds.Set( "Forward", 1, "ctrl + p" );
		binds.Set( "Jump", 1, "ctrl + p" );

		Assert.AreEqual( "Forward", Input.Keyboard.GetBoundActions( ["W"] ).Single().Name );
		Assert.AreEqual( 0, Input.Keyboard.GetBoundActions( ["h"] ).Count );
		Assert.AreEqual( 0, Input.Keyboard.GetBoundActions( ["p"] ).Count );
		CollectionAssert.AreEquivalent( new[] { "Forward", "Jump" }, Input.Keyboard.GetBoundActions( ["P", "CTRL"] ).Select( x => x.Name ).ToArray() );
	}

	/// <summary>
	/// A physical mouse tap while Ctrl is held reaches the host even when several frames pass before sending.
	/// Key edges survive multiple commands in one update and a stale command cannot restore a released key.
	/// </summary>
	[TestMethod]
	public void PhysicalChordSurvivesNetworkSend()
	{
		using var input = new ActionCaptureScope();
		var remote = new MockConnection( Guid.NewGuid() );

		Input.OnButton( ButtonCode.KEY_LCONTROL, "ctrl", true );
		remote.Input.ApplyUserCommand( RoundTrip( CaptureCommand( 1 ) ) );
		Assert.IsTrue( remote.KeyDown( "CTRL" ) );
		Assert.IsFalse( remote.KeyPressed( "ctrl", "mouse1" ) );

		Input.OnButton( ButtonCode.MouseLeft, "mouse1", true );
		Input.OnButton( ButtonCode.MouseLeft, "mouse1", false );
		input.Frame.Flip();
		Assert.IsTrue( Connection.Local.KeyPressed( "ctrl", "mouse1" ) );
		input.Frame.Flip();

		var tap = RoundTrip( CaptureCommand( 2 ) );
		remote.Input.ApplyUserCommand( tap );
		Assert.IsTrue( remote.KeyDown( "mouse1", "CTRL" ) );

		remote.Input.ApplyUserCommand( RoundTrip( CaptureCommand( 3 ) ) );
		remote.Input.ApplyUserCommand( tap );
		Assert.IsFalse( remote.KeyDown( "mouse1" ) );
		Assert.IsTrue( remote.KeyPressed( "ctrl", "mouse1" ) );
		Assert.IsTrue( remote.KeyReleased( "ctrl", "mouse1" ) );
		Assert.IsFalse( remote.KeyReleased( "ctrl" ) );
		Assert.IsFalse( remote.KeyDown() );
		Assert.IsFalse( remote.KeyDown( "unknown" ) );

		remote.Input.ClearUpdateContext();
		Assert.IsFalse( remote.KeyPressed( "mouse1" ) );
		Assert.IsFalse( remote.KeyReleased( "mouse1" ) );
		Assert.IsTrue( remote.KeyDown( "ctrl" ) );
	}

	/// <summary>
	/// A release followed by another press stays held. Suppressed and controller-only samples omit keyboard input.
	/// </summary>
	[TestMethod]
	public void PhysicalKeysRespectInputScope()
	{
		using var input = new ActionCaptureScope();
		var remote = new MockConnection( Guid.NewGuid() );
		foreach ( var down in new[] { true, false, true } )
			Input.OnButton( ButtonCode.KEY_K, "k", down );

		remote.Input.ApplyUserCommand( RoundTrip( CaptureCommand( 1 ), 9 ) );
		remote.Input.ApplyUserCommand( RoundTrip( CaptureCommand( 2 ), 9 ) );
		Assert.IsTrue( remote.KeyDown( "k" ) );

		Input.Suppressed = true;
		remote.Input.ApplyUserCommand( RoundTrip( CaptureCommand( 3 ), 6 ) );
		Assert.IsFalse( remote.KeyDown( "k" ) );
		Input.Suppressed = false;

		using var scope = Input.PlayerScope( (Input.PlayerIndex)1 );
		remote.Input.ApplyUserCommand( RoundTrip( CaptureCommand( 4 ), 6 ) );
		Assert.IsFalse( remote.KeyDown( "k" ) );
	}

	/// <summary>
	/// Keys held in separate commands do not activate a chord, even if both commands arrive during one update.
	/// </summary>
	[TestMethod]
	public void SeparateKeySamplesDoNotPressChord()
	{
		using var input = new ActionCaptureScope();
		var remote = new MockConnection( Guid.NewGuid() );

		Input.OnButton( ButtonCode.KEY_LCONTROL, "ctrl", true );
		Input.OnButton( ButtonCode.KEY_LCONTROL, "ctrl", false );
		remote.Input.ApplyUserCommand( RoundTrip( CaptureCommand( 1 ) ) );

		Input.OnButton( ButtonCode.KEY_K, "k", true );
		remote.Input.ApplyUserCommand( RoundTrip( CaptureCommand( 2 ) ) );
		Assert.IsTrue( remote.KeyPressed( "ctrl" ) );
		Assert.IsTrue( remote.KeyPressed( "k" ) );
		Assert.IsFalse( remote.KeyDown( "ctrl", "k" ) );
		Assert.IsFalse( remote.KeyPressed( "ctrl", "k" ) );
		Assert.IsFalse( remote.KeyReleased( "ctrl", "k" ) );
	}

	private static UserCommand CaptureCommand( uint number = 1 )
	{
		var command = new UserCommand( number );
		Input.CaptureNetworkInput( ref command );
		return command;
	}

	/// <summary>
	/// Isolate global input contexts and devices so action capture tests cannot affect other tests.
	/// </summary>
	private sealed class ActionCaptureScope : IDisposable
	{
		private static FieldInfo Field( string name ) => typeof( Input ).GetField( name, BindingFlags.Static | BindingFlags.NonPublic );
		private readonly object _contexts = Field( "contexts" ).GetValue( null );
		private readonly object _network = Field( "NetworkContext" ).GetValue( null );
		private readonly object _activeButtons = Field( "activeButtons" ).GetValue( null );
		private readonly BaseFileSystem _config = EngineFileSystem.Config;
		private readonly MemoryFileSystem _files = new();
		private readonly FieldInfo _bindCollectionsField = typeof( Sandbox.Engine.InputBinds ).GetField( "Collections", BindingFlags.Static | BindingFlags.NonPublic );
		private readonly object _bindCollections;
		private readonly InputSettings _settings = Input.InputSettings;
		private readonly Controller[] _controllers = Controller.All.ToArray();
		private readonly bool _suppressed = Input.Suppressed;
		private readonly bool _usingController = Input.UsingController;
		private readonly bool _headless = Application.IsHeadless;
		private readonly FieldInfo _controllerButtonsField = typeof( InputRouter ).GetField( "PressedControllerButtons", BindingFlags.Static | BindingFlags.NonPublic );
		private readonly object _controllerButtons;
		private readonly IDisposable _scope;

		internal Input.Context Network { get; }
		internal Input.Context Frame { get; }
		internal Controller Controller { get; }

		internal ActionCaptureScope()
		{
			_bindCollections = _bindCollectionsField.GetValue( null );
			_bindCollectionsField.SetValue( null, new CaseInsensitiveDictionary<Sandbox.Engine.BindCollection>() );
			typeof( EngineFileSystem ).GetProperty( nameof( EngineFileSystem.Config ) ).SetValue( null, _files );
			Field( "activeButtons" ).SetValue( null, new HashSet<string>() );
			_controllerButtons = _controllerButtonsField.GetValue( null );
			_controllerButtonsField.SetValue( null, new HashSet<GamepadCode>() );
			Field( "contexts" ).SetValue( null, new List<WeakReference<Input.Context>>() );
			Network = Input.Context.Create( "Test.Network" );
			Field( "NetworkContext" ).SetValue( null, Network );
			Frame = Input.Context.Create( "Test.Frame" );
			_scope = Frame.Push();
			Input.InputSettings = new InputSettings();
			Input.InputSettings.InitDefault();
			Input.Suppressed = false;
			typeof( Application ).GetProperty( nameof( Application.IsHeadless ) ).SetValue( null, false );
			Sandbox.Controller.All.Clear();
			Controller = new Controller( 4096 );
			Sandbox.Controller.All.Add( Controller );
		}

		/// <summary>
		/// Restore the input state that was active before the test.
		/// </summary>
		public void Dispose()
		{
			_bindCollectionsField.SetValue( null, _bindCollections );
			typeof( EngineFileSystem ).GetProperty( nameof( EngineFileSystem.Config ) ).SetValue( null, _config );
			_files.Dispose();
			Field( "activeButtons" ).SetValue( null, _activeButtons );
			_controllerButtonsField.SetValue( null, _controllerButtons );
			_scope.Dispose();
			Field( "contexts" ).SetValue( null, _contexts );
			Field( "NetworkContext" ).SetValue( null, _network );
			Input.InputSettings = _settings;
			Input.Suppressed = _suppressed;
			Input.UsingController = _usingController;
			typeof( Application ).GetProperty( nameof( Application.IsHeadless ) ).SetValue( null, _headless );
			Sandbox.Controller.All.Clear();
			Sandbox.Controller.All.UnionWith( _controllers );
		}
	}

	private static UserCommand RoundTrip( UserCommand command, int? expectedBytes = null, UserCommand result = default )
	{
		var stream = ByteStream.Create( 128 );
		command.Serialize( ref stream );
		if ( expectedBytes.HasValue )
			Assert.AreEqual( expectedBytes.Value, stream.Position );

		var reader = ByteStream.CreateReader( stream.ToSpan() );
		result.Deserialize( ref reader );
		Assert.AreEqual( 0, reader.ReadRemaining );
		reader.Dispose();
		stream.Dispose();
		return result;
	}
}
