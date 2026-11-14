using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Borealis.Features;

/// <summary>
/// Per-frame bones, skinned instance IDs and compute vertex-cache updates.
/// </summary>
internal sealed partial class MeshRenderFeature
{
	readonly List<MeshObject> skinMembers = new();

	// A tinted run's objects' first entries, while it's built (Emit)
	readonly List<int> tintedSlots = new();

	/// <summary>
	/// Write instance IDs in per-mesh blocks and return the first index.
	/// Each draw starts at first + <see cref="RenderMesh.BlockForLod"/> * member count.
	/// </summary>
	int WriteSkinnedInstances( RenderMesh mesh, int lod, TransformBuffer transforms )
	{
		var first = transforms.InstanceCount;
		foreach ( var m in mesh.MeshesForLod( lod ) )
		{
			foreach ( var member in CollectionsMarshal.AsSpan( skinMembers ) )
				transforms.AddInstance( member.SkinSlots[m] );
		}

		return first;
	}

	/// <summary>
	/// Per-mesh compute job with bone slot and vertex-cache destination.
	/// </summary>
	record struct SkinJob( MeshObject Object, int ModelMesh, int Slot, int VertexCacheOffset )
	{
		/// <summary>
		/// Where its deformation volumes start in the frame's volume buffer, for a deformed object.
		/// </summary>
		public int VolumeOffset;

		/// <summary>
		/// Where its anchors, per mesh bone, start in the frame's anchor buffer, for a rigidly deformed object; 0 for none.
		/// </summary>
		public int AnchorOffset;

		/// <summary>
		/// Its morphs' handle this frame (<see cref="IMorphSource.Queue"/>), or -1 when it doesn't morph.
		/// </summary>
		public int Morph;

		public readonly bool Morphed => Morph >= 0;
	}

	readonly List<SkinJob> skinJobs = new();
	int skinJobsGeneration = -1;
	Matrix3x4[] skinScratch = new Matrix3x4[64];
	TransformBuffer frameTransforms;
	GpuMaterial skinningShader;

	/// <summary>
	/// The compute shader native skins with (<c>CSceneSystem</c>'s <c>m_hCsSkinningMat</c>).
	/// </summary>
	public const string SkinningShader = "materials/dev/skinning_cs.vmat";

	/// <summary>
	/// Write bones once per mesh per frame: world pose after inverse bind pose (<c>SetupBones</c>).
	/// Skeleton-free meshes stay rigid. CPU-only; cache allocation waits until <see cref="BeforeUpload"/>.
	/// </summary>
	void WriteBones( MeshObject obj, int lod, in Matrix objectToWorld, Color tint, TransformBuffer transforms )
	{
		if ( skinJobsGeneration != transforms.Generation )
		{
			skinJobs.Clear();
			skinJobsGeneration = transforms.Generation;
		}

		var mesh = obj.Mesh;
		if ( obj.SkinGeneration != transforms.Generation )
		{
			obj.SkinGeneration = transforms.Generation;
			if ( obj.SkinSlots.Length != mesh.ModelMeshCount ) obj.SkinSlots = new int[mesh.ModelMeshCount];
			obj.SkinSlots.AsSpan().Fill( -1 );
		}

		var worldBones = obj.BoneMatrices;
		var bindPose = mesh.BindPose;
		var objectMatrix = Matrix3x4.From( objectToWorld );

		// Each mesh this LOD draws that no view has written yet this frame
		foreach ( var m in mesh.MeshesForLod( lod ) )
		{
			if ( obj.SkinSlots[m] >= 0 ) continue;

			var skin = mesh.SkinFor( m );
			if ( skin is null )
			{
				obj.SkinSlots[m] = transforms.Add( objectToWorld, tint );
				continue;
			}

			if ( skinScratch.Length < skin.BoneCount ) skinScratch = new Matrix3x4[Math.Max( skin.BoneCount, skinScratch.Length * 2 )];
			for ( int b = 0; b < skin.BoneCount; b++ )
			{
				// Posed bones, or the bind pose at the object's transform
				var master = skin.MasterBones[b];
				var world = (uint)master < (uint)worldBones.Length ? worldBones[master]
					: (uint)master < (uint)bindPose.Length ? Matrix3x4.Concat( objectMatrix, bindPose[master] ) : objectMatrix;
				skinScratch[b] = Matrix3x4.Concat( world, skin.InverseBindPoses[b] );
			}

			var slot = transforms.AddSkinned( tint, skin.BlendWeightCount, skinScratch.AsSpan( 0, skin.BoneCount ) );
			obj.SkinSlots[m] = slot;

			// Deformation and morphs require compute skinning even with one weight.
			if ( skin.UsesVertexCache || obj.IsDeformed || (skin.Morphs && obj.MorphSource is not null) ) skinJobs.Add( new SkinJob( obj, m, slot, -1 ) { Morph = -1 } );
		}
	}

