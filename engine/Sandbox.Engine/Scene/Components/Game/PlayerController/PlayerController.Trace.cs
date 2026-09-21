namespace Sandbox;

public sealed partial class PlayerController : Component
{
	/// <summary>
	/// Return an aabb representing the body
	/// </summary>
	public BBox BodyBox( float scale = 1.0f, float heightScale = 1.0f )
	{
		var radius = BodyRadius * 0.5f * scale;
		var height = CurrentHeight * heightScale;
		var up = UpDirection;
		var center = up * height * 0.5f;
		var extents = new Vector3( radius ) + up.Abs() * (height * 0.5f - radius);

		return new BBox( center - extents, center + extents );
	}

	/// <summary>
	/// Trace the aabb body from one position to another and return the result
	/// </summary>
	public SceneTraceResult TraceBody( Vector3 from, Vector3 to, float scale = 1.0f, float heightScale = 1.0f )
	{
		return Scene.Trace.Box( BodyBox( scale, heightScale ), from, to )
								.IgnoreGameObjectHierarchy( GameObject )
								.WithCollisionRules( Tags )
								.Run();
	}
}
