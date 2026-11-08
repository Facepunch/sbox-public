using System.Threading;

namespace Sandbox.Rendering;

/// <summary>
/// Shadow caster culling, shared by every light type.
///
/// A shadow map only matters where it's sampled: a caster is worth rendering only when it sits between the light
/// and something the camera can see. Two tests come out of that, both run on the CPU while shadows are worked out:
///
/// <list type="bullet">
/// <item>A whole shadow view is skipped when its frustum misses the camera's (<see cref="ShadowViewReachesCamera"/>).
/// A point light's six cube faces are each their own cone out of the light, so a light off to the side usually only
/// needs one or two of them - the rest can't contain a receiver the camera sees, so nothing samples them.</item>
/// <item>What survives is culled against the caster volume (<see cref="BuildCasterVolume"/>): the camera's visible
/// range swept toward the light. Clutter's GPU cull reads it as planes.</item>
/// </list>
/// </summary>
internal partial class ShadowMapper
{
	[ConVar( "r.shadows.castercull", Help = "Skip shadow views that can't cast onto anything the camera sees." )]
	public static bool CasterCull { get; set; } = true;

	internal static int ShadowViewsCulled { get; set; }
	internal static int ShadowViewsCulledLastFrame { get; set; }

	/// <summary>
	/// Stands in for a far plane at infinity - a projection built from FOV tangents has no finite corner to unproject.
	/// Bigger than any map, so the hull it caps still contains everything the camera could see.
	/// </summary>
	const float InfiniteFarFallback = 262144f;

	// Faces of the corner hexahedron (indices into Corners order): near, far, then the four sides.
	private static readonly int[][] HullFaces =
	[
		[0, 1, 2, 3],
		[4, 5, 6, 7],
		[0, 1, 5, 4],
		[1, 2, 6, 5],
		[2, 3, 7, 6],
		[3, 0, 4, 7],
	];

	// Edges of the corner hexahedron: two corners and the two faces sharing them.
	private static readonly (int A, int B, int FaceA, int FaceB)[] HullEdges =
	[
		(0, 1, 0, 2), (1, 2, 0, 3), (2, 3, 0, 4), (3, 0, 0, 5),
		(4, 5, 1, 2), (5, 6, 1, 3), (6, 7, 1, 4), (7, 4, 1, 5),
		(0, 4, 2, 5), (1, 5, 2, 3), (2, 6, 3, 4), (3, 7, 4, 5),
	];

	// The near and far rings run in the same cycle, so HullFaces reads either hexahedron the same way.
	private static readonly (float U, float V)[] RingOffsets = [(-1, -1), (-1, 1), (1, 1), (1, -1)];

	/// <summary>
	/// Planes of the shadow caster volume: the convex hull of <paramref name="corners"/> extended toward the light,
	/// which for a directional light is a point at infinity, so the volume runs off along -<paramref name="lightDirection"/>.
	/// Only casters inside it can shadow anything inside the hull. Corners are the 8 frustum corners in
	/// <see cref="Corners"/> order (near ring 0-3, far ring 4-7). Output planes point inward: xyz = unit normal,
	/// w = -distance, so dot(xyz, p) + w is the signed distance. Returns the plane count (≤ 12).
	/// </summary>
	internal static int BuildCasterVolume( ReadOnlySpan<Vector3> corners, Vector3 lightDirection, Span<Vector4> planes )
		=> BuildVolumePlanes( corners, lightDirection, Vector3.Zero, true, planes );

	/// <summary>
	/// The same volume for a local light: the hull of <paramref name="corners"/> closed up at
	/// <paramref name="lightPosition"/>, which bounds it instead of infinity.
	/// </summary>
	internal static int BuildCasterVolumeFrom( ReadOnlySpan<Vector3> corners, Vector3 lightPosition, Span<Vector4> planes )
		=> BuildVolumePlanes( corners, Vector3.Zero, lightPosition, false, planes );

