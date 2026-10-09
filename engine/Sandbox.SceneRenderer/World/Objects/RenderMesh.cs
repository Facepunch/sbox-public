using System.Linq;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Per-LOD draws referencing the model's compiled buffers without copying, equivalent to native's <c>CRenderMesh</c>.
/// </summary>
public sealed class RenderMesh
{
	/// <summary>
	/// Model draw-call range and material flags (<c>ComputeSceneObjectFlagsFromDrawCalls</c>).
	/// Fast-path depth uses <c>depth_only.vmat</c>; lightmapped draws use object lightmap attributes.
	/// <paramref name="Tint"/> is a linear per-draw multiplier, or null for white (<c>CMaterialDrawDescriptor::m_vTintColor</c>).
	/// </summary>
	public readonly record struct Draw( int Mesh, int DrawCall, int StartIndex, int IndexCount, int VertexCount, int PrimitiveType, Material Material,
		bool Translucent = false, bool AlphaTest = false, bool ShadowFastPath = false, bool Lightmapped = false, Vector3? Tint = null );

	/// <summary>
	/// Whether a material blends, as native reads its <c>translucent</c> attribute.
	/// </summary>
	internal static bool IsTranslucent( Material material ) => material is not null && material.native.GetIntAttributeOrDefault( "translucent", 0 ) != 0;

	/// <summary>
	/// Whether a material alpha tests, as native reads its <c>alphatest</c> attribute.
	/// </summary>
	internal static bool IsAlphaTest( Material material ) => material is not null && material.native.GetIntAttributeOrDefault( "alphatest", 0 ) != 0;

	/// <summary>
	/// Read the material's frame-copy flag (<c>DefaultMaterialBasedSceneObjectUpdater</c>).
	/// </summary>
	internal static bool ReadsFrameBuffer( Material material ) => material is not null && material.native.GetIntAttributeOrDefault( "bWantsFBCopyTexture", 0 ) != 0;

	/// <summary>
	/// Model owning the draw buffers.
	/// </summary>
	public Model Model { get; }
	/// <summary>
	/// The model's bounds, in its own space.
	/// </summary>
	public BBox Bounds { get; }

	/// <summary>
	/// LOD 0's draws.
	/// </summary>
	public IReadOnlyList<Draw> Draws => lods[0];

	/// <summary>
	/// How many LODs the model has - at least one.
	/// </summary>
	public int LodCount => lods.Length;

	/// <summary>
	/// Triangles drawn per instance at LOD 0.
	/// </summary>
	public int TriangleCount => triangles[0];

	/// <summary>
	/// Bone mappings, inverse bind poses and vertex weights (<c>CAnimatableSceneObjectDesc::SetupBones</c>).
	/// </summary>
	internal sealed class MeshSkin
	{
		public int BlendWeightCount;

		/// <summary>
		/// Vertices across the mesh's vertex buffers - its block of the vertex cache.
		/// </summary>
		public int VertexCount;

		public Matrix3x4[] InverseBindPoses;
		public int[] MasterBones;

		public int BoneCount => MasterBones.Length;

		/// <summary>
		/// Supports morphs from the composite atlas during compute skinning (<c>CAnimatableSceneObjectDesc::IsMorphing</c>).
		/// </summary>
		public bool Morphs;

		/// <summary>
		/// Multiple weights require compute skinning. Single-weight meshes use it only for deformation or morphs.
		/// </summary>
		public bool UsesVertexCache => BlendWeightCount > 1;
	}

	readonly Draw[][] lods;
	readonly int[] triangles;
	readonly float[] switchDistances;
	readonly MeshBlend[] blends;
	readonly int[][] lodMeshes;
	readonly int[][] lodMeshBlocks;
	readonly ulong[] lodSimilarity;
	MeshSkin[] skins = [];

	/// <summary>
	/// Cached first-material similarity key for sorting runs and reducing rebinds (<c>IMaterial2::GetSimilarityKey</c>).
	/// </summary>
	internal ulong SimilarityForLod( int lod ) => lodSimilarity[lod];

	/// <summary>
	/// The model's bones in bind pose, in model space - where a skinned object with no bones of its own is posed.
	/// </summary>
	internal Matrix3x4[] BindPose { get; private set; } = [];

	/// <summary>
	/// Whether any of the model's meshes has a skeleton to skin with.
	/// </summary>
	public bool IsSkinned { get; private set; }

	/// <summary>
	/// The model's meshes - what <see cref="Draw.Mesh"/> indexes.
	/// </summary>
	internal int ModelMeshCount => skins.Length;

	/// <summary>
	/// The model meshes a LOD's draws come from, each once, in order of first draw.
	/// </summary>
	internal ReadOnlySpan<int> MeshesForLod( int lod ) => lodMeshes[lod];

	/// <summary>
	/// Model mesh's block index within an instanced skinned LOD.
	/// </summary>
	internal int BlockForLod( int lod, int mesh ) => lodMeshBlocks[lod][mesh];

	readonly int[] tintOffsets;

