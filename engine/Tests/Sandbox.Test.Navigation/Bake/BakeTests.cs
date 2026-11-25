namespace Nav.Lib.Tests;

/// <summary>
/// The load helper of the library's bake tests, whose tests need the scenario harness and are not here.
/// </summary>
static class BakeTests
{
	// A load with the options NavMeshOptions.Create gives for the settings in the bake.
	internal static NavMesh? Load( ReadOnlySpan<byte> data )
	{
		NavMesh.TryReadSettings( data, out var settings );
		return NavMesh.Load( data, NavMeshOptions.Create( settings ) );
	}
}
