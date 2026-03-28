
using System.Runtime.InteropServices;

namespace Sandbox.Engine.Utility.RayTrace;

public partial struct MeshTraceRequest
{
	private MeshTraceInput request;

	internal SceneWorld targetWorld;
	internal Model targetModel;

	internal Func<SceneObject, bool> filterCallback;

	[ThreadStatic]
	static Func<SceneObject, bool> _currentfilterCallback;

	[UnmanagedCallersOnly]
	static byte FilterFunctionInternal( int value )
	{
		try
		{
			Assert.NotNull( _currentfilterCallback );

			var shape = HandleIndex.Get<SceneObject>( value );
			if ( shape is null ) return 1; // should never happen, just use default behaviour

			if ( _currentfilterCallback( shape ) ) return 1;
			return 0;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error in trace filter: {e.Message}" );
			return 1;
		}
	}

	/// <summary>
	/// Run the trace and return the result. The result will return the first hit.
	/// </summary>
	public readonly unsafe Result Run()
	{
		MeshTraceOutput result = default;
		result.distance = float.MaxValue;

		var r = request;

		if ( targetWorld != null && targetWorld.IsValid() )
		{
			if ( filterCallback is not null )
			{
				r.filterDelegate = (IntPtr)((delegate* unmanaged< int, byte >)&FilterFunctionInternal);
				_currentfilterCallback = filterCallback;
			}

			try
			{
				if ( targetWorld.native.MeshTrace( r, ref result ) )
				{
					return Result.From( this, result );
				}
			}
			finally
			{
				_currentfilterCallback = default;
			}
		}

		if ( targetModel != null )
		{
			if ( !targetModel.native.MeshTrace( r, ref result ) )
				return new Result { Hit = false };

			return Result.From( this, result );
		}

		return new Result
		{
			Hit = false,
			EndPosition = r.end,
			Distance = Vector3.DistanceBetween( r.start, r.end )
		};
	}

	public readonly unsafe Result[] RunAll()
	{
		var r = request;

		if ( targetWorld != null && targetWorld.IsValid() )
		{
			if ( filterCallback is not null )
			{
				r.filterDelegate = (IntPtr)((delegate* unmanaged< int, byte >)&FilterFunctionInternal);
				_currentfilterCallback = filterCallback;
			}

			var results = NativeEngine.CUtlVectorMeshTraceOutput.Create( 32, 32 );

			try
			{

				if ( targetWorld.native.MeshTraceAll( r, results ) )
				{
					var hitCount = results.Count();
					var output = new Result[hitCount];

					for ( int i = 0; i < hitCount; i++ )
					{
						output[i] = Result.From( this, results.Element( i ) );
					}

					return output;
				}
			}
			finally
			{
				_currentfilterCallback = default;

				results.DeleteThis();
			}
		}

		if ( targetModel is not null )
		{
			MeshTraceOutput result = default;
			if ( targetModel.native.MeshTrace( r, ref result ) )
				return [Result.From( this, result )];
		}

		return [];
	}

	/// <summary>
	/// Casts a ray from point A to point B.
	/// </summary>
	public MeshTraceRequest Ray( in Vector3 from, in Vector3 to )
	{
		request.start = from;
		request.end = to;

		return this;
	}

	/// <summary>
	/// Casts a ray from a given position and direction, up to a given distance.
	/// </summary>
	public MeshTraceRequest Ray( in Ray ray, in float distance )
	{
		request.start = ray.Position;
		request.end = ray.ProjectSafe( distance );

		return this;
	}

	internal unsafe MeshTraceRequest WithOptionalTag( string tag )
	{
		var ident = StringToken.FindOrCreate( tag );

		for ( int i = 0; i < 8; i++ )
		{
			if ( request.tagAny[i] == ident ) return this;

			if ( request.tagAny[i] == 0 )
			{
				request.tagAny[i] = ident;
				return this;
			}
		}

		return this;
	}

	/// <summary>
	/// Only return scene objects with this tag. Subsequent calls to this will add multiple requirements
	/// and they'll all have to be met (ie, the scene object will need all tags).
	/// </summary>
	public unsafe MeshTraceRequest WithTag( string tag )
	{
		var ident = StringToken.FindOrCreate( tag );

		for ( int i = 0; i < 8; i++ )
		{
			if ( request.tagRequire[i] == ident ) return this;

			if ( request.tagRequire[i] == 0 )
			{
				request.tagRequire[i] = ident;
				return this;
			}
		}

		return this;
	}

	/// <summary>
	/// Only return scene objects with all of these tags
	/// </summary>
	public unsafe MeshTraceRequest WithAllTags( params string[] tags )
	{
		var t = this;

		foreach ( var tag in tags )
		{
			t = t.WithTag( tag );
		}

		return t;
	}

	/// <summary>
	/// Only return scene objects with any of these tags
	/// </summary>
	public unsafe MeshTraceRequest WithAnyTags( params string[] tags )
	{
		var t = this;

		foreach ( var tag in tags )
		{
			t = t.WithOptionalTag( tag );
		}

		return t;
	}