	static int BuildVolumePlanes( ReadOnlySpan<Vector3> corners, Vector3 lightDirection, Vector3 lightPosition, bool directional, Span<Vector4> planes )
	{
		Vector3 centroid = Vector3.Zero;
		for ( int i = 0; i < 8; i++ )
			centroid += corners[i];
		centroid /= 8;

		int count = 0;
		Span<bool> kept = stackalloc bool[HullFaces.Length];
		for ( int f = 0; f < HullFaces.Length; f++ )
		{
			var face = HullFaces[f];
			var plane = Inward( new Plane( corners[face[0]], corners[face[1]], corners[face[2]] ), centroid );

			// A face still bounds the volume when the light is on its inside: everything the light reaches through
			// the hull stops there. The faces the light is outside of are swallowed by the sweep.
			kept[f] = directional
				? Vector3.Dot( plane.Normal, lightDirection ) <= 0
				: plane.GetDistance( lightPosition ) >= 0;

			if ( kept[f] && count < planes.Length )
				planes[count++] = new Vector4( plane.Normal, -plane.Distance );
		}

		// Silhouette edges, seen from the light, close the sides of the sweep
		foreach ( var edge in HullEdges )
		{
			if ( kept[edge.FaceA] == kept[edge.FaceB] )
				continue;

			var apex = directional ? corners[edge.A] - lightDirection : lightPosition;
			var plane = new Plane( corners[edge.A], corners[edge.B], apex );
			if ( plane.Normal.LengthSquared < 0.5f ) // Edge in line with the light, its faces already bound it
				continue;

			plane = Inward( plane, centroid );
			if ( count < planes.Length )
				planes[count++] = new Vector4( plane.Normal, -plane.Distance );
		}

		return count;

		static Plane Inward( Plane plane, Vector3 inside )
		{
			if ( plane.GetDistance( inside ) < 0 )
			{
				plane.Normal = -plane.Normal;
				plane.Distance = -plane.Distance;
			}
			return plane;
		}
	}

	/// <summary>
	/// A caster volume (<see cref="BuildCasterVolume"/>) and the shadow views it belongs to, named by where their
	/// camera sits: a local light's views all start at the light, a sun cascade's at its own ortho origin. A shadow
	/// view renders long after it's worked out, so it finds its volume by that instead of carrying it along.
	/// </summary>
	internal sealed class CasterVolume
	{
		public readonly Vector4[] Planes = new Vector4[12];
		public int PlaneCount;
		// A local light's views all start at it; a sun's cascades each have their own ortho origin, four at most
		public readonly Vector3[] Origins = new Vector3[4];
		public int OriginCount;

		/// <summary>True when <paramref name="origin"/> is one of the views this volume was built for.</summary>
		public bool Covers( Vector3 origin )
		{
			for ( int i = 0; i < OriginCount; i++ )
				if ( Origins[i].DistanceSquared( origin ) < 1f ) return true;
			return false;
		}
	}

	/// <summary>
	/// The volumes published for a frame. Two sets alternate, so a render thread still reading last frame's never
	/// sees this frame's half-built one. A view that doesn't find its volume just skips the extra cull.
	/// </summary>
	sealed class CasterVolumeSet
	{
		public const int Capacity = 64;
		public readonly CasterVolume[] Volumes = new CasterVolume[Capacity];
		public int Count;

		public CasterVolumeSet()
		{
			for ( int i = 0; i < Capacity; i++ )
				Volumes[i] = new CasterVolume();
		}
	}

	static readonly CasterVolumeSet[] VolumeSets = [new(), new()];
	static CasterVolumeSet PublishedVolumes = VolumeSets[0];
	static int VolumeSetIndex;

	/// <summary>
	/// Start a frame's volumes on the set the last frame didn't use.
	/// </summary>
	static void BeginCasterVolumes()
	{
		VolumeSetIndex ^= 1;
		var set = VolumeSets[VolumeSetIndex];
		set.Count = 0;
		Volatile.Write( ref PublishedVolumes, set );
	}

