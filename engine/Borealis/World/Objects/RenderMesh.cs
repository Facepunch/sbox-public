using System.Linq;
using System.Threading;

namespace Borealis;

/// <summary>
/// Per-LOD draws referencing the model's compiled buffers without copying, equivalent to native's <c>CRenderMesh</c>.
/// </summary>
public sealed class RenderMesh
{
	/// <summary>
	/// Model draw-call range and material flags (<c>ComputeSceneObjectFlagsFromDrawCalls</c>).
	/// Fast-path depth uses <c>depth_only.vmat</c>; lightmapped draws use object lightmap attributes.
	/// <paramref name="Tint"/> is a linear per-draw multiplier, or null for white (<c>CMaterialDrawDescriptor::m_vTintColor</c>).
	/// <paramref name="UvDensity"/> is its textures' texels per world unit, or 0 if unknown (<c>m_flUvDensity</c>), which
	/// texture streaming's sizes start from (<see cref="TextureSizes"/>).
	/// </summary>
	public readonly record struct Draw( int Mesh, int DrawCall, int StartIndex, int IndexCount, int VertexCount, int PrimitiveType, GpuMaterial Material,
		bool Translucent = false, bool AlphaTest = false, bool ShadowFastPath = false, bool Lightmapped = false, Vector3? Tint = null, float UvDensity = 0 );

	/// <summary>
	/// Whether a material blends, as native reads its <c>translucent</c> attribute.
	/// </summary>
	internal static bool IsTranslucent( GpuMaterial material ) => material is { IsTranslucent: true };

	/// <summary>
	/// Whether a material alpha tests, as native reads its <c>alphatest</c> attribute.
	/// </summary>
	internal static bool IsAlphaTest( GpuMaterial material ) => material is { IsAlphaTest: true };

	/// <summary>
	/// Read the material's frame-copy flag (<c>DefaultMaterialBasedSceneObjectUpdater</c>).
	/// </summary>
	internal static bool ReadsFrameBuffer( GpuMaterial material ) => material is { ReadsFrameBuffer: true };

	internal static bool NeedsLightProbe( GpuMaterial material ) => material is { NeedsLightProbe: true };

	/// <summary>
	/// The geometry its draws bind, or null for a mesh that can't render (tests).
	/// </summary>
	public GpuMesh Geometry { get; }
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

	/// <summary>Its LOD switch distances (<see cref="LodForScreenSize(float, float)"/>).</summary>
	internal ReadOnlySpan<float> SwitchDistances => switchDistances;

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
	readonly ulong[][] drawSimilarity;
	MeshSkin[] skins = [];

	/// <summary>
	/// Cached first-material similarity key for sorting runs and reducing rebinds (<c>IMaterial2::GetSimilarityKey</c>).
	/// </summary>
	internal ulong SimilarityForLod( int lod ) => lodSimilarity[Base( lod )];

	/// <summary>
	/// A draw's material's similarity key, cached for sorting draws by bind state (<c>IMaterial2::GetSimilarityKey</c>).
	/// </summary>
	internal ulong SimilarityForDraw( int lod, int draw ) => drawSimilarity[Base( lod )][draw];

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
	internal ReadOnlySpan<int> MeshesForLod( int lod ) => lodMeshes[Base( lod )];

	/// <summary>
	/// Model mesh's block index within an instanced skinned LOD.
	/// </summary>
	internal int BlockForLod( int lod, int mesh ) => lodMeshBlocks[Base( lod )][mesh];

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
	internal int TintOffsetForLod( int lod ) => tintOffsets[Base( lod )];

	/// <summary>
	/// A model mesh's skinning, or null if it isn't skinned.
	/// </summary>
	internal MeshSkin SkinFor( int mesh ) => (uint)mesh < (uint)skins.Length ? skins[mesh] : null;

	/// <summary>
	/// Give the mesh skinning data - what a backend reads from its model, and what the tests make up.
	/// </summary>
	internal void SetSkinning( MeshSkin[] meshSkins, Matrix3x4[] bindPose )
	{
		skins = meshSkins ?? [];
		BindPose = bindPose ?? [];
		IsSkinned = skins.Any( x => x is { BoneCount: > 0 } );
	}

	/// <summary>
	/// A mesh drawing <paramref name="geometry"/>: its LODs' draws, from LOD 0, and the screen sizes they switch at.
	/// </summary>
	public RenderMesh( GpuMesh geometry, BBox bounds, Draw[][] lods, float[] switchDistances = null ) : this( bounds, lods, switchDistances )
	{
		Geometry = geometry;
	}

	/// <summary>
	/// Native per-object draw state after body groups and material overrides (<c>RenderTools.GetSceneObjectDraw</c>).
	/// Flags reflect the effective material (<c>CMeshSystem::UpdateDrawCallFlags</c>).
	/// </summary>
	internal readonly record struct DrawState( int Mesh, int DrawCall, GpuMaterial Material, bool Visible, bool Translucent, bool ShadowFastPath, bool Lightmapped = false );

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