	/// <summary>
	/// Allocate vertex-cache blocks and finalize morph entries before upload.
	/// </summary>
	internal override void BeforeUpload( GpuContext context, TransformBuffer transforms )
	{
		frameTransforms = transforms;
		if ( skinJobsGeneration != transforms.Generation ) skinJobs.Clear();

		var jobs = CollectionsMarshal.AsSpan( skinJobs );
		var morphs = false;
		for ( int i = 0; i < jobs.Length; i++ )
		{
			ref var job = ref jobs[i];
			var skin = job.Object.Mesh.SkinFor( job.ModelMesh );
			job.VertexCacheOffset = GpuContext.AllocateVertexCache( skin.VertexCount );
			transforms.SetVertexCacheOffset( job.Slot, job.VertexCacheOffset );

			// Evaluate and queue morphs (FindOrAllocateTransforms).
			job.Morph = skin.Morphs && job.Object.MorphSource is { } source ? source.Queue( job.Object.Mesh.Geometry, job.ModelMesh ) : -1;
			morphs |= job.Morphed;
		}

		if ( !morphs ) return;

		// Composite before submission, then store atlas subrects.
		context.GenerateMorphs();
		for ( int i = 0; i < jobs.Length; i++ )
		{
			if ( jobs[i].Morphed ) transforms.SetMorphSubrect( jobs[i].Slot, GpuContext.MorphSubrect( jobs[i].Morph ) );
		}
	}

	/// <summary>
	/// The fewest skinning jobs worth recording on a worker (<see cref="BeginSetupWork"/>).
	/// </summary>
	internal static int MinWorkerSkinningJobs { get; set; } = 16;

	/// <summary>
	/// Records the frame's skinning when a worker does, submitted right after the frame's setup context.
	/// </summary>
	GpuContext skinningContext;

	/// <summary>
	/// Whether skinning may be recorded on a worker this frame: only when workers record its segments, each on a context of its
	/// own. A frame that records serially draws on its setup context, which skinning must come before.
	/// </summary>
	internal bool SkinOnWorker { get; set; }
	GpuContext skinningFrame;
	JobHandle skinning;
	bool skinningStarted;

	/// <summary>
	/// Compute-skin uploaded transforms before any render pass: on the frame's setup context, or, for many dispatches while
	/// recording in parallel, on a context of its own that a worker records once setup is done (<see cref="BeginSetupWork"/>)
	/// and that's submitted right after the setup context (<see cref="EndSetupWork"/>). Each dispatch is a material bind and
	/// a constant buffer of native work, about 5 us, so a scene of dressed citizens spends a third of a millisecond on them.
	/// </summary>
	void DispatchSkinning( GpuContext context )
	{
		if ( !PrepareSkinning( context ) ) return;

		if ( SkinOnWorker && skinJobs.Count >= MinWorkerSkinningJobs && JobDispatcher.MaxDegreeOfParallelism > 1 )
		{
			skinningFrame = context;
			return;
		}

		RecordSkinning( context );
	}

	/// <summary>
	/// Start recording the skinning <see cref="DispatchSkinning"/> left for a worker, now that setup has finished writing the
	/// frame's attributes, which its context inherits.
	/// </summary>
	internal override void BeginSetupWork()
	{
		if ( skinningFrame is null ) return;

		skinningContext ??= RenderHost.Current.Device.CreateContext( skinningFrame );
		skinningStarted = true;
		skinning = JobDispatcher.Begin( 1, this, static ( feature, _ ) => feature.RecordSkinningOnWorker() );
	}

	void RecordSkinningOnWorker()
	{
		var recording = GpuContext.Recording;
		try
		{
			skinningContext.BeginSegment( skinningFrame, null );
			GpuContext.Recording = skinningContext;
			RecordSkinning( skinningContext );
		}
		finally
		{
			GpuContext.Recording = recording;
		}
	}

	/// <summary>
	/// Join the worker's skinning, and submit it after the frame's setup context, or discard it with a frame that isn't
	/// submitted.
	/// </summary>
	internal override void EndSetupWork( bool submit )
	{
		if ( !skinningStarted )
		{
			skinningFrame = null;
			return;
		}

		// Joining records it here if no worker took it yet, so the frame it begins on stays set until then
		skinningStarted = false;
		try
		{
			skinning.Join();
		}
		catch
		{
			skinningContext.End();
			throw;
		}
		finally
		{
			skinningFrame = null;
		}

		if ( submit ) skinningContext.SubmitNext();
		else skinningContext.End();
	}

