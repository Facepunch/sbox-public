using System.Reflection;

namespace SystemTests;

[TestClass]
public class PreferencesPrivacyTests
{
	[TestMethod]
	[DataRow( "VoiceEnabled", "VoiceEnabledSetting" )]
	[DataRow( "StreamerMode", "StreamerModeSetting" )]
	[DataRow( "ChatEnabled", "ChatEnabledSetting" )]
	public void PrivacyPreferencesAreNotGameSettable( string name, string setting )
	{
		var property = typeof( Preferences ).GetProperty( name, BindingFlags.Public | BindingFlags.Static );
		var convar = typeof( Preferences ).GetProperty( setting, BindingFlags.NonPublic | BindingFlags.Static );
		var before = (bool)convar.GetValue( null );

		// The public setter stays so existing games still compile, but it does nothing
		property.SetValue( null, !before );
		Assert.AreEqual( before, property.GetValue( null ) );

		Assert.IsTrue( convar.GetCustomAttribute<ConVarAttribute>().Flags.HasFlag( ConVarFlags.Protected ) );
	}
}
