using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class InteropGen( bool skipNative = false )
{
	internal ExitCode Run()
	{
		return BuildDisplay.Run( "Generate interop bindings", () =>
		{
			var previousSink = Facepunch.InteropGen.Log.Sink;
			try
			{
				Facepunch.InteropGen.Log.Sink = Log.Info;
				BuildDisplay.Status( "Generating interop bindings" );
				return Facepunch.InteropGen.Program.ProcessManifest( "engine", skipNative,
					( completed, total ) => BuildDisplay.Progress( completed, total, $"{completed}/{total} definitions processed" ) )
					? ExitCode.Success : ExitCode.Failure;
			}
			catch ( Exception ex )
			{
				Log.Error( $"Interop generation failed: {ex}" );
				return ExitCode.Failure;
			}
			finally
			{
				Facepunch.InteropGen.Log.Sink = previousSink;
			}
		} );
	}
}
