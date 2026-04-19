namespace Sandbox;

public partial class DebugOverlaySystem
{
	/// <summary>
	/// Draw a sphere
	/// </summary>
	public void Sphere( Sphere sphere, Color color = new Color(), float duration = 0, Transform transform = default, bool overlay = false )
	{
		if ( transform == default ) transform = Transform.Zero;
		if ( color == default ) color = Color.White;

		var so = new DebugSphereSceneObject( Scene.SceneWorld );
		so.Transform = transform;
		so.Material = LineMaterial;
		so.sphere = sphere;
		so.ColorTint = color;
		so.Flags.CastShadows = false;
		so.RenderLayer = overlay ? SceneRenderLayer.OverlayWithoutDepth : SceneRenderLayer.OverlayWithDepth;

		Add( duration, so );
	}
}

file class DebugSphereSceneObject : SceneCustomObject
{
	public Sphere sphere;
	public Material Material;

	List<Vertex> Vertices = new();

	public DebugSphereSceneObject( SceneWorld sceneWorld ) : base( sceneWorld )
	{
		managedNative.ExecuteOnMainThread = false;
	}

	public override void RenderSceneObject()
	{
		Vertices.Clear();

		var dir = sphere.Center - Graphics.CameraPosition;
		var rings = 16;
		var rot = Rotation.LookAt( dir );

		AddCircle( new Transform( sphere.Center, rot, sphere.Radius ), rings, new Vertex { Color = ColorTint } );
		AddCircle( new Transform( sphere.Center, Transform.Rotation, sphere.Radius ), rings, new Vertex { Color = ColorTint } );
		AddCircle( new Transform( sphere.Center, Transform.Rotation * new Angles( 0, 90, 0 ), sphere.Radius ), rings, new Vertex { Color = ColorTint } );
		AddCircle( new Transform( sphere.Center, Transform.Rotation * new Angles( 90, 0, 0 ), sphere.Radius ), rings, new Vertex { Color = ColorTint } );

		Graphics.Draw( Vertices.ToArray().AsSpan(), Vertices.Count, default, default, Material, Attributes, Graphics.PrimitiveType.Lines );
	}
	void AddCircle( Transform transform, int segments, Vertex vertex )
	{
		Vector3 lastPos = 0;
		for ( int s = -1; s < segments; s++ )
		{
			float f = s / (float)segments;
			var t = f * MathF.PI * 2;

			Vector3 pos = 0;

			pos += Vector3.Right * MathF.Sin( t );
			pos += Vector3.Up * MathF.Cos( t );

			pos = transform.PointToWorld( pos );

			if ( s >= 0 )
			{
				Vertices.Add( vertex with { Position = lastPos } );
				Vertices.Add( vertex with { Position = pos } );
			}

			lastPos = pos;
		}
	}
}
