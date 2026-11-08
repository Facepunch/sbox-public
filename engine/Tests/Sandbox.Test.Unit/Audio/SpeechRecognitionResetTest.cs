using System;
using System.Reflection;

namespace AudioTests;

[TestClass]
[DoNotParallelize]
public class SpeechRecognitionResetTest
{
	[TestMethod]
	public void ResetStopsListening()
	{
		var engineProperty = typeof( Sandbox.Speech.Recognition ).GetProperty( "Engine", BindingFlags.NonPublic | BindingFlags.Static );
		Assert.IsNotNull( engineProperty );

		object engine;

		try
		{
			engine = Activator.CreateInstance( engineProperty.PropertyType );
		}
		catch ( Exception e )
		{
			Assert.Inconclusive( $"No speech recognizer installed: {e.InnerException?.Message ?? e.Message}" );
			return;
		}

		try
		{
			engineProperty.SetValue( null, engine );

			Sandbox.Speech.Recognition.Reset();

			Assert.IsNull( engineProperty.GetValue( null ) );
			Assert.IsFalse( Sandbox.Speech.Recognition.IsListening );
		}
		finally
		{
			Sandbox.Speech.Recognition.Stop();
		}
	}
}
