namespace Sandbox.SceneRenderer;

/// <summary>
/// A model drawn at a transform - the managed counterpart of a native <c>CSceneObject</c> with a model.
/// </summary>
public sealed class MeshObject : RenderObject
{
	RenderMesh mesh;

	/// <summary>
	/// A mesh drawn at a transform.
	/// </summary>
	public MeshObject( RenderMesh mesh, Transform transform )
	{
		Mesh = mesh;
		Transform = transform;
	}

	/// <summary>
	/// What to draw. Setting it sets <see cref="RenderObject.LocalBounds"/> to the mesh's. Null draws nothing.
	/// </summary>
	public RenderMesh Mesh
	{
		get => mesh;
		set
		{
			mesh = value;
			LocalBounds = value?.Bounds ?? default;
			WantsFrameBufferCopy = MaterialOverride is { } material ? RenderMesh.ReadsFrameBuffer( material ) : value?.ReadsFrameBufferCopy ?? false;
		}
	}

	/// <summary>
	/// Colour multiplier. Alpha fades opaque geometry with dithering and no prepass;
	/// translucent geometry becomes more transparent and stops casting shadows (<c>SetAlphaFade</c>).
	/// </summary>
	public Color Tint { get; set; } = Color.White;

	/// <summary>
	/// Replace all draw materials (<c>SceneObject.SetMaterialOverride</c>). Blend flags select passes; disables the depth fast path.
	/// </summary>
	public Material MaterialOverride
	{
		get;
		set
		{
			field = value;
			OverrideTranslucent = RenderMesh.IsTranslucent( value );
			OverrideAlphaTest = RenderMesh.IsAlphaTest( value );
			WantsFrameBufferCopy = value is not null ? RenderMesh.ReadsFrameBuffer( value ) : mesh?.ReadsFrameBufferCopy ?? false;
		}
	}

	/// <summary>
	/// Translucent draws need a frame copy and refraction depth (<c>SCENEOBJECTFLAG_WANTS_FRAMEBUFFER_COPY_TEXTURE</c>).
	/// Recomputed when the mesh or material override changes.
	/// </summary>
	public bool WantsFrameBufferCopy { get; set; }

	/// <summary>
	/// Per-object attributes override view attributes and prevent instancing (<c>CBaseSceneObjectDesc</c>).
	/// </summary>
	public RenderAttributes Attributes { get; set; }

	/// <summary>
	/// Forward-only lightmap attributes below object attributes (<c>SetBakedLightingAttributes</c>).
	/// Supplied by the bridge; prevents instancing.
	/// </summary>
	internal RenderAttributes LightmapAttributes { get; set; }

	/// <summary>
	/// The override's material flags, read once when it's set so preparing a frame doesn't call native.
	/// </summary>
	internal bool OverrideTranslucent { get; private set; }
	internal bool OverrideAlphaTest { get; private set; }

	/// <summary>
	/// How the object blends at a LOD: its override's blend, or its mesh's.
	/// </summary>
	internal MeshBlend BlendForLod( int lod )
	{
		if ( MaterialOverride is not null ) return OverrideTranslucent ? MeshBlend.Translucent : MeshBlend.Opaque;
		return Mesh?.BlendForLod( lod ) ?? MeshBlend.Opaque;
	}

	/// <summary>
	/// Drawn into shadow maps. On by default, like <c>ModelRenderer.RenderType</c>'s default.
	/// </summary>
	public bool CastShadows { get; set; } = true;

	/// <summary>
	/// Enable opaque draws in colour, prepass and shadows (<c>SCENEOBJECTFLAG_IS_OPAQUE</c>).
	/// </summary>
	public bool DrawsOpaque { get; set; } = true;

	/// <summary>
	/// Enable translucent draws (<c>SCENEOBJECTFLAG_IS_TRANSLUCENT</c>).
	/// </summary>
	public bool DrawsTranslucent { get; set; } = true;

	/// <summary>
	/// Include in the depth prepass (inverse of <c>SCENEOBJECTFLAG_NO_Z_PREPASS</c>).
	/// </summary>
	public bool DepthPrepass { get; set; } = true;

