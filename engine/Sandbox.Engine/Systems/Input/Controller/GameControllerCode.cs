namespace Sandbox
{
	internal enum GameControllerCode : int
	{
		INVALID = -1,

		A = 0,
		B = 1,
		X = 2,
		Y = 3,
		Back = 4,
		Guide = 5,
		Start = 6,
		LeftAnalogStick = 7,
		RightAnalogStick = 8,
		LeftShoulder = 9,
		RightShoulder = 10,
		DPadUp = 11,
		DPadDown = 12,
		DPadLeft = 13,
		DPadRight = 14,
		Misc1 = 15,
		Paddle1 = 16,
		Paddle2 = 17,
		Paddle3 = 18,
		Paddle4 = 19,
		Touchpad = 20,

		MAX = 21,
	};

	internal static partial class GameControllerExtensions
	{
		internal static GamepadCode ToGamepadCode( this GameControllerCode x )
		{
			return x switch
			{
				> GameControllerCode.MAX => GamepadCode.None,
				_ => (GamepadCode)x,
			};
		}
	}
}
