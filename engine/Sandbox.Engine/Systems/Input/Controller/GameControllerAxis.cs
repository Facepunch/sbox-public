namespace Sandbox
{
	internal enum GameControllerAxis : int
	{
		INVALID = -1,

		LeftX = 0,
		LeftY = 1,
		RightX = 2,
		RightY = 3,
		TriggerLeft = 4,
		TriggerRight = 5,

		MAX = 6,
	};

	internal static partial class GameControllerExtensions
	{
		internal static InputAnalog ToInputAnalog( this GameControllerAxis x )
		{
			return x switch
			{
				GameControllerAxis.LeftX => InputAnalog.LeftStickX,
				GameControllerAxis.LeftY => InputAnalog.LeftStickY,
				GameControllerAxis.RightX => InputAnalog.RightStickX,
				GameControllerAxis.RightY => InputAnalog.RightStickY,
				GameControllerAxis.TriggerLeft => InputAnalog.LeftTrigger,
				GameControllerAxis.TriggerRight => InputAnalog.RightTrigger,
				_ => default
			};
		}
	}
}
