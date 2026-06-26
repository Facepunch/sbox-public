namespace Sandbox;

public partial class DebugOverlaySystem
{
	/// <summary>
	/// Loaded on first draw, so scenes that never draw a debug overlay don't load it.
	/// </summary>
	Material LineMaterial => field ??= Material.Load( "materials/gizmo/line.vmat" );

	/// <summary>
	/// Draw a line 
	/// </summary>
	public void Normal( Vector3 position, Vector3 direction, Color color = new Color(), float duration = 0, Transform transform = default, bool overlay = false )
	{
		Line( position, position + direction, color, duration, transform, overlay );
	}

	/// <summary>
	/// Draw a line 
	/// </summary>
	public void Line( Line line, Color color = new Color(), float duration = 0, Transform transform = default, bool overlay = false )
	{
		Line( line.Start, line.End, color, duration, transform, overlay );
	}

	/// <summary>
	/// Draw a line 
	/// </summary>
	public void Line( Vector3 from, Vector3 to, Color color = new Color(), float duration = 0, Transform transform = default, bool overlay = false )
	{
		if ( transform == default ) transform = Transform.Zero;
		if ( color == default ) color = Color.White;

		var so = new SceneDynamicObject( Scene.SceneWorld );
		so.Transform = transform;
		so.Material = LineMaterial;
		so.Flags.CastShadows = false;
		so.RenderLayer = overlay ? SceneRenderLayer.OverlayWithoutDepth : SceneRenderLayer.OverlayWithDepth;
		so.Init( Graphics.PrimitiveType.Lines );

		so.AddVertex( new Vertex( from, color ) );
		so.AddVertex( new Vertex( to, color ) );

		Add( duration, so );
	}

	/// <summary>
	/// Draw a line 
	/// </summary>
	public void Line( IEnumerable<Vector3> points, Color color = new Color(), float duration = 0, Transform transform = default, bool overlay = false )
	{
		if ( transform == default ) transform = Transform.Zero;
		if ( color == default ) color = Color.White;

		var so = new SceneDynamicObject( Scene.SceneWorld );
		so.Transform = Transform.Zero;
		so.Material = LineMaterial;
		so.Flags.CastShadows = false;
		so.RenderLayer = overlay ? SceneRenderLayer.OverlayWithoutDepth : SceneRenderLayer.OverlayWithDepth;
		so.Init( Graphics.PrimitiveType.LineStrip );

		foreach ( var p in points )
		{
			so.AddVertex( new Vertex( p, color ) );
		}

		Add( duration, so );
	}
}
