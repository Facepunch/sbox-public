namespace Sandbox;

public struct PhysicsTraceResult
{
	/// <summary>
	/// Whether the trace hit something or not
	/// </summary>
	public bool Hit;

	/// <summary>
	/// Whether the trace started in a solid
	/// </summary>
	public bool StartedSolid;

	/// <summary>
	/// The start position of the trace
	/// </summary>
	public Vector3 StartPosition;

	/// <summary>
	/// The end or hit position of the trace
	/// </summary>
	public Vector3 EndPosition;

	/// <summary>
	/// The hit position of the trace
	/// </summary>
	public Vector3 HitPosition;

	/// <summary>
	/// The hit surface normal (direction vector)
	/// </summary>
	public Vector3 Normal;

	/// <summary>
	/// A fraction [0..1] of where the trace hit between the start and the original end positions
	/// </summary>
	public float Fraction;

	/// <summary>
	/// The physics object that was hit, if any
	/// </summary>
	public PhysicsBody Body;

	/// <summary>
	/// The physics shape that was hit, if any
	/// </summary>
	public PhysicsShape Shape;

	/// <summary>
	/// The physical properties of the hit surface
	/// </summary>
	public Surface Surface;

	/// <summary>
	/// The id of the hit bone (either from hitbox or physics shape)
	/// </summary>
	public readonly int Bone => Shape.IsValid() ? Shape.BoneIndex : -1;

	/// <summary>
	/// The direction of the trace ray
	/// </summary>
	public Vector3 Direction;

	/// <summary>
	/// The triangle index hit, if we hit a mesh <see cref="PhysicsShape">physics shape</see>
	/// </summary>
	public int Triangle;

	/// <summary>
	/// Returns true if the hit shape has this tag.
	/// </summary>
	public readonly bool HasTag( StringToken tag ) => _rawTags.Contains( tag.Value );

	/// <summary>
	/// The tags that the hit shape had.
	/// </summary>
	[Obsolete( "Use HasTag instead." )]
	public readonly string[] Tags => _rawTags.Count == 0 ? Array.Empty<string>() : _rawTags.ToStringArray();

	// Raw token IDs stored inline, allocation free
	internal TagBuffer16 _rawTags;

	/// <summary>
	/// The distance between start and end positions.
	/// </summary>
	public readonly float Distance => Vector3.DistanceBetween( StartPosition, EndPosition );

	internal PhysicsTrace.Request.Shape StartShape;

	internal unsafe static PhysicsTraceResult From( in PhysicsTrace.Result result, in PhysicsTrace.Request.Shape shape )
	{
		var rawTags = new TagBuffer16();

		for ( var i = 0; i < 16; i++ )
		{
			if ( result.Tags[i] == 0 ) break;
			rawTags.AddUnique( result.Tags[i] );
		}

		var direction = Vector3.Direction( result.StartPos, result.EndPos );

		return new PhysicsTraceResult
		{
			Hit = result.Fraction < 1,
			StartedSolid = result.StartedInSolid != 0,
			StartPosition = result.StartPos,
			EndPosition = result.EndPos,
			HitPosition = result.HitPos,
			Normal = result.Normal,
			Fraction = result.Fraction,
			Direction = direction,
			Triangle = result.TriangleIndex,
			_rawTags = rawTags,

			// TODO - maybe we populate these on access?
			Surface = Surface.FindByIndex( result.SurfaceProperty ),
			Body = HandleIndex.Get<PhysicsBody>( result.PhysicsBodyHandle )?.SelfOrParent,
			Shape = HandleIndex.Get<PhysicsShape>( result.PhysicsShapeHandle ),

			StartShape = shape
		};
	}
}

internal unsafe struct TagBuffer16
{
	public int Count;
	private fixed uint _tokens[16];

	public unsafe void AddUnique( uint token )
	{
		for ( var i = 0; i < Count; i++ ) if ( _tokens[i] == token ) return;
		if ( Count < 16 ) _tokens[Count++] = token;
	}

	public readonly unsafe bool Contains( uint token )
	{
		for ( var i = 0; i < Count; i++ ) if ( _tokens[i] == token ) return true;
		return false;
	}

	public readonly unsafe string[] ToStringArray()
	{
		var arr = new string[Count];
		for ( var i = 0; i < Count; i++ ) arr[i] = StringToken.GetValue( _tokens[i] ) ?? string.Empty;
		return arr;
	}
}

