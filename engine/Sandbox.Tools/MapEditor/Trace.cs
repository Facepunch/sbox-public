using Editor.MapDoc;
using System;
using System.Runtime.InteropServices;

namespace Editor
{
	public struct TraceResult
	{
		/// <summary>
		/// Whether the trace hit something or not
		/// </summary>
		public bool Hit;

		/// <summary>
		/// The hit position of the trace
		/// </summary>
		public Vector3 HitPosition;

		/// <summary>
		/// The hit surface normal (direction vector)
		/// </summary>
		public Vector3 Normal;

		/// <summary>
		/// The map node that was hit, if any
		/// </summary>
		public MapNode MapNode;

		internal static TraceResult From( in NativeHammer.TraceResult result )
		{
			// Native leaves everything but the hit flag uninitialized on a miss
			if ( !result.Hit )
				return default;

			return new TraceResult
			{
				Hit = true,
				HitPosition = result.HitPos,
				Normal = result.HitNormal,
				MapNode = HandleIndex.Get<MapNode>( result.HitMapNodeHandle ),
			};
		}
	}

	/// <summary>
	/// Trace for tools, not to be confused with <see cref="Sandbox.SceneTrace"/>
	/// </summary>
	public struct Trace
	{
		NativeHammer.TraceRequest request;

		/// <summary>
		/// Create a trace ray.
		/// </summary>
		/// <param name="from">Start position in world space.</param>
		/// <param name="to">End position in world space.</param>
		public static Trace Ray( in Vector3 from, in Vector3 to )
		{
			return new Trace
			{
				request = new NativeHammer.TraceRequest
				{
					StartPos = from,
					EndPos = to
				}
			};
		}

		/// <summary>
		/// Only trace against hammer mesh geometry ( CMapMesh nodes )
		/// </summary>
		public readonly Trace MeshesOnly()
		{
			var t = this;
			t.request.TraceFlags = t.request.TraceFlags | NativeHammer.TraceFlags.TRACE_FLAG_ONLY_MESHES;
			return t;
		}

		/// <summary>
		/// Don't hit tools materials (materials with the <c>tools.toolsmaterial</c> attribute)
		/// </summary>
		public readonly Trace SkipToolsMaterials()
		{
			var t = this;
			t.request.TraceFlags = t.request.TraceFlags | NativeHammer.TraceFlags.TRACE_FLAG_SKIP_TOOLS_MATERIALS;
			return t;
		}

		/// <summary>
		/// Hit back facing geometry too - by default only faces pointing towards the ray are hit.
		/// A back face hit (normal pointing along the ray) means the ray started inside something.
		/// </summary>
		public readonly Trace HitBackfaces()
		{
			var t = this;
			t.request.TraceFlags = t.request.TraceFlags | NativeHammer.TraceFlags.TRACE_FLAG_CULL_NONE;
			return t;
		}

		/// <summary>
		/// Runs a trace against given world.
		/// </summary>
		public TraceResult Run( MapWorld world )
		{
			Assert.IsValid( world );
			return TraceResult.From( world.worldNative.Trace( request ) );
		}
	}

}

namespace NativeHammer
{
	/// <summary>
	/// Matches TraceFlags_t
	/// </summary>
	[Flags]
	internal enum TraceFlags
	{
		TRACE_FLAG_NONE = 0,
		TRACE_FLAG_CULL_NONE = 1 << 0,    // Trace against both front and back facing geometry (if not set only front facing is used)
		TRACE_FLAG_CULL_FRONT = 1 << 1,   // Trace against only back facing geometry (Ignored if CULL_NONE is specified )
		TRACE_FLAG_DONT_PRIORITIZE_ACTIVE_TOOL = 1 << 3,  // Don't do the special single trace against the active tool with an early out
		TRACE_FLAG_HIT_INSTANCE_MEMBERS = 1 << 4, // If the trace hits an object in an instance, return the object rather than the instance
		TRACE_FLAG_ONLY_FIRST_RAY_HIT = 1 << 5,   // If multiple rays are specified only return the result for the first one which has a hit
		TRACE_FLAG_ONLY_MESHES = 1 << 6,  // Only trace against hammer mesh geometry ( CMapMesh nodes )
		TRACE_FLAG_ALLOW_DISABLED = 1 << 7,   // Allow the trace to hit nodes which are not editable
		TRACE_FLAG_ALLOW_HIDDEN_INSTANCES = 1 << 8,   // Allow tracing against hidden instances
		TRACE_FLAG_SKIP_TOOLS_MATERIALS = 1 << 9, // Don't hit tools materials (materials with the the tools.toolsmaterial attribute)
		TRACE_FLAG_SKIP_REFERENCE_NO_TRACE = 1 << 10, // Don't hit materials marked with tools.reference_notrace
		TRACE_FLAG_SKIP_SELECTED_OBJECTS = 1 << 11,   // Don't hit any objects
		TRACE_FLAG_GROUP_DROP_TARGET = 1 << 12,   // The trace is trying to hit a group drop target, this allows nodes to provide a different shape for group drop target hit testing
		TRACE_FLAG_ALLOW_HIDDEN_MATERIALS = 1 << 13,  // Allow hitting materials which are currently hidden. By default faces with materials that are currently hidden will not be hit.
	}

	internal struct TraceRequest
	{
		public Vector3 StartPos;
		public Vector3 EndPos;
		public TraceFlags TraceFlags;
	}

	internal struct TraceResult
	{
		public bool Hit;
		public Vector3 HitPos;
		public Vector3 HitNormal;
		public int HitMapNodeHandle;
	}

	[StructLayout( LayoutKind.Sequential )]
	internal unsafe struct HitInfo_t
	{
		public IntPtr pObject;
		public IntPtr uData;
		public int nDepth;
		public float m_flTraceT;
		public Vector3 m_vPos;
		public Vector3 m_vNormal;
		public IntPtr m_pSubObjectInfo;
		public IntPtr m_pSubObjectInfo2;
		public int m_nInstancePathCount;
		public IntPtr m_InstancePath1;
		public IntPtr m_InstancePath2;
		public IntPtr m_InstancePath3;
		public IntPtr m_InstancePath4;
		public IntPtr m_InstancePath5;
		public IntPtr m_InstancePath6;
		public IntPtr m_InstancePath7;
		public IntPtr m_InstancePath8;
		public IntPtr m_InstancePath9;
		public IntPtr m_InstancePath10;
		public IntPtr m_InstancePath11;
		public IntPtr m_InstancePath12;
		public IntPtr m_InstancePath13;
		public IntPtr m_InstancePath14;
		public IntPtr m_InstancePath15;
		public IntPtr m_InstancePath16;
	}
}