	/// <summary>
	/// Whether rigid objects need one transform entry per draw, combining object and draw tints.
	/// Animated objects ignore draw tints, matching native.
	/// </summary>
	internal bool HasDrawTints { get; }

	/// <summary>
	/// An object's entries when <see cref="HasDrawTints"/>: one per draw of every LOD.
	/// </summary>
	internal int TintEntryCount { get; }

	/// <summary>
	/// Where a LOD's draws' entries start among an object's (<see cref="HasDrawTints"/>).
	/// </summary>
	internal int TintOffsetForLod( int lod ) => tintOffsets[lod];

	/// <summary>
	/// A model mesh's skinning, or null if it isn't skinned.
	/// </summary>
	internal MeshSkin SkinFor( int mesh ) => (uint)mesh < (uint)skins.Length ? skins[mesh] : null;

	/// <summary>
	/// Give the mesh skinning data - what <see cref="FromModel"/> reads, and what the tests make up.
	/// </summary>
	internal void SetSkinning( MeshSkin[] meshSkins, Matrix3x4[] bindPose )
	{
		skins = meshSkins ?? [];
		BindPose = bindPose ?? [];
		IsSkinned = skins.Any( x => x is { BoneCount: > 0 } );
	}

	RenderMesh( Model model, Draw[][] lods, float[] switchDistances ) : this( model.Bounds, lods, switchDistances )
	{
		Model = model;
	}

	/// <summary>
	/// Native per-object draw state after body groups and material overrides (<c>RenderTools.GetSceneObjectDraw</c>).
	/// Flags reflect the effective material (<c>CMeshSystem::UpdateDrawCallFlags</c>).
	/// </summary>
	internal readonly record struct DrawState( int Mesh, int DrawCall, Material Material, bool Visible, bool Translucent, bool ShadowFastPath, bool Lightmapped = false );

	/// <summary>
	/// Apply supplied draw states, omitting hidden draws and retaining unspecified ones.
	/// Returns this mesh if unchanged; variants share model buffers, skinning and LOD distances.
	/// </summary>
	internal RenderMesh WithDraws( ReadOnlySpan<DrawState> states )
	{
		var byDraw = new Dictionary<(int, int), DrawState>( states.Length );
		foreach ( var state in states ) byDraw[(state.Mesh, state.DrawCall)] = state;

		var changed = false;
		var variantLods = new Draw[lods.Length][];
		for ( int lod = 0; lod < lods.Length; lod++ )
		{
			var draws = new List<Draw>( lods[lod].Length );
			foreach ( var draw in lods[lod] )
			{
				if ( !byDraw.TryGetValue( (draw.Mesh, draw.DrawCall), out var state ) )
				{
					draws.Add( draw );
					continue;
				}

				if ( !state.Visible )
				{
					changed = true;
					continue;
				}

				var alphaTest = IsAlphaTest( state.Material );
				var drawn = draw with { Material = state.Material, Translucent = state.Translucent, AlphaTest = alphaTest, ShadowFastPath = state.ShadowFastPath, Lightmapped = state.Lightmapped };
				changed |= drawn != draw;
				draws.Add( drawn );
			}

			variantLods[lod] = draws.ToArray();
		}

		if ( !changed ) return this;

		var variant = Model is not null ? new RenderMesh( Model, variantLods, switchDistances ) : new RenderMesh( Bounds, variantLods, switchDistances );
		variant.SetSkinning( skins, BindPose );
		return variant;
	}

	bool? hasLightmappedDraws;

	/// <summary>
	/// Any of its draws, at any LOD, is <see cref="Draw.Lightmapped"/>. Worked out once.
	/// </summary>
	internal bool HasLightmappedDraws => hasLightmappedDraws ??= lods.Any( lod => lod.Any( draw => draw.Lightmapped ) );

	bool? readsFrameBuffer;

	/// <summary>
	/// Whether any LOD reads the frame copy. Cached on first main-thread access.
	/// </summary>
	internal bool ReadsFrameBufferCopy => readsFrameBuffer ??= lods.Any( lod => lod.Any( draw => ReadsFrameBuffer( draw.Material ) ) );

	/// <summary>
	/// Model-free mesh for sorting and culling tests; cannot render.
	/// </summary>
	internal RenderMesh( BBox bounds, Draw[][] lods, float[] switchDistances = null )
	{
		Bounds = bounds;
		this.lods = lods;
		this.switchDistances = switchDistances ?? [];
		triangles = lods.Select( l => l.Sum( x => x.IndexCount / 3 ) ).ToArray();
		blends = lods.Select( BlendOf ).ToArray();
		lodMeshes = lods.Select( l => l.Select( d => d.Mesh ).Distinct().ToArray() ).ToArray();
		lodSimilarity = lods.Select( l => l.Length > 0 && l[0].Material is { } material ? RenderContext.SimilarityKey( material ) : 0 ).ToArray();
		tintOffsets = new int[lods.Length];
		for ( int lod = 0; lod < lods.Length; lod++ )
		{
			tintOffsets[lod] = TintEntryCount;
			TintEntryCount += lods[lod].Length;
		}
		HasDrawTints = lods.Any( lod => lod.Any( draw => draw.Tint is not null ) );

		lodMeshBlocks = lodMeshes.Select( meshes =>
		{
			var blocks = new int[meshes.Length == 0 ? 0 : meshes.Max() + 1];
			blocks.AsSpan().Fill( -1 );
			for ( int i = 0; i < meshes.Length; i++ ) blocks[meshes[i]] = i;
			return blocks;
		} ).ToArray();
	}

