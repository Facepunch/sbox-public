using Sandbox.ShaderCompiler;

namespace Borealis.Backend;

/// <summary>Sandbox.ShaderCompiler's compile phases as Borealis's profiler zones (<see cref="Zones.Slang"/>), set once.</summary>
internal static class CompileZones
{
	static int installed;

	public static void Install()
	{
		if ( System.Threading.Interlocked.Exchange( ref installed, 1 ) != 0 ) return;
		SlangCompiler.Profile = phase => (int)phase < Zones.Slang.Length && RenderHost.Current.ProfilesZones ? new Scope( Zones.Slang[(int)phase] ) : null;
	}

	sealed class Scope( Zone zone ) : IDisposable
	{
		readonly Zone.Scope scope = zone.Start();

		public void Dispose() => scope.Dispose();
	}
}
