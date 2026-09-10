using Sandbox.Utility;
using System.ComponentModel;

namespace Sandbox;

public static partial class Input
{
	/// <summary>
	/// What's our current player index (for input scoping)?
	/// -1 is the default behavior, where it'll accept keyboard AND the first controller.
	/// 0 = keyboard/mouse only, 1+ = specific controller by index.
	/// Additional controllers beyond the first require an explicit PlayerScope(N) to read.
	/// </summary>
	internal static int CurrentPlayerScope { get; private set; } = -1;

	/// <summary>
	/// Push a specific player scope to be active.
	/// </summary>
	public static IDisposable PlayerScope( PlayerIndex player )
	{
		var index = (int)player;

		var oldScope = CurrentPlayerScope;
		var oldAnalogLook = AnalogLook;
		var oldAnalogMove = AnalogMove;
		var oldUsingController = UsingController;
		var oldMotionData = MotionData;

		CurrentPlayerScope = index;
		ApplyPlayerScope( index );

		return DisposeAction.Create( () =>
		{
			if ( CurrentPlayerScope != index ) return;

			CurrentPlayerScope = oldScope;
			AnalogLook = oldAnalogLook;
			AnalogMove = oldAnalogMove;
			UsingController = oldUsingController;
			MotionData = oldMotionData;
		} );
	}

	/// <inheritdoc cref="PlayerScope(PlayerIndex)" />
	[Obsolete( "Use PlayerScope( PlayerIndex ) instead" )]
	[EditorBrowsable( EditorBrowsableState.Never )]
	public static IDisposable PlayerScope( int index ) => PlayerScope( (PlayerIndex)index );

	/// <summary>
	/// Computes and applies the analog input state for a given player scope.
	/// </summary>
	private static void ApplyPlayerScope( int index )
	{
		if ( index == 0 )
		{
			UsingController = false;
			ComputeAnalogLook();
			ComputeAnalogMove();
			return;
		}

		// Controller scope
		UsingController = true;
		AnalogLook = default;
		AnalogMove = default;
		ProcessControllerInput();
	}
}
