using static Facepunch.Constants;

namespace Facepunch.Steps;

internal static class CheckNativeTouched
{
	internal static ExitCode Run()
	{
		bool touched = Utility.PrTouchesNativeCode();
		string value = touched ? "true" : "false";

		Log.Info( $"native_touched={value}" );

		var githubOutput = Environment.GetEnvironmentVariable( "GITHUB_OUTPUT" );
		if ( !string.IsNullOrEmpty( githubOutput ) )
		{
			File.AppendAllText( githubOutput, $"native_touched={value}{Environment.NewLine}" );
		}

		return ExitCode.Success;
	}
}
