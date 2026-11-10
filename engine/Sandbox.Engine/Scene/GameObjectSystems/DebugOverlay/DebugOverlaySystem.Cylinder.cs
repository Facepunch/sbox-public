namespace Sandbox;

public partial class DebugOverlaySystem
{
	/// <summary>
	/// Draw a wireframe cylinder, like a capsule without the hemispheres, showing all sides.
	/// </summary>
	public void Cylinder( Capsule capsule, Color color = default, float duration = 0, Transform transform = default, bool overlay = false, int segments = 12 )
	{
		TaperedCylinder( capsule.CenterA, capsule.CenterB, capsule.Radius, capsule.Radius, color, duration, transform, overlay, segments );
	}

	/// <summary>
	/// Draw a wireframe tapered cylinder, like a capsule without the hemispheres with start and end radius, showing all sides.
	/// </summary>
	public void TaperedCylinder( Vector3 startCenter, Vector3 endCenter, float startRadius, float endRadius, Color color = default, float duration = 0, Transform transform = default, bool overlay = false, int segments = 12 )
	{
		if ( transform == default ) transform = Transform.Zero;
		if ( color == default ) color = Color.White;

		var sceneObject = new SceneDynamicObject( Scene.SceneWorld )
		{
			Transform = transform,
			Material = LineMaterial,
			RenderLayer = overlay ? SceneRenderLayer.OverlayWithoutDepth : SceneRenderLayer.OverlayWithDepth
		};

		sceneObject.Flags.CastShadows = false;
		sceneObject.Init( Graphics.PrimitiveType.Lines );

		var axis = endCenter - startCenter;
		var direction = axis.Length > 0 ? axis.Normal : Vector3.Up;

		var tangent = Vector3.Cross( direction, Vector3.Right );
		if ( tangent.IsNearZeroLength ) tangent = Vector3.Cross( direction, Vector3.Up );
		tangent = tangent.Normal;

		var bitangent = Vector3.Cross( direction, tangent ).Normal;
		var angleStep = MathF.Tau / segments;

		bool startIsPoint = startRadius == 0.0f;
		bool endIsPoint = endRadius == 0.0f;

		for ( int i = 0; i < segments; i++ )
		{
			var a0 = i * angleStep;
			var a1 = (i + 1) * angleStep;

			Vector3 dir0 = tangent * MathF.Cos( a0 ) + bitangent * MathF.Sin( a0 );
			Vector3 dir1 = tangent * MathF.Cos( a1 ) + bitangent * MathF.Sin( a1 );

			var b0 = startIsPoint ? startCenter : startCenter + dir0 * startRadius;
			var b1 = startIsPoint ? startCenter : startCenter + dir1 * startRadius;

			var t0 = endIsPoint ? endCenter : endCenter + dir0 * endRadius;
			var t1 = endIsPoint ? endCenter : endCenter + dir1 * endRadius;

			if ( !startIsPoint )
			{
				sceneObject.AddVertex( new Vertex( b0, color ) );
				sceneObject.AddVertex( new Vertex( b1, color ) );
			}

			if ( !endIsPoint )
			{
				sceneObject.AddVertex( new Vertex( t0, color ) );
				sceneObject.AddVertex( new Vertex( t1, color ) );
			}

			sceneObject.AddVertex( new Vertex( b0, color ) );
			sceneObject.AddVertex( new Vertex( t0, color ) );
		}

		Add( duration, sceneObject );
	}
}
