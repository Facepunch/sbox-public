/// <summary>
/// A rope, cable or string whose constraint went away, falling onto the world and fading out like a gib. It is a
/// local copy of the line, not networked: every peer that saw the rope drops its own. A released
/// <see cref="VerletRope"/> simulates it, so it falls the way the live rope moved.
/// </summary>
public sealed class RopeGib : Component
{
	/// <summary>
	/// How long the rope lies before it starts to fade, the most extra time a gib waits at random so a bunch doesn't
	/// vanish at once, and how long the fade takes, in seconds.
	/// </summary>
	const float RestTime = 3.0f;
	const float RestJitter = 1.0f;
	const float FadeTime = 1.0f;

	/// <summary>
	/// The length of one segment, in world units, as the rope tool splits its ropes, and the most segments it uses.
	/// </summary>
	const float SegmentLength = 16.0f;
	const int MaxSegments = 48;

	/// <summary>
	/// How far each point is nudged sideways at random, in world units. A line held straight up, like a balloon's
	/// string, is balanced on its held end; the nudge lets it tip over and fall.
	/// </summary>
	const float Nudge = 0.5f;

	LineRenderer _line;
	float _width;
	float _fadeAt;
	TimeSince _age;

	/// <summary>
	/// Leave a copy of <paramref name="source"/> in <paramref name="scene"/>, in the shape it was last drawn. The line may
	/// already be off its object: it is only read. <paramref name="start"/> and <paramref name="end"/> are where its
	/// ends were last seen, for a line drawn straight between them. The rope hangs by its start, or by its end when
	/// <paramref name="holdsEnd"/>, and moves with the <paramref name="holder"/> when there is one.
	/// </summary>
	public static void Spawn( Scene scene, LineRenderer source, Vector3 start, Vector3? end, GameObject holder = null, bool holdsEnd = false )
	{
		if ( Application.IsDedicatedServer || source is null )
			return;

		if ( !scene.IsValid() || scene.IsDestroyed || scene.IsEditor || scene.IsLoading )
			return;

		var shape = CurrentShape( source, start, end );
		var length = 0.0f;
		for ( int i = 1; i < shape.Count; i++ )
			length += Vector3.DistanceBetween( shape[i - 1], shape[i] );

		if ( length < 1.0f )
			return;

		// The held end goes first: the rope hangs from its first point.
		if ( holdsEnd )
			shape.Reverse();

		for ( int i = 1; i < shape.Count; i++ )
			shape[i] += new Vector3( Random.Shared.Float( -Nudge, Nudge ), Random.Shared.Float( -Nudge, Nudge ), 0.0f );

		var go = scene.CreateObject();
		go.Name = "rope gib";
		go.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		go.NetworkMode = NetworkMode.Never;
		go.Tags.Add( "debris", "clientside" );
		if ( holder.IsValid() )
			go.SetParent( holder, false );

		go.WorldPosition = shape[0];

		var width = source.Width.Evaluate( 0.5f );
		var line = go.AddComponent<LineRenderer>();
		line.Face = source.Face;
		line.Color = source.Color;
		line.Width = width;
		line.Texturing = source.Texturing;
		line.SplineInterpolation = source.SplineInterpolation;
		line.StartCap = source.StartCap;
		line.EndCap = source.EndCap;
		line.Opaque = source.Opaque;
		line.Additive = source.Additive;
		line.CastShadows = source.CastShadows;
		line.Lighting = source.Lighting;
		line.FogStrength = source.FogStrength;
		line.CylinderSegments = source.CylinderSegments;
		line.UseVectorPoints = true;
		line.VectorPoints = shape;

		// A rope with a free end hangs from its object, along the points its line has.
		var rope = go.AddComponent<VerletRope>( false );
		rope.LinkedRenderer = line;
		rope.FreeEnd = true;
		rope.Radius = MathF.Max( width * 0.5f, 0.5f );
		rope.SegmentCount = Math.Clamp( (int)MathF.Ceiling( length / SegmentLength ), 1, MaxSegments );
		rope.Enabled = true;

		var gib = go.AddComponent<RopeGib>();
		gib._line = line;
		gib._width = width;
		gib._fadeAt = RestTime + Random.Shared.Float( 0.0f, RestJitter );
		gib._age = 0;
	}

	/// <summary>
	/// The points the line is drawn through: the rope simulation's, or its two ends.
	/// </summary>
	static List<Vector3> CurrentShape( LineRenderer source, Vector3 start, Vector3? end )
	{
		if ( source.UseVectorPoints && source.VectorPoints is { Count: >= 2 } simulated )
			return [.. simulated];

		return end is { } last ? [start, last] : [];
	}

	protected override void OnUpdate()
	{
		var fade = Math.Clamp( (_age - _fadeAt) / FadeTime, 0.0f, 1.0f );
		if ( fade >= 1.0f )
		{
			GameObject.Destroy();
			return;
		}

		_line.Width = _width * (1.0f - fade);
	}
}