	/// <summary>
	/// Sort the jobs into batches and upload their deformation data on the setup context. False when there's nothing to skin.
	/// </summary>
	bool PrepareSkinning( GpuContext context )
	{
		if ( skinJobs.Count == 0 || frameTransforms is null ) return false;

		skinningShader ??= RenderHost.Current.Device.LoadMaterial( SkinningShader );

		// Batch matching meshes; deformation/morph combos must match (CSceneSystem::CanBatchVertexCacheRequests).
		var jobs = CollectionsMarshal.AsSpan( skinJobs );
		// Deformed, then morphed, then by geometry and model mesh: a key each, rather than comparing the objects' state per pair
		if ( skinSortKeys.Length < jobs.Length ) skinSortKeys = new ulong[Math.Max( jobs.Length, skinSortKeys.Length * 2 )];
		var keys = skinSortKeys.AsSpan( 0, jobs.Length );
		for ( int i = 0; i < jobs.Length; i++ )
		{
			ref readonly var job = ref jobs[i];
			keys[i] = (job.Object.IsDeformed ? 1UL << 63 : 0) | (job.Morphed ? 1UL << 62 : 0)
				| (ulong)(uint)RuntimeHelpers.GetHashCode( SkinningKey( job.Object.Mesh ) ) << 16 | (ushort)job.ModelMesh;
		}

		keys.Sort( jobs );

		if ( batchSlots.Length < jobs.Length )
		{
			batchSlots = new int[jobs.Length];
			batchOffsets = new int[jobs.Length];
			batchVolumeOffsets = new int[jobs.Length];
			batchVolumeCounts = new int[jobs.Length];
			batchAnchorOffsets = new int[jobs.Length];
		}

		// Pack deformation volumes into one buffer with per-job ranges (m_frameVolumes), and rigidly deformed jobs' anchors
		// in their mesh's bone order after an unused first one (m_frameAnchors)
		frameVolumes.Clear();
		frameAnchors.Clear();
		frameAnchors.Add( default );
		foreach ( ref var job in jobs )
		{
			if ( !job.Object.IsDeformed ) continue;
			job.VolumeOffset = frameVolumes.Count;
			frameVolumes.AddRange( job.Object.DeformationVolumes );

			var anchors = job.Object.DeformationAnchors;
			job.AnchorOffset = anchors.Length > 0 ? frameAnchors.Count : 0;
			if ( anchors.Length == 0 ) continue;

			foreach ( var master in job.Object.Mesh.SkinFor( job.ModelMesh ).MasterBones )
				frameAnchors.Add( (uint)master < (uint)anchors.Length ? anchors[master] : default );
		}

		skinningVolumes = frameVolumes.Count > 0 ? volumeRing.Upload( context, CollectionsMarshal.AsSpan( frameVolumes ) ) : null;
		skinningAnchors = frameVolumes.Count > 0 ? anchorRing.Upload( context, CollectionsMarshal.AsSpan( frameAnchors ) ) : null;
		return true;
	}

	GpuBuffer skinningVolumes, skinningAnchors;

	/// <summary>
	/// Record the prepared batches' dispatches, between the vertex cache's barriers.
	/// </summary>
	void RecordSkinning( GpuContext context )
	{
		var jobs = CollectionsMarshal.AsSpan( skinJobs );
		var volumeBuffer = skinningVolumes;
		var anchorBuffer = skinningAnchors;

		context.BarrierVertexCacheToWrite();
		for ( int first = 0; first < jobs.Length; )
		{
			var mesh = jobs[first].Object.Mesh;
			var key = SkinningKey( mesh );
			var modelMesh = jobs[first].ModelMesh;
			var deformed = jobs[first].Object.IsDeformed;
			var morphed = jobs[first].Morphed;

			var count = 0;
			while ( first + count < jobs.Length && SkinningKey( jobs[first + count].Object.Mesh ) == key && jobs[first + count].ModelMesh == modelMesh && jobs[first + count].Object.IsDeformed == deformed && jobs[first + count].Morphed == morphed )
			{
				ref var job = ref jobs[first + count];
				batchSlots[count] = job.Slot;
				batchOffsets[count] = job.VertexCacheOffset;
				batchVolumeOffsets[count] = job.VolumeOffset;
				batchVolumeCounts[count] = deformed ? job.Object.DeformationVolumes.Length : 0;
				batchAnchorOffsets[count] = job.AnchorOffset;
				count++;
			}

			context.DispatchSkinning( skinningShader, mesh, modelMesh, frameTransforms.Current, batchSlots.AsSpan( 0, count ), batchOffsets.AsSpan( 0, count ), mesh.SkinFor( modelMesh ).BlendWeightCount,
				deformed ? volumeBuffer : null, batchVolumeOffsets.AsSpan( 0, count ), batchVolumeCounts.AsSpan( 0, count ), anchorBuffer, batchAnchorOffsets.AsSpan( 0, count ), morphed );
			first += count;
		}
		context.BarrierVertexCacheToRead();
	}

	/// <summary>
	/// What skinning jobs batch by: the mesh's geometry, which its variants (body groups, materials) share with their skinning, as
	/// native batches by render mesh (<c>CSceneSystem::CanBatchVertexCacheRequests</c>), or the mesh itself without one (tests).
	/// </summary>
	static object SkinningKey( RenderMesh mesh ) => (object)mesh.Geometry ?? mesh;
	ulong[] skinSortKeys = [];

	int[] batchSlots = [];
	int[] batchOffsets = [];
	int[] batchVolumeOffsets = [];
	int[] batchVolumeCounts = [];
	int[] batchAnchorOffsets = [];
	readonly List<DeformationVolume> frameVolumes = new();
	readonly UploadRing<DeformationVolume> volumeRing = new( "Borealis DeformationVolumes" );
	readonly List<Vector4> frameAnchors = new();
	readonly UploadRing<Vector4> anchorRing = new( "Borealis DeformationAnchors" );
}
