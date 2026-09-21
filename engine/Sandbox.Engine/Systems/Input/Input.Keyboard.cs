using NativeEngine;

namespace Sandbox;

public static partial class Input
{
	public static partial class Keyboard
	{
		/// <summary>
		/// Keyboard key is held down
		/// </summary>
		public static bool Down( string keyName )
		{
			if ( Application.IsHeadless ) return false;
			if ( Suppressed ) return false;

			var code = Sandbox.Engine.KeyTranslation.StringToButtonCode( keyName );
			if ( code == ButtonCode.BUTTON_CODE_INVALID ) return false;

			return CurrentContext.KeysCurrent.Contains( code );
		}

		/// <summary>
		/// Keyboard key wasn't pressed but now it is
		/// </summary>
		public static bool Pressed( string keyName )
		{
			if ( Application.IsHeadless ) return false;
			if ( Suppressed ) return false;

			var code = Sandbox.Engine.KeyTranslation.StringToButtonCode( keyName );
			if ( code == ButtonCode.BUTTON_CODE_INVALID ) return false;

			return !CurrentContext.KeysPrevious.Contains( code ) && Down( keyName );
		}

		/// <summary>
		/// Keyboard key was pressed but now it isn't
		/// </summary>
		public static bool Released( string keyName )
		{
			if ( Application.IsHeadless ) return false;
			if ( Suppressed ) return false;

			var code = Sandbox.Engine.KeyTranslation.StringToButtonCode( keyName );
			if ( code == ButtonCode.BUTTON_CODE_INVALID ) return false;

			return CurrentContext.KeysPrevious.Contains( code ) && !Down( keyName );
		}
	}
}
