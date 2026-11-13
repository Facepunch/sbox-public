using NativeEngine;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace EngineTests;

[TestClass, DoNotParallelize]
public class InputContextTests
{
	[TestMethod]
	public void KeyReleasedAndPressedAgainBeforeFlipStaysDown() => WithInput( context =>
	{
		Input.OnButton( ButtonCode.KEY_W, "w", true );
		context.Flip();

		Input.OnButton( ButtonCode.KEY_W, "w", false );
		Input.OnButton( ButtonCode.KEY_W, "w", true );
		context.Flip();
		context.Flip();

		Assert.IsTrue( context.KeysCurrent.Contains( ButtonCode.KEY_W ) );
		Assert.IsTrue( IsSet( context.ActionsCurrent, "Forward" ) );
	} );

	[TestMethod]
	public void KeyPressedAndReleasedBeforeFlipIsDownForOneFlip() => WithInput( context =>
	{
		Input.OnButton( ButtonCode.KEY_W, "w", true );
		Input.OnButton( ButtonCode.KEY_W, "w", false );
		context.Flip();

		Assert.IsTrue( context.KeysCurrent.Contains( ButtonCode.KEY_W ) );
		Assert.IsTrue( IsSet( context.ActionsCurrent, "Forward" ) );

		context.Flip();

		Assert.IsFalse( context.KeysCurrent.Contains( ButtonCode.KEY_W ) );
		Assert.IsFalse( IsSet( context.ActionsCurrent, "Forward" ) );
	} );

	[TestMethod]
	public void GamepadButtonReleasedAndPressedAgainBeforeFlipStaysDown() => WithInput( _ =>
	{
		var saved = Controller.All.ToArray();
		var controller = (Controller)RuntimeHelpers.GetUninitializedObject( typeof( Controller ) );
		controller.InputContext = Input.Context.Create( "Test Controller" );

		try
		{
			Controller.All.Clear();
			Controller.All.Add( controller );

			PressA( controller, true );
			controller.InputContext.Flip();

			PressA( controller, false );
			PressA( controller, true );
			controller.InputContext.Flip();
			controller.InputContext.Flip();

			Assert.IsTrue( IsSet( controller.InputContext.ActionsCurrent, "Jump" ) );
		}
		finally
		{
			Controller.All.Clear();
			foreach ( var c in saved )
				Controller.All.Add( c );
		}
	} );

	static void PressA( Controller controller, bool down ) => Sandbox.Engine.InputRouter.OnGameControllerButton( controller.DeviceId, GameControllerCode.A, down );

	static bool IsSet( ulong actions, string name ) => (actions & (1UL << Input.GetActionIndex( name ))) != 0;

	/// <summary>
	/// Input state the test swaps for empty collections, so its presses can't reach other contexts and nothing held or bound
	/// outside the test can change the result.
	/// </summary>
	static readonly FieldInfo[] SharedInputState =
	[
		typeof( Input ).GetField( "contexts", BindingFlags.NonPublic | BindingFlags.Static ),
		typeof( Input ).GetField( "activeButtons", BindingFlags.NonPublic | BindingFlags.Static ),
		typeof( Sandbox.Engine.InputBinds ).GetField( "Collections", BindingFlags.NonPublic | BindingFlags.Static ),
		typeof( Sandbox.Engine.InputRouter ).GetField( "PressedControllerButtons", BindingFlags.NonPublic | BindingFlags.Static ),
	];

	static void WithInput( Action<Input.Context> test )
	{
		var settings = Input.InputSettings;
		var ident = Application.GameIdent;
		var usingController = Input.UsingController;
		var config = EngineFileSystem.Config;
		var state = SharedInputState.Select( x => x.GetValue( null ) ).ToArray();

		try
		{
			foreach ( var field in SharedInputState )
				field.SetValue( null, Activator.CreateInstance( field.FieldType ) );

			// no saved binds, so the default binds are the ones under test
			SetConfig( new MemoryFileSystem() );

			Application.GameIdent = "test.inputcontext";
			Input.ReadConfig( new InputSettings() );

			test( Input.Context.Create( "Test" ) );
		}
		finally
		{
			for ( int i = 0; i < SharedInputState.Length; i++ )
				SharedInputState[i].SetValue( null, state[i] );

			SetConfig( config );
			Application.GameIdent = ident;
			Input.InputSettings = settings;
			Input.UsingController = usingController;
		}
	}

	static void SetConfig( BaseFileSystem fs ) => typeof( EngineFileSystem ).GetProperty( nameof( EngineFileSystem.Config ) ).SetValue( null, fs );
}