	/// <summary>
	/// Take the next free volume to fill, or null when the frame has published as many as it can hold.
	/// Publish it with <see cref="PublishCasterVolume"/> once it's filled in.
	/// </summary>
	static CasterVolume NextCasterVolume()
	{
		var set = PublishedVolumes;
		return set.Count < CasterVolumeSet.Capacity ? set.Volumes[set.Count] : null;
	}

	/// <summary>
	/// Make a filled volume visible to the views that render against it.
	/// </summary>
	static void PublishCasterVolume( CasterVolume volume )
	{
		var set = PublishedVolumes;
		if ( set.Count >= CasterVolumeSet.Capacity || set.Volumes[set.Count] != volume )
			return;

		Volatile.Write( ref set.Count, set.Count + 1 );
	}

	/// <summary>
	/// The caster volume for the shadow view rendering from <paramref name="origin"/>, or null when it has none.
	/// Called while the view renders, so it reads the published set once.
	/// </summary>
	internal static CasterVolume FindCasterVolume( Vector3 origin )
	{
		if ( !CasterCull ) return null;

		var set = Volatile.Read( ref PublishedVolumes );
		int count = Volatile.Read( ref set.Count );
		for ( int i = 0; i < count; i++ )
		{
			if ( set.Volumes[i].Covers( origin ) ) return set.Volumes[i];
		}

		return null;
	}

	/// <summary>
	/// The camera's world-space frustum corners in <see cref="Corners"/> order, and its inward planes - what a
	/// shadow view has to reach to be worth rendering. Built once per view, on first use.
	/// </summary>
	readonly Vector3[] viewHullCorners = new Vector3[8];
	readonly Vector4[] viewHullPlanes = new Vector4[6];
	bool viewHullValid;

	ReadOnlySpan<Vector3> ViewHullCorners
	{
		get
		{
			BuildViewHull();
			return viewHullCorners;
		}
	}

	void BuildViewHull()
	{
		if ( viewHullValid ) return;
		viewHullValid = true;

		BuildCameraHull( View, viewHullCorners );
		BuildHullPlanes( viewHullCorners, viewHullPlanes );
	}

	/// <summary>
	/// The camera's world-space frustum corners in <see cref="Corners"/> order.
	///
	/// Not every projection has a far plane to unproject: a VR eye's is built from FOV tangents, so its corners at
	/// clip depth 0 come back with w = 0 and land at infinity. Only a perspective projection can do that, and its
	/// corners run on lines out of the camera, so the near corners say which way out - capped past anything a map
	/// could hold. Capping long is the safe way round: the hull is what a shadow view has to reach to be worth
	/// rendering, so too long only costs a view that didn't need it, while too short would cull one that did.
	/// </summary>
	internal static void BuildCameraHull( in ShadowViewInfo view, Span<Vector3> corners )
	{
		Span<Vector3> viewSpace = stackalloc Vector3[8];
		for ( int i = 0; i < 8; i++ )
		{
			var corner = System.Numerics.Vector4.Transform( Corners[i], view.ProjectionToView );
			viewSpace[i] = new Vector3( corner.X, corner.Y, corner.Z ) / corner.W;
		}

		if ( !float.IsFinite( viewSpace[4].LengthSquared ) )
		{
			Vector3 nearCenter = (viewSpace[0] + viewSpace[1] + viewSpace[2] + viewSpace[3]) * 0.25f;
			Vector3 forward = Vector3.Cross( viewSpace[3] - viewSpace[0], viewSpace[1] - viewSpace[0] ).Normal;
			float nearDepth = MathF.Abs( Vector3.Dot( forward, nearCenter ) );
			float scale = InfiniteFarFallback / MathF.Max( nearDepth, 1e-3f );

			for ( int i = 0; i < 4; i++ )
				viewSpace[i + 4] = viewSpace[i] * scale;
		}

		for ( int i = 0; i < 8; i++ )
			corners[i] = ViewToWorld( viewSpace[i], view );
	}

