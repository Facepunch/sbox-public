namespace Sandbox;

public struct PhysicsTraceResult
{
	// Private fields first
	// Cache resolved objects to avoid multiple lookups
	private PhysicsBody _cachedBody;
	private PhysicsShape _cachedShape;
	private Surface _cachedSurface;
	private int _cachedBone = -2; // -2 = not cached
	private string[] _cachedTags;

	// store raw tag bits instead of allocating string array immediately
	internal unsafe fixed uint _rawTags[16];

	[ThreadStatic] internal static HashSet<string> tagBuilder;

	public PhysicsTraceResult()
	{
		_cachedTags = null;
	}

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

	// store raw IDs instead of resolving Objects immediately.
	internal int _bodyHandle;
	internal int _shapeHandle;
	internal int _surfaceIndex;

	public PhysicsBody Body => _cachedBody ??= HandleIndex.Get<PhysicsBody>( _bodyHandle )?.SelfOrParent;
	public PhysicsShape Shape => _cachedShape ??= HandleIndex.Get<PhysicsShape>( _shapeHandle );
	public Surface Surface => _cachedSurface ??= Surface.FindByIndex( _surfaceIndex );

	public int Bone
	{
		get
		{
			if ( _cachedBone == -2 )
			{
				var shape = Shape;
				_cachedBone = shape.IsValid() ? shape.BoneIndex : -1;
			}
			return _cachedBone;
		}
	}

	public Vector3 Direction;
	public int Triangle;

	/// <summary>
	/// The tags that the hit shape had.
	/// WARNING:  May allocate! If you need to check tags often, use HasTag instead (Most won't bother to move over to it who cares)
	/// </summary>
	public string[] Tags
	{
		get
		{
			if ( _cachedTags is not null ) return _cachedTags;
			_cachedTags = BuildTags();
			return _cachedTags;
		}
		set => _cachedTags = value;
	}

	/// <summary>
	/// Check if the hit object has a specific tag.
	/// </summary>
	public bool HasTag( string tag )
	{
		if ( string.IsNullOrEmpty( tag ) ) return false;

		// Convert string to ID
		var token = (StringToken)tag;
		if ( token.Value == 0 ) return false;

		return HasTag( token );
	}

	/// <summary>
	/// Check if the hit object has a specific tag token
	/// </summary>
	public unsafe bool HasTag( StringToken token )
	{
		var id = token.Value;

		// Fixed buffer
		for ( int i = 0; i < 16; i++ )
		{
			if ( _rawTags[i] == 0 ) break; // End of list
			if ( _rawTags[i] == id ) return true;
		}
		return false;
	}

	/// <summary>
	/// The distance between start and end positions.
	/// </summary>
	public readonly float Distance => Vector3.DistanceBetween( StartPosition, EndPosition );

	internal PhysicsTrace.Request.Shape StartShape;

	private unsafe string[] BuildTags()
	{
		tagBuilder ??= new();
		tagBuilder.Clear();

		for ( int i = 0; i < 16; i++ )
		{
			if ( _rawTags[i] == 0 ) break;

			var t = StringToken.GetValue( _rawTags[i] );
			if ( t != null )
			{
				tagBuilder.Add( t.ToLowerInvariant() );
			}
		}

		return tagBuilder.Count > 0 ? tagBuilder.ToArray() : Array.Empty<string>();
	}

	internal unsafe static PhysicsTraceResult From( in PhysicsTrace.Result result, in PhysicsTrace.Request.Shape shape )
	{
		var direction = Vector3.Direction( result.StartPos, result.EndPos );

		var r = new PhysicsTraceResult
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

			// just copy the ints and don't stick it up your ass (don't look up objects yet).
			_surfaceIndex = result.SurfaceProperty,
			_bodyHandle = result.PhysicsBodyHandle,
			_shapeHandle = result.PhysicsShapeHandle,

			StartShape = shape
		};

		for ( int i = 0; i < 16; i++ )
		{
			if ( result.Tags[i] == 0 ) break;
			r._rawTags[i] = result.Tags[i];
		}

		return r;
	}
}
