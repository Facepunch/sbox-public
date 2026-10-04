using System.Buffers;
using Sandbox.Rendering;

namespace Sandbox.Clutter;

/// <summary>
/// One model's instances in a clutter layer. The scene's <see cref="ClutterRenderer"/> owns its GPU data and draws it.
/// </summary>
internal sealed class ClutterBatch
{
	public const int MaxLods = 4; // dont think we need more than that

	public Model Model { get; }

	/// <summary>
	/// Whether the clutter entry wants shadows. <see cref="ClutterRenderer.ShadowsEnabled"/> can still turn them off.
	/// </summary>
	public bool WantsShadows { get; }

	public int LodCount { get; }
	public float ModelRadius { get; }
	public float[] LodSwitchDistances { get; }
	public int[] DrawCallCounts { get; }

	/// <summary>
	/// Slot in the renderer's batch table, or -1 once removed.
	/// </summary>
	internal int Id { get; set; } = -1;

	/// <summary>
	/// First indirect draw entry per LOD in the renderer's argument buffer.
	/// </summary>
	internal int[] ArgsBase { get; } = new int[MaxLods];

	/// <summary>
	/// This batch's tiles and where they sit in the renderer's instance buffers.
	/// </summary>
	internal Dictionary<PreparedInstances, ClutterRenderer.Slot> Tiles { get; } = [];

	internal int InstanceCount { get; set; }
	internal BBox Bounds { get; set; }

	public ClutterBatch( Model model, bool wantsShadows )
	{
		Model = model;
		WantsShadows = wantsShadows;
		ModelRadius = model.Bounds.Size.Length * 0.5f;

		var switches = model.GetLodSwitchDistances() ?? [];
		LodCount = Math.Clamp( switches.Length, 1, MaxLods );

		LodSwitchDistances = new float[LodCount];
		DrawCallCounts = new int[LodCount];
		for ( int i = 0; i < LodCount; i++ )
		{
			LodSwitchDistances[i] = i < switches.Length ? switches[i] : 0f;
			DrawCallCounts[i] = Math.Max( 1, model.GetLodDrawCallCount( i ) );
		}
	}

	/// <summary>
	/// Prepared tile data owned by the layer. Buffers return to the pool after the tile leaves its batch.
	/// </summary>
	internal sealed class PreparedInstances : IDisposable
	{
		public GpuInstanceTransform[] Transforms { get; }
		public Vector4[] Spheres { get; }
		public BBox Bounds { get; private set; }
		public int Count { get; }

		private int _next;
		private readonly BBox _modelBounds;
		private readonly float _modelRadius;

		public PreparedInstances( Model model, int count )
		{
			Count = count;
			Transforms = ArrayPool<GpuInstanceTransform>.Shared.Rent( count );
			Spheres = ArrayPool<Vector4>.Shared.Rent( count );
			_modelBounds = model.Bounds;
			_modelRadius = _modelBounds.Size.Length * 0.5f;
		}

		public void Add( Transform transform )
		{
			var center = transform.PointToWorld( _modelBounds.Center );
			var scale = transform.Scale;
			var radius = _modelRadius * MathF.Max( scale.x, MathF.Max( scale.y, scale.z ) );
			Transforms[_next] = GpuInstanceTransform.From( transform );
			Spheres[_next] = new Vector4( center.x, center.y, center.z, radius );
			var bounds = _modelBounds.Transform( transform );
			Bounds = _next == 0 ? bounds : Bounds.AddBBox( bounds );
			_next++;
		}

		public void Dispose()
		{
			ArrayPool<GpuInstanceTransform>.Shared.Return( Transforms );
			ArrayPool<Vector4>.Shared.Return( Spheres );
		}
	}
}