	/// <summary>
	/// The six inward planes of a corner hexahedron: xyz = unit normal, w = -distance.
	/// </summary>
	static void BuildHullPlanes( ReadOnlySpan<Vector3> corners, Span<Vector4> planes )
	{
		Vector3 centroid = Vector3.Zero;
		for ( int i = 0; i < 8; i++ )
			centroid += corners[i];
		centroid /= 8;

		for ( int f = 0; f < HullFaces.Length; f++ )
		{
			var face = HullFaces[f];
			var plane = new Plane( corners[face[0]], corners[face[1]], corners[face[2]] );
			if ( plane.GetDistance( centroid ) < 0 )
			{
				plane.Normal = -plane.Normal;
				plane.Distance = -plane.Distance;
			}

			planes[f] = new Vector4( plane.Normal, -plane.Distance );
		}
	}

	/// <summary>
	/// A shadow view's world-space corners, in <see cref="Corners"/> order.
	/// </summary>
	static void ShadowViewCorners( in ShadowViewDesc view, Span<Vector3> corners )
	{
		Vector3 forward = view.Rotation.Forward, right = view.Rotation.Right, up = view.Rotation.Up;
		float tangent = view.Orthographic ? 0 : MathF.Tan( view.FieldOfView * 0.5f * MathF.PI / 180f );

		for ( int ring = 0; ring < 2; ring++ )
		{
			float depth = ring == 0 ? view.ZNear : view.ZFar;
			float halfWidth = view.Orthographic ? view.Width * 0.5f : tangent * depth;
			float halfHeight = view.Orthographic ? view.Height * 0.5f : tangent * depth;
			Vector3 center = view.Position + forward * depth;

			for ( int i = 0; i < 4; i++ )
			{
				var (u, v) = RingOffsets[i];
				corners[ring * 4 + i] = center + right * (u * halfWidth) + up * (v * halfHeight);
			}
		}
	}

	/// <summary>
	/// True when no plane of <paramref name="planes"/> has every corner outside it - a cheap conservative
	/// separating-plane test, so a false "they meet" only costs a shadow view that didn't need rendering.
	/// </summary>
	static bool NotSeparated( ReadOnlySpan<Vector4> planes, ReadOnlySpan<Vector3> corners )
	{
		for ( int p = 0; p < planes.Length; p++ )
		{
			var plane = planes[p];
			var normal = new Vector3( plane.x, plane.y, plane.z );

			bool allOutside = true;
			for ( int c = 0; c < corners.Length; c++ )
			{
				if ( Vector3.Dot( normal, corners[c] ) + plane.w < 0 ) continue;
				allOutside = false;
				break;
			}

			if ( allOutside ) return false;
		}

		return true;
	}

	/// <summary>
	/// Whether anything rendered into <paramref name="view"/> could shadow something the camera sees.
	///
	/// A local light's shadow view is a cone out of the light, and so is the direction the lighting shader looks its
	/// shadow map up in: a receiver only samples this view when it's inside the view's frustum. So if the frustum
	/// never meets the camera's, no visible pixel reads it and the whole view is wasted work.
	/// </summary>
	bool ShadowViewReachesCamera( in ShadowViewDesc view )
	{
		if ( !CasterCull ) return true;

		BuildViewHull();

		Span<Vector3> corners = stackalloc Vector3[8];
		Span<Vector4> planes = stackalloc Vector4[6];
		ShadowViewCorners( view, corners );
		BuildHullPlanes( corners, planes );

		return NotSeparated( planes, viewHullCorners ) && NotSeparated( viewHullPlanes, corners );
	}

	/// <summary>
	/// Publish the caster volume for a local light's shadow views: the camera's visible range closed up at the light.
	/// </summary>
	void PublishLocalCasterVolume( Vector3 lightPosition )
	{
		if ( !CasterCull ) return;
		if ( NextCasterVolume() is not { } volume ) return;

		BuildViewHull();
		volume.PlaneCount = BuildCasterVolumeFrom( viewHullCorners, lightPosition, volume.Planes );
		volume.Origins[0] = lightPosition;
		volume.OriginCount = 1;
		PublishCasterVolume( volume );
	}
}
