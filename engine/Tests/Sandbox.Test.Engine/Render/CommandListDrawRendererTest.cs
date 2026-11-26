using Sandbox.Rendering;
using System.Collections.Generic;
using System.Reflection;

namespace RenderTests;

[TestClass]
[DoNotParallelize]
public class CommandListDrawRendererTest : SceneTests.SceneTest
{
	sealed class RecordingRenderer : Renderer
	{
		public readonly List<RendererSetup> Setups = new();
		internal override void RenderSceneObject( RendererSetup rendererSetup = default ) => Setups.Add( rendererSetup );
	}

	static void Execute( CommandList list ) => typeof( CommandList ).GetMethod( "ExecuteEntries", BindingFlags.Instance | BindingFlags.NonPublic ).Invoke( list, null );

	[TestMethod]
	public void SetupOverridesReachTheRenderer()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var renderer = new GameObject().AddComponent<RecordingRenderer>();

		var transform = new Transform( new Vector3( 1, 2, 3 ), Rotation.From( 10, 20, 30 ), new Vector3( 4, 5, 6 ) );
		var list = new CommandList( "test" );
		list.DrawRenderer( renderer );
		list.DrawRenderer( renderer, new RendererSetup { Transform = transform } );
		list.DrawRenderer( renderer, new RendererSetup { Color = new Color( 0.1f, 0.2f, 0.3f, 0.4f ) } );
		Execute( list );

		Assert.AreEqual( 3, renderer.Setups.Count );
		Assert.IsNull( renderer.Setups[0].Transform );
		Assert.IsNull( renderer.Setups[0].Color );
		Assert.IsNull( renderer.Setups[0].Material );

		var tx = renderer.Setups[1].Transform.Value;
		Assert.AreEqual( transform.Position, tx.Position );
		Assert.AreEqual( transform.Rotation, tx.Rotation );
		Assert.AreEqual( transform.Scale, tx.Scale );
		Assert.IsNull( renderer.Setups[1].Color );

		Assert.AreEqual( new Color( 0.1f, 0.2f, 0.3f, 0.4f ), renderer.Setups[2].Color );
		Assert.IsNull( renderer.Setups[2].Transform );
	}
}
