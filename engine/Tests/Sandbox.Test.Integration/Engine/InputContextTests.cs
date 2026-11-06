using NativeEngine;
using System;
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
			PressA( controller, false );
			Controller.All.Clear();
			foreach ( var c in saved )
				Controller.All.Add( c );
		}
	} );

	static void PressA( Controller controller, bool down ) => Sandbox.Engine.InputRouter.OnGameControllerButton( controller.DeviceId, GameControllerCode.A, down );

	static bool IsSet( ulong actions, string name ) => (actions & (1UL << Input.GetActionIndex( name ))) != 0;

	static void WithInput( Action<Input.Context> test )
	{
		var settings = Input.InputSettings;
		var ident = Application.GameIdent;
		var usingController = Input.UsingController;

		try
		{
			Application.GameIdent = "test.inputcontext";
			Input.ReadConfig( new InputSettings() );

			test( Input.Context.Create( "Test" ) );
		}
		finally
		{
			Input.OnButton( ButtonCode.KEY_W, "w", false );
			Application.GameIdent = ident;
			Input.InputSettings = settings;
			Input.UsingController = usingController;
		}
	}
}
