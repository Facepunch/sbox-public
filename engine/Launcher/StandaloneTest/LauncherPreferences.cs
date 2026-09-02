using System.IO;

namespace Sandbox;

internal class LauncherPreferences
{
	private static CookieContainer _cookie;
	public static CookieContainer Cookie => _cookie;

	public static bool CloseOnLaunch
	{
		get => _cookie.Get( "CloseOnLaunch", false );
		set => _cookie.Set( "CloseOnLaunch", value );
	}

	public static string DefaultProjectLocation
	{
		get => _cookie.GetString( "DefaultProjectLocation", Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ), "S&box Projects" ) );
		set => _cookie.SetString( "DefaultProjectLocation", value );
	}

	public static bool LightTheme
	{
		get => _cookie.Get( "LightTheme", false );
		set => _cookie.Set( "LightTheme", value );
	}

	public static void Load()
	{
		_cookie = new CookieContainer( "launcher" );
	}

	public static void Save()
	{
		_cookie.Save();
	}
}