	internal unsafe MeshTraceRequest WithoutTag( string tag )
	{
		var ident = StringToken.FindOrCreate( tag );

		for ( int i = 0; i < 8; i++ )
		{
			if ( request.tagExclude[i] == ident ) return this;

			if ( request.tagExclude[i] == 0 )
			{
				request.tagExclude[i] = ident;
				return this;
			}
		}

		return this;
	}

	/// <summary>
	/// Only return scene objects without any of these tags
	/// </summary>
	public unsafe MeshTraceRequest WithoutTags( params string[] tags )
	{
		var t = this;

		foreach ( var tag in tags )
		{
			t = t.WithoutTag( tag );
		}

		return t;
	}

	internal static unsafe MeshTraceRequest From( PhysicsTrace.Request request, SceneWorld targetWorld, int cullMode )
	{
		MeshTraceInput meshTraceRequest = new();
		meshTraceRequest.start = request.StartPos;
		meshTraceRequest.end = request.EndPos;
		meshTraceRequest.cullMode = cullMode;

		for ( int i = 0; i < 8; i++ )
		{
			meshTraceRequest.tagRequire[i] = request.TagRequire[i];
			meshTraceRequest.tagAny[i] = request.TagAny[i];
			meshTraceRequest.tagExclude[i] = request.TagExclude[i];
		}

		return new MeshTraceRequest { request = meshTraceRequest, targetWorld = targetWorld };
	}

	public struct Result
	{
		public bool Hit { get; set; }

		/// <summary>
		/// The distance between start and end positions.
		/// </summary>
		public float Distance { get; set; }

		/// <summary>
		/// The start position of the trace
		/// </summary>
		public Vector3 StartPosition { get; set; }

		/// <summary>
		/// The end or hit position of the trace
		/// </summary>
		public Vector3 EndPosition { get; set; }

		/// <summary>
		/// The hit position of the trace
		/// </summary>
		public Vector3 HitPosition { get; set; }

		/// <summary>
		/// A fraction [0..1] of where the trace hit between the start and the original end positions
		/// </summary>
		public float Fraction { get; set; }

		/// <summary>
		/// The hit surface normal (direction vector)
		/// </summary>
		public Vector3 Normal { get; set; }
		public int HitTriangle { get; set; } // todo this makes no sense if there are multiple rendermeshes
		public Material Material { get; set; }

		/// <summary>
		/// The transform of the hit object (if it has one)
		/// </summary>
		public Transform Transform { get; set; }

		/// <summary>
		/// If we hit something associated with a sceneobject, this will be that object.
		/// </summary>
		public SceneObject SceneObject { get; set; }

		/// <summary>
		/// This is the Uv coordinate on the triangle hit. 'x' represents the distance between Vertex 0-1, 'y' represents the distance between Vertex 0-2.
		/// </summary>
		public Vector2 HitTriangleUv { get; set; }

		/// <summary>
		/// Given the position on the triangle hit, this vector gives the influence of each vertex on that position.
		/// So for example, if the Vector is [1,0,0] that means that the hit point is right on vertex 0. If it's [0.33, 0.33, 0.33] then it's 
		/// right in the middle of each vertex.
		/// </summary>
		public Vector3 VertexInfluence { get; set; }

		public struct VertexDetail
		{
			public Vector3 Position { get; set; }
			public Vector3 Normal { get; set; }
			public Vector4 Color { get; set; }

			public Vector2 Uv0 { get; set; }
			public Vector2 Uv1 { get; set; }

			public Vector4 Paint1 { get; set; }
			public Vector4 Paint0 { get; set; }
		}

		public VertexDetail Vertex0;
		public VertexDetail Vertex1;
		public VertexDetail Vertex2;

		internal unsafe static Result From( in MeshTraceRequest input, in MeshTraceOutput result )
		{
			Result tr = default;

			var distance = input.request.start.Distance( input.request.end );

			tr.Hit = true;
			tr.Distance = result.distance;
			tr.Fraction = distance > 0.0f ? result.distance / distance : 0.0f;
			tr.StartPosition = input.request.start;
			tr.EndPosition = result.position;
			tr.HitPosition = result.position;
			tr.Normal = result.normal;
			tr.Material = Material.FromNative( result.material );
			tr.Transform = result.transform;
			tr.HitTriangle = result.triangleIndex;
			tr.SceneObject = HandleIndex.Get<SceneObject>( result.sceneobjectHandle );

			tr.HitTriangleUv = result.uv;
			tr.VertexInfluence = new Vector3( result.uv.x, result.uv.y, 1 - result.uv.x - result.uv.y );

			tr.Vertex0 = GetTraceVertex( (TraceVertex_t*)result.v0 );
			tr.Vertex1 = GetTraceVertex( (TraceVertex_t*)result.v1 );
			tr.Vertex2 = GetTraceVertex( (TraceVertex_t*)result.v2 );

			return tr;
		}

		static unsafe VertexDetail GetTraceVertex( TraceVertex_t* data )
		{
			if ( data == null )
				return default;

			VertexDetail d = default;
			d.Position = data->m_vPosition;
			d.Normal = data->m_vNormal;
			d.Uv0 = data->m_vTexCoord;
			d.Uv1 = data->m_vTexCoord2;

			return d;
		}
	}
}