	/// <summary>
	/// Light non-lightmapped draws from a probe volume (<c>SCENEOBJECTFLAG_NEEDS_LIGHT_PROBE</c>).
	/// </summary>
	public bool NeedsLightProbe { get; set; }

	/// <summary>
	/// World-space probe selection point; null uses bounds centre (<c>CSceneObject::GetLightingOrigin</c>).
	/// </summary>
	public Vector3? LightingOrigin { get; set; }

	/// <summary>
	/// Probe light-group token (<c>LightGroup</c>); zero means none.
	/// </summary>
	public uint LightGroup { get; set; }

	/// <summary>
	/// Cached probe index (-1 for none) and selection inputs. Only main-view Prepare writes this cache.
	/// </summary>
	internal int ProbeChoice = -1;
	internal Vector3 ProbeCenter, ProbeExtents, ProbeOrigin;
	internal uint ProbeLightGroup;
	internal long ProbeVolumesKey = long.MinValue;

	/// <summary>
	/// Fixed LOD, clamped to the mesh's range; -1 selects by screen size (<c>CSceneObject::SetLOD</c>).
	/// </summary>
	public int LodOverride { get; set; } = -1;

	/// <summary>
	/// Include in the decal layer (<c>SCENEOBJECTFLAG_IS_DECAL</c>). Opaque/translucent flags can also enable other layers.
	/// </summary>
	public bool Decal { get; set; }

	/// <summary>
	/// Static-overlay sort order, lowest first (<c>RenderOrder</c>).
	/// </summary>
	public int RenderOrder { get; set; }

	Transform[] bones = [];
	Matrix3x4[] boneMatrices = [];

	/// <summary>
	/// World-space bones by model index. Empty uses the bind pose at the object's transform.
	/// Set <see cref="RenderObject.LocalBounds"/> to fit the pose; bones do not update bounds.
	/// </summary>
	public ReadOnlySpan<Transform> Bones => bones;

	/// <summary>
	/// Pose a skinned mesh: its bones' world transforms, by model bone index. They're copied.
	/// </summary>
	public void SetBones( ReadOnlySpan<Transform> worldBones )
	{
		if ( bones.Length != worldBones.Length )
		{
			bones = new Transform[worldBones.Length];
			boneMatrices = new Matrix3x4[worldBones.Length];
		}

		for ( int i = 0; i < worldBones.Length; i++ )
		{
			bones[i] = worldBones[i];
			boneMatrices[i] = Matrix3x4.From( worldBones[i] );
		}
	}

	/// <summary>
	/// <see cref="Bones"/> as matrices.
	/// </summary>
	internal ReadOnlySpan<Matrix3x4> BoneMatrices => boneMatrices;

	/// <summary>
	/// Whether it's drawn skinned, from bones - its mesh has a skeleton.
	/// </summary>
	internal bool IsSkinned => Mesh is { IsSkinned: true };

	/// <summary>
	/// Shader-layout deformation volumes (<c>CSceneAnimatableObject::SetDeformationVolumes</c>).
	/// Forces vertex-cache skinning; callers must update bounds.
	/// </summary>
	internal SceneDeformationVolumeData[] DeformationVolumes { get; set; } = [];

	/// <summary>
	/// A model-space anchor per model bone the volumes move it rigidly by (<c>CSceneAnimatableObject::SetDeformationAnchors</c>),
	/// or none to deform it as usual.
	/// </summary>
	internal Vector4[] DeformationAnchors { get; set; } = [];

	/// <summary>
	/// Whether any volumes deform it (<see cref="DeformationVolumes"/>).
	/// </summary>
	internal bool IsDeformed => DeformationVolumes.Length > 0 && IsSkinned;

	/// <summary>
	/// Native model supplying evaluated morph weights; null when unused.
	/// </summary>
	internal SceneObject MorphSource { get; set; }

	/// <summary>
	/// Per-mesh skinning slots shared by all views this frame.
	/// </summary>
	internal int[] SkinSlots = [];

	/// <summary>
	/// Last main-view LOD, reused by mid-frame <c>Graphics.Render</c> calls.
	/// </summary>
	internal int DrawnLod;
	internal int SkinGeneration = -1;
}
