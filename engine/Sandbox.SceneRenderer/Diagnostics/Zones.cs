using Sandbox.Diagnostics;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Profile scopes for renderer phases and parallel recording segments.
/// </summary>
internal static class Zones
{
	public static readonly ProfileScope Collect = new( "SceneRenderer.Collect" );
	public static readonly ProfileScope Prepare = new( "SceneRenderer.Prepare" );
	public static readonly ProfileScope MeshPrepare = new( "SceneRenderer.MeshPrepare" );
	public static readonly ProfileScope ShadowCull = new( "SceneRenderer.ShadowCull" );
	public static readonly ProfileScope Setup = new( "SceneRenderer.Setup" );
	public static readonly ProfileScope Record = new( "SceneRenderer.Record" );
	public static readonly ProfileScope Segment = new( "SceneRenderer.Segment" );
	public static readonly ProfileScope RecordWait = new( "SceneRenderer.RecordWait" );
	public static readonly ProfileScope Submit = new( "SceneRenderer.Submit" );
}
