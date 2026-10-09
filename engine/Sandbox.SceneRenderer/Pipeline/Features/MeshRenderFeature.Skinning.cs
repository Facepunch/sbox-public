using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.SceneRenderer.Features;

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
		/// Its morphs' handle this frame (<see cref="RenderContext.QueueMorph"/>), or -1 when it doesn't morph.
		/// </summary>
		public int Morph;

		public readonly bool Morphed => Morph >= 0;
	}

	readonly List<SkinJob> skinJobs = new();
	int skinJobsGeneration = -1;
	Matrix3x4[] skinScratch = new Matrix3x4[64];
	TransformBuffer frameTransforms;
	Material skinningShader;

	/// <summary>
	/// The compute shader native skins with (<c>CSceneSystem</c>'s <c>m_hCsSkinningMat</c>).
	/// </summary>
	public const string SkinningShader = "materials/dev/skinning_cs.vmat";

	/// <summary>
	/// Write bones once per mesh per frame: world pose after inverse bind pose (<c>SetupBones</c>).
	/// Skeleton-free meshes stay rigid. CPU-only; cache allocation waits until <see cref="BeforeUpload"/>.
	/// </summary>
	void WriteBones( MeshObject obj, int lod, in Matrix objectToWorld, TransformBuffer transforms )
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
				obj.SkinSlots[m] = transforms.Add( objectToWorld, obj.Tint );
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

			var slot = transforms.AddSkinned( obj.Tint, skin.BlendWeightCount, skinScratch.AsSpan( 0, skin.BoneCount ) );
			obj.SkinSlots[m] = slot;

			// Deformation and morphs require compute skinning even with one weight.
			if ( skin.UsesVertexCache || obj.IsDeformed || (skin.Morphs && obj.MorphSource is not null) ) skinJobs.Add( new SkinJob( obj, m, slot, -1 ) { Morph = -1 } );
		}
	}

	/// <summary>
	/// Allocate vertex-cache blocks and finalize morph entries before upload.
	/// </summary>
	internal override void BeforeUpload( RenderContext context, TransformBuffer transforms )
	{
		frameTransforms = transforms;
		if ( skinJobsGeneration != transforms.Generation ) skinJobs.Clear();

		var jobs = CollectionsMarshal.AsSpan( skinJobs );
		var morphs = false;
		for ( int i = 0; i < jobs.Length; i++ )
		{
			ref var job = ref jobs[i];
			var skin = job.Object.Mesh.SkinFor( job.ModelMesh );
			job.VertexCacheOffset = RenderContext.AllocateVertexCache( skin.VertexCount );
			transforms.SetVertexCacheOffset( job.Slot, job.VertexCacheOffset );

			// Evaluate and queue morphs (FindOrAllocateTransforms).
			job.Morph = skin.Morphs && job.Object.MorphSource is { } source && source.IsValid() ? RenderContext.QueueMorph( source, job.Object.Mesh.Model, job.ModelMesh ) : -1;
			morphs |= job.Morphed;
		}

		if ( !morphs ) return;

		// Composite before submission, then store atlas subrects.
		RenderContext.GenerateMorphs();
		for ( int i = 0; i < jobs.Length; i++ )
		{
			if ( jobs[i].Morphed ) transforms.SetMorphSubrect( jobs[i].Slot, RenderContext.MorphSubrect( jobs[i].Morph ) );
		}
	}

	/// <summary>
	/// Compute-skin uploaded transforms before any render pass.
	/// </summary>
	void DispatchSkinning( RenderContext context )
	{
		if ( skinJobs.Count == 0 || frameTransforms is null ) return;

		skinningShader ??= Material.Load( SkinningShader );

		// Batch matching meshes; deformation/morph combos must match (CSceneSystem::CanBatchVertexCacheRequests).
		var jobs = CollectionsMarshal.AsSpan( skinJobs );
		jobs.Sort( static ( a, b ) =>
		{
			var byDeformed = a.Object.IsDeformed.CompareTo( b.Object.IsDeformed );
			if ( byDeformed != 0 ) return byDeformed;
			var byMorphed = a.Morphed.CompareTo( b.Morphed );
			if ( byMorphed != 0 ) return byMorphed;
			var byMesh = RuntimeHelpers.GetHashCode( a.Object.Mesh ).CompareTo( RuntimeHelpers.GetHashCode( b.Object.Mesh ) );
			return byMesh != 0 ? byMesh : a.ModelMesh.CompareTo( b.ModelMesh );
		} );

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

		GpuBuffer volumeBuffer = frameVolumes.Count > 0 ? volumeRing.Upload( context, CollectionsMarshal.AsSpan( frameVolumes ) ) : null;
		GpuBuffer anchorBuffer = frameVolumes.Count > 0 ? anchorRing.Upload( context, CollectionsMarshal.AsSpan( frameAnchors ) ) : null;

		context.BarrierVertexCacheToWrite();
		for ( int first = 0; first < jobs.Length; )
		{
			var mesh = jobs[first].Object.Mesh;
			var modelMesh = jobs[first].ModelMesh;
			var deformed = jobs[first].Object.IsDeformed;
			var morphed = jobs[first].Morphed;

			var count = 0;
			while ( first + count < jobs.Length && jobs[first + count].Object.Mesh == mesh && jobs[first + count].ModelMesh == modelMesh && jobs[first + count].Object.IsDeformed == deformed && jobs[first + count].Morphed == morphed )
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

	int[] batchSlots = [];
	int[] batchOffsets = [];
	int[] batchVolumeOffsets = [];
	int[] batchVolumeCounts = [];
	int[] batchAnchorOffsets = [];
	readonly List<SceneDeformationVolumeData> frameVolumes = new();
	readonly UploadRing<SceneDeformationVolumeData> volumeRing = new( "SceneRenderer DeformationVolumes" );
	readonly List<Vector4> frameAnchors = new();
	readonly UploadRing<Vector4> anchorRing = new( "SceneRenderer DeformationAnchors" );
}