	/// <summary>
	/// Aggregate LOD blend mode (<c>ComputeSceneObjectTranslucencyFlags</c>).
	/// </summary>
	internal MeshBlend BlendForLod( int lod ) => blends[lod];

	static MeshBlend BlendOf( Draw[] draws )
	{
		var translucent = draws.Count( x => x.Translucent );
		return translucent == 0 ? MeshBlend.Opaque : translucent == draws.Length ? MeshBlend.Translucent : MeshBlend.Partial;
	}

	/// <summary>
	/// LOD draws without enumerator boxing.
	/// </summary>
	internal ReadOnlySpan<Draw> DrawsForLod( int lod ) => lods[lod];

	internal int TrianglesForLod( int lod ) => triangles[lod];

	/// <summary>
	/// Select LOD using <c>CModel::LODLevelForScreenSize</c>: scaled switch distance below 50 / screen width.
	/// Screen width measures a radius-0.5 sphere at the object's centre.
	/// </summary>
	internal int LodForScreenSize( float screenWidthInPixels, float scale )
	{
		return lods.Length == 1 ? 0 : LodForScreenSize( switchDistances, lods.Length, screenWidthInPixels, scale );
	}

	internal static int LodForScreenSize( ReadOnlySpan<float> switchDistances, int lodCount, float screenWidthInPixels, float scale )
	{
		var metric = screenWidthInPixels > 0 ? 50.0f / screenWidthInPixels : 0;

		// Search all switch distances before clamping to available LODs, matching native.
		var lod = Math.Max( switchDistances.Length - 1, 0 );
		for ( ; lod > 0; lod-- )
		{
			var distance = switchDistances[lod] * scale;
			if ( distance > 0 && distance < metric ) break;
		}

		return Math.Min( lod, lodCount - 1 );
	}

	/// <summary>
	/// Read all model LODs and switch distances. Throws without geometry.
	/// Keep the model alive while GPU frames use its buffers.
	/// </summary>
	public static RenderMesh FromModel( Model model )
	{
		ArgumentNullException.ThrowIfNull( model );

		var info = model.MeshInfo;
		var lodCount = Math.Max( 1, info.LodCount );
		var lods = new Draw[lodCount][];

		for ( int lod = 0; lod < lodCount; lod++ )
		{
			var draws = new List<Draw>();
			for ( int m = 0; m < info.Meshes.Length; m++ )
			{
				if ( (info.Meshes[m].LodMask & (1 << lod)) == 0 ) continue;

				var drawCalls = info.Meshes[m].DrawCalls;
				for ( int d = 0; d < drawCalls.Length; d++ )
				{
					var call = drawCalls[d];
					var material = call.Material;
					var translucent = IsTranslucent( material );
					var alphaTest = IsAlphaTest( material );
					var fastPath = material is not null && material.native.GetBoolAttributeOrDefault( "ShadowFastPath", false ) && !translucent && !alphaTest;
					draws.Add( new Draw( m, d, call.StartIndex, call.Indices, call.Vertices, call.PrimitiveType, material, translucent, alphaTest, fastPath, Tint: RenderContext.ReadDrawTint( model, m, d ) ) );
				}
			}

			lods[lod] = draws.ToArray();
		}

		if ( lods[0].Length == 0 )
			throw new InvalidOperationException( $"Model {model.ResourcePath} has no render geometry" );

		var mesh = new RenderMesh( model, lods, info.LodSwitchDistances ?? [] );

		var skins = new MeshSkin[info.Meshes.Length];
		for ( int m = 0; m < skins.Length; m++ )
			skins[m] = RenderContext.ReadMeshSkin( model, m );

		var bindPose = new Matrix3x4[model.BoneCount];
		for ( int b = 0; b < bindPose.Length; b++ )
			bindPose[b] = Matrix3x4.From( model.GetBoneTransform( b ) );

		mesh.SetSkinning( skins, bindPose );
		return mesh;
	}
}

/// <summary>
/// Aggregate blend mode corresponding to native's <c>IS_OPAQUE</c>/<c>IS_TRANSLUCENT</c> flags.
/// </summary>
internal enum MeshBlend
{
	/// <summary>
	/// No draw blends: opaque passes only.
	/// </summary>
	Opaque,

	/// <summary>
	/// Every draw blends: the translucent pass only, and the translucent shadow layer.
	/// </summary>
	Translucent,

	/// <summary>
	/// Mixed draws, split between opaque and translucent passes (<c>SCENEOBJECTTYPEFLAG_PARTIALLY_ALPHA_BLENDED</c>).
	/// </summary>
	Partial,
}
