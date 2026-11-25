using System.Runtime.CompilerServices;

namespace Borealis;

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
			WantsStencil = MaterialOverride is { } stencilMaterial ? stencilMaterial.NeedsStencil : value?.NeedsStencil ?? false;
			UpdateAnimation();
			DrawChanged();
		}
	}

	/// <summary>
	/// Colour multiplier. Alpha fades opaque geometry with dithering and no prepass;
	/// translucent geometry becomes more transparent and stops casting shadows (<c>SetAlphaFade</c>).
	/// </summary>
	public Color Tint
	{
		get;
		set
		{
			// Only alpha reaches a shadow: a faded caster dithers. Fading or not decides its lists
			var faded = field.a != value.a;
			var lists = field.a < 1 != value.a < 1;
			var changed = !field.Equals( value );
			field = value;
			if ( faded ) ShadowChanged();
			if ( lists ) DrawChanged();
			if ( changed ) Changed( WorldNote.Entry );
		}
	} = Color.White;

	/// <summary>
	/// Replace all draw materials (<c>SceneObject.SetMaterialOverride</c>). Blend flags select passes; disables the depth fast path.
	/// </summary>
	public GpuMaterial MaterialOverride
	{
		get;
		set
		{
			var changed = field != value;
			field = value;
			OverrideTranslucent = RenderMesh.IsTranslucent( value );
			OverrideAlphaTest = RenderMesh.IsAlphaTest( value );
			WantsFrameBufferCopy = value is not null ? RenderMesh.ReadsFrameBuffer( value ) : mesh?.ReadsFrameBufferCopy ?? false;
			WantsStencil = value is not null ? value.NeedsStencil : mesh?.NeedsStencil ?? false;
			UpdateAnimation();
			if ( !changed ) return;
			ShadowChanged();
			DrawChanged();
		}
	}

	/// <inheritdoc/>
	internal override bool? MaterialAnimates => MaterialOverride is { } material ? material.AnimatesVertices : mesh is null ? false : mesh.AnimatesVertices;

	/// <summary>
	/// Whether a material it draws with needs a stencil in the view's depth (<see cref="GpuMaterial.NeedsStencil"/>).
	/// Recomputed when the mesh or material override changes.
	/// </summary>
	internal bool WantsStencil { get; private set; }

	/// <summary>
	/// Translucent draws need a frame copy and refraction depth (<c>SCENEOBJECTFLAG_WANTS_FRAMEBUFFER_COPY_TEXTURE</c>).
	/// Recomputed when the mesh or material override changes.
	/// </summary>
	public bool WantsFrameBufferCopy
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			DrawChanged();
		}
	}

	/// <summary>
	/// Work run around this object's draws in colour layers, or null. Prevents instancing.
	/// </summary>
	public DrawCallbacks Callbacks
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			DrawChanged();
		}
	}

	/// <summary>
	/// Per-object attributes override view attributes and prevent instancing (<c>CBaseSceneObjectDesc</c>).
	/// </summary>
	public ShaderParameters Attributes
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			DrawChanged();
		}
	}

	/// <summary>
	/// Where <see cref="Attributes"/> come from when they're read as the object draws rather than set, or null. The views of a
	/// render ask it once, for the objects they prepare (<see cref="RefreshAttributes"/>), so attributes that can change without
	/// notice aren't read for every object in the world each frame, off screen too.
	/// </summary>
	internal IAttributeSource AttributesFrom
	{
		get;
		set
		{
			if ( (field is null) != (value is null) ) AttributeSourceChanged( value is null ? -1 : 1 );
			field = value;
		}
	}

	/// <summary>
	/// The render <see cref="AttributesFrom"/> last brought <see cref="Attributes"/> up to date in.
	/// </summary>
	int attributesRender;

	/// <summary>
	/// Bring <see cref="Attributes"/> up to date from <see cref="AttributesFrom"/>, once a render. Main thread only.
	/// </summary>
	internal void RefreshAttributes()
	{
		if ( AttributesFrom is not { } source || attributesRender == RenderSystem.RenderNumber ) return;

		attributesRender = RenderSystem.RenderNumber;
		source.Refresh( this );
	}

	/// <summary>
	/// Forward-only lightmap attributes below object attributes (<c>SetBakedLightingAttributes</c>).
	/// Supplied by the bridge; prevents instancing.
	/// </summary>
	internal ShaderParameters LightmapAttributes
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			DrawChanged();
		}
	}

	/// <summary>
	/// The override's material flags, read once when it's set so preparing a frame doesn't call native.
	/// </summary>
	internal bool OverrideTranslucent { get; private set; }
	internal bool OverrideAlphaTest { get; private set; }

	/// <summary>
	/// How the object blends at a LOD: its override's blend, or its mesh's.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal MeshBlend BlendForLod( int lod )
	{
		if ( MaterialOverride is not null ) return OverrideTranslucent ? MeshBlend.Translucent : MeshBlend.Opaque;
		return Mesh?.BlendForLod( lod ) ?? MeshBlend.Opaque;
	}

	/// <summary>
	/// Drawn into shadow maps. On by default, like <c>ModelRenderer.RenderType</c>'s default.
	/// </summary>
	public bool CastShadows
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ShadowChanged();
		}
	} = true;

	/// <summary>
	/// Enable opaque draws in colour, prepass and shadows (<c>SCENEOBJECTFLAG_IS_OPAQUE</c>).
	/// </summary>
	public bool DrawsOpaque
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ShadowChanged();
			DrawChanged();
		}
	} = true;

	/// <summary>
	/// Enable translucent draws (<c>SCENEOBJECTFLAG_IS_TRANSLUCENT</c>).
	/// </summary>
	public bool DrawsTranslucent
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ShadowChanged();
			DrawChanged();
		}
	} = true;

	/// <summary>
	/// Include in the depth prepass (inverse of <c>SCENEOBJECTFLAG_NO_Z_PREPASS</c>).
	/// </summary>
	public bool DepthPrepass
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			DrawChanged();
		}
	} = true;

	/// <summary>
	/// Can be occlusion culled (inverse of <c>SCENEOBJECTFLAG_NO_OCCLUSION_CULLING</c>).
	/// </summary>
	public bool OcclusionCulling
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			DrawChanged();
		}
	} = true;

	/// <summary>
	/// Light non-lightmapped draws from a probe volume (<c>SCENEOBJECTFLAG_NEEDS_LIGHT_PROBE</c>).
	/// </summary>
	public bool NeedsLightProbe
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			DrawChanged();
		}
	}

	/// <summary>
	/// World-space probe selection point; null uses bounds centre (<c>CSceneObject::GetLightingOrigin</c>).
	/// </summary>
	public Vector3? LightingOrigin { get; set; }

	/// <summary>
	/// The light probe volume the map compiler bound it to (<see cref="LightProbeVolume.Handshake"/>), chosen before any by
	/// position; zero for none.
	/// </summary>
	public int LightProbeHandshake { get; set; }

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
	internal int ProbeHandshake;
	internal long ProbeVolumesKey = long.MinValue;

	/// <summary>
	/// Fixed LOD, clamped to the mesh's range; -1 selects by screen size (<c>CSceneObject::SetLOD</c>).
	/// </summary>
	public int LodOverride
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			DrawChanged();
		}
	} = -1;

	/// <summary>
	/// Include in the decal layer (<c>SCENEOBJECTFLAG_IS_DECAL</c>). Opaque/translucent flags can also enable other layers.
	/// </summary>
	public bool Decal
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			DrawChanged();
		}
	}

	/// <summary>
	/// Static-overlay sort order, lowest first (<c>RenderOrder</c>).
	/// </summary>
	public int RenderOrder
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			DrawChanged();
		}
	}

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

		// Set whole here: a pose written in place before it isn't taken over this one
		posePending = false;
	}

	// The pose being written (PoseBoneMatrices), drawn once it's taken (TakePose); and whether one was written since
	Matrix3x4[] poseWritten = [];
	volatile bool posePending;

	/// <summary>
	/// Pose a skinned mesh in place: room for <paramref name="count"/> world-space bone matrices, by model bone index, for the
	/// caller to fill - <see cref="SetBones"/> without converting or copying. <see cref="Bones"/> is left empty. The pose is a
	/// second array, drawn from once <see cref="TakePose"/> swaps it in: animation workers write the next frame's pose while a
	/// frame is drawn from the last one.
	/// </summary>
	internal Span<Matrix3x4> PoseBoneMatrices( int count )
	{
		if ( bones.Length != 0 ) bones = [];
		if ( poseWritten.Length != count ) poseWritten = new Matrix3x4[count];
		posePending = true;
		return poseWritten;
	}

	/// <summary>
	/// Draw the pose last written with <see cref="PoseBoneMatrices"/> from now on, if one was: a reference swap, at the sync
	/// point (the world writing the object's change), when nothing writes or draws it.
	/// </summary>
	internal void TakePose()
	{
		if ( !posePending ) return;
		posePending = false;
		(boneMatrices, poseWritten) = (poseWritten, boneMatrices);
	}

	/// <summary>
	/// <see cref="Bones"/> as matrices.
	/// </summary>
	internal ReadOnlySpan<Matrix3x4> BoneMatrices => boneMatrices;

	/// <summary>
	/// Whether it's drawn skinned, from bones - its mesh has a skeleton.
	/// </summary>
	internal bool IsSkinned
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		get => mesh is { IsSkinned: true };
	}

	/// <summary>
	/// Shader-layout deformation volumes (<c>SceneModel.DeformationVolumes</c>).
	/// Forces vertex-cache skinning; callers must update bounds.
	/// </summary>
	internal DeformationVolume[] DeformationVolumes
	{
		get;
		set
		{
			// Deformed or not decides how it instances
			var deformed = field.Length > 0 != value.Length > 0;
			field = value;
			if ( deformed ) DrawChanged();
		}
	} = [];

	/// <summary>
	/// A model-space anchor per model bone the volumes move it rigidly by (<c>SceneModel.DeformationAnchors</c>),
	/// or none to deform it as usual.
	/// </summary>
	internal Vector4[] DeformationAnchors { get; set; } = [];

	/// <summary>
	/// Whether any volumes deform it (<see cref="DeformationVolumes"/>).
	/// </summary>
	internal bool IsDeformed => DeformationVolumes.Length > 0 && IsSkinned;

	/// <summary>
	/// Supplies this frame's evaluated morphs; null when it doesn't morph.
	/// </summary>
	public IMorphSource MorphSource
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			DrawChanged();
		}
	}

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