				// A draw state without a material (one the device can't load) keeps the draw's own
				var material = state.Material ?? draw.Material;
				var alphaTest = IsAlphaTest( material );
				var drawn = draw with { Material = material, Translucent = state.Translucent, AlphaTest = alphaTest, ShadowFastPath = state.ShadowFastPath, Lightmapped = state.Lightmapped };
				changed |= drawn != draw;
				draws.Add( drawn );
			}

			variantLods[lod] = draws.ToArray();
		}

		if ( !changed ) return this;

		var variant = new RenderMesh( Geometry, Bounds, variantLods, switchDistances );
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

	bool? needsStencil;

	/// <summary>
	/// Whether any LOD draws with a material that needs a stencil (<see cref="GpuMaterial.NeedsStencil"/>). Cached on first
	/// main-thread access.
	/// </summary>
	internal bool NeedsStencil => needsStencil ??= lods.Any( lod => lod.Any( draw => draw.Material is { NeedsStencil: true } ) );

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
		drawSimilarity = lods.Select( l => l.Select( d => d.Material?.SimilarityKey ?? 0 ).ToArray() ).ToArray();
		lodSimilarity = drawSimilarity.Select( l => l.Length > 0 ? l[0] : 0 ).ToArray();
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
	internal MeshBlend BlendForLod( int lod ) => blends[Base( lod )];

	static MeshBlend BlendOf( Draw[] draws )
	{
		var translucent = draws.Count( x => x.Translucent );
		return translucent == 0 ? MeshBlend.Opaque : translucent == draws.Length ? MeshBlend.Translucent : MeshBlend.Partial;
	}

	/// <summary>
	/// LOD draws without enumerator boxing. A LOD past the model's own is a generated level (<see cref="LevelCount"/>).
	/// </summary>
	internal ReadOnlySpan<Draw> DrawsForLod( int lod ) => lod < lods.Length ? lods[lod] : levelDraws[lod - lods.Length];

	internal int TrianglesForLod( int lod ) => lod < lods.Length ? triangles[lod] : levelTriangles[lod - lods.Length];

	/// <summary>The model's own LOD a LOD index stands for: a generated level is LOD 0's draws over fewer indices.</summary>
	int Base( int lod ) => lod < lods.Length ? lod : 0;

	// Generated levels of a model without LODs of its own (GpuMesh.LevelsFor): LOD 0's draws over each level's indices (a draw
	// with fewer levels keeps its coarsest, one with none its own), the largest of their errors, and their triangles. Made
	// once every draw's are known; until then each newly published level makes the mesh ask again
	Draw[][] levelDraws = [];
	float[] levelErrors = [];
	int[] levelTriangles = [];
	int levelsAsked = -1;
	const int LevelsSettled = int.MaxValue;
	readonly object levelsLock = new();

	/// <summary>
	/// Generated levels it has: LODs 1 to this count draw them, LOD 0 the model's own draws (<see cref="RefreshLevels"/>).
	/// Only a model without LODs of its own has any.
	/// </summary>
	internal int LevelCount => Volatile.Read( ref levelDraws ).Length;

	/// <summary>
	/// The error of generated level <paramref name="level"/> (0 for the model's own draws): the largest of its draws', as a
	/// fraction of their bounds' half diagonal, which a view scales by the object's world bounds.
	/// </summary>
	internal float LevelError( int level ) => level <= 0 ? 0 : levelErrors[level - 1];

	/// <summary>
	/// Ask the geometry for its draws' generated levels while some are still to come, again only once the device has
	/// published more (<paramref name="version"/>, <see cref="GpuDevice.LevelsVersion"/>). Any thread.
	/// </summary>
	internal void RefreshLevels( int version )
	{
		if ( Volatile.Read( ref levelsAsked ) is LevelsSettled || levelsAsked == version ) return;
		lock ( levelsLock )
		{
			if ( levelsAsked is LevelsSettled || levelsAsked == version ) return;
			levelsAsked = version;
			if ( Geometry is null || lods.Length != 1 )
			{
				levelsAsked = LevelsSettled;
				return;
			}

			var draws = lods[0];
			var count = 0;
			for ( int d = 0; d < draws.Length; d++ )
			{
				var levels = Geometry.LevelsFor( draws[d] );
				if ( ReferenceEquals( levels, GeneratedLevels.Pending ) ) return;
				count = Math.Max( count, levels.Ranges.Length );
			}

			levelsAsked = LevelsSettled;
			if ( count == 0 ) return;

			var made = new Draw[count][];
			var errors = new float[count];
			var levelTris = new int[count];
			for ( int k = 0; k < count; k++ )
			{
				made[k] = new Draw[draws.Length];
				for ( int d = 0; d < draws.Length; d++ )
				{
					var levels = Geometry.LevelsFor( draws[d] );
					var draw = draws[d];
					if ( levels.Ranges.Length > 0 )
					{
						var at = Math.Min( k, levels.Ranges.Length - 1 );
						var (start, indexCount) = levels.Ranges[at];
						draw = draw with { StartIndex = start, IndexCount = indexCount };
						errors[k] = Math.Max( errors[k], levels.Errors[at] );
					}

					made[k][d] = draw;
					levelTris[k] += draw.IndexCount / 3;
				}
			}

			levelErrors = errors;
			levelTriangles = levelTris;
			Volatile.Write( ref levelDraws, made );
		}
	}

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
