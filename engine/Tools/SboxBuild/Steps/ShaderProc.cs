using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class ShaderProc
{
	internal ExitCode Run()
	{
		return BuildDisplay.Run( "Pack shader sources", () =>
		{
			try
			{
				BuildDisplay.Status( "Packing shader sources" );
				Facepunch.ShaderProc.Program.Process( "engine", Log.Info );
				return ExitCode.Success;
			}
			catch ( Exception ex )
			{
				Log.Error( $"Shader source packing failed: {ex}" );
				return ExitCode.Failure;
			}
		} );
	}
}
