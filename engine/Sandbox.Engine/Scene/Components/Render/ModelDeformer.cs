using Sandbox.Volumes;

namespace Sandbox;

/// <summary>
/// Deforms an ancestor model using an ordered stack of masked operations before bone skinning.
/// Bones, collision, hitboxes and attachments are unaffected.
/// </summary>
[Title( "Model Deformer" ), Category( "Rendering" ), Icon( "bubble_chart" )]
[Alias( "DeformationVolume", "Sandbox.DeformationVolume" )]
public sealed partial class ModelDeformer : VolumeComponent, Component.ExecuteInEditor, Editor.ISkinnedModelEditor
{
	/// <summary>
	/// An operation in the ordered deformation stack. Each operation receives the previous result.
	/// </summary>
	[Expose]
	public enum OperationType
	{
		/// <summary>
		/// Inflate or deflate the model. Negative Inflation shrinks; positive Inflation expands.
		/// </summary>
		[Icon( "open_in_full" )]
		Inflate = 0,

		/// <summary>
		/// Flatten or lengthen the model along the shape's local Z axis, adjusting its width to match.
		/// </summary>
		[Icon( "unfold_more" ), Title( "Squash / Stretch" )]
		SquashStretch = 1,

		/// <summary>
		/// Move, rotate or scale the affected part of the model around the shape center.
		/// </summary>
		[Icon( "transform" )]
		Transform = 2,

		/// <summary>
		/// Pull the model toward the shape center. Move the shape to aim the pull.
		/// </summary>
		[Icon( "filter_center_focus" )]
		Suck = 3,

		/// <summary>
		/// Push vertices out to the shape's surface. Use a Box to make a square shape or a Sphere to round it.
		/// Value blends from the original mesh to the surface. Requires a finite shape; Infinite has no surface.
		/// </summary>
		[Icon( "settings_overscan" )]
		Project = 4
	}

	/// <summary>
	/// How a volume changes the shading frame, independently of its vertex displacement.
	/// </summary>
	[Expose]
	public enum NormalModeType
	{
		/// <summary>
		/// Keep surface lighting aligned with the deformed mesh. Recommended for most models.
		/// </summary>
		[Icon( "change_history" )]
		Accurate = 0,

		/// <summary>
		/// Soften abrupt changes in surface lighting. Adjust Smoothing to choose how much detail to soften.
		/// </summary>
		[Icon( "blur_on" )]
		Smooth = 1,

		/// <summary>
		/// Keep the incoming surface lighting while this operation changes the shape.
		/// </summary>
		[Icon( "lock" ), Title( "Original" )]
		Preserve = 2
	}

	private ModelRenderer _owner;
	private Transform _packedPlacement;
	private bool _hasPackedData;

	/// <summary>
	/// Cached volume data ready for the native renderer.
	/// </summary>
	internal SceneDeformationVolumeData Data { get; private set; }

	/// <summary>
	/// Whether this volume can change any vertices.
	/// </summary>
	internal bool IsActive => Weight > 0 && (Operation switch
	{
		OperationType.Transform => Translation != Vector3.Zero || RotationOffset != Angles.Zero || ScaleFactor != Vector3.One,
		OperationType.SquashStretch => StretchRatio != 1,
		OperationType.Inflate => Inflation != 0,
		OperationType.Project => !IsInfinite,
		_ => true
	});

	/// <summary>
	/// Creates a sphere volume with a broad, smooth falloff.
	/// </summary>
	public ModelDeformer()
	{
		SceneVolume = new SceneVolume
		{
			Type = SceneVolume.VolumeTypes.Sphere,
			Sphere = new Sphere( 0, 24 ),
			Box = BBox.FromPositionAndSize( 0, 48 ),
			Capsule = new Capsule( Vector3.Down * 12, Vector3.Up * 12, 12 )
		};
	}

	/// <summary>
	/// Choose the affected area, then move or resize it in the scene. Infinite affects the whole model.
	/// Vertices affected by a finite shape stay inside its boundary.
	/// </summary>
	[Property, Editor( "model-deformer-shape" ), Header( "Shape" ), Order( -100 )]
	public override SceneVolume SceneVolume
	{
		get => base.SceneVolume;
		set => base.SceneVolume = value;
	}

	/// <summary>
	/// Operation applied inside the shape. Hover a button for its name and description.
	/// </summary>
	[Property, Editor( "model-deformer-operation" ), WideMode( HasLabel = false ), Order( 100 ), EnumButtonGroup( IconOnly = true )]
	public OperationType Operation { get; set; } = OperationType.Inflate;

	/// <summary>
	/// Width of the smooth edge fade, relative to the shape's radius or smallest half-extent.
	/// Zero applies full strength inside the shape; one fades across the entire interior.
	/// Infinite shapes always apply full strength and do not use falloff.
	/// </summary>
	[Property, Hide]
	public float Falloff
	{
		get;
		set => field = float.IsFinite( value ) ? Math.Clamp( value, 0, 1 ) : 0.5f;
	} = 0.5f;

	/// <summary>
	/// Blend from the original shape at zero to the full effect at one.
	/// Lower this to soften the result, or animate it to bring the effect in and out.
	/// </summary>
	[Property, Order( 101 ), Title( "Value" ), Range( 0, 1 )]
	public float Weight
	{
		get;
		set => field = float.IsFinite( value ) ? Math.Clamp( value, 0, 1 ) : 0;
	} = 1;

	/// <summary>
	/// Accurate follows the local deformation; Smooth softens its shading over a neighbourhood.
	/// Preserve moves vertices without correcting their shading. No mode changes the displacement itself.
	/// </summary>
	[Property, Title( "Normals" ), Space( 8 ), Order( 50 ), EnumDropdown]
	public NormalModeType NormalMode { get; set; }

	/// <summary>
	/// Distance over which surface shading is smoothed, in local units.
	/// Larger values soften more detail without changing the shape of the mesh.
	/// </summary>
	[Property, Order( 51 ), Title( "Smoothing" ), Range( 0.01f, 16 ), ShowIf( nameof( NormalMode ), NormalModeType.Smooth )]
	public float NormalSmoothingRadius
	{
		get;
		set => field = float.IsFinite( value ) ? Math.Max( 0.01f, value ) : 1;
	} = 1;

	/// <summary>
	/// Apply this effect to bone-merged clothing and accessories attached to the same model.
	/// </summary>
	[Property, Order( 111 ), Title( "Include Bone Merged" )]
	public bool ApplyToBoneMergedChildren { get; set; }

	/// <summary>
	/// Lower priorities deform the model first; higher priorities operate on their result.
	/// Use distinct priorities when the order of effects matters.
	/// </summary>
	[Property, Order( 112 )]
	public int Priority { get; set; }

	/// <summary>
	/// Ancestor renderer receiving this volume.
	/// </summary>
	public ModelRenderer Target => Components.GetInAncestors<ModelRenderer>();

	void Editor.ISkinnedModelEditor.AddBindPosePreviewTargets( HashSet<SkinnedModelRenderer> targets )
	{
		if ( Target is SkinnedModelRenderer renderer )
		{
			targets.Add( renderer );
		}
	}

	protected override void OnEnabled()
	{
		Scene.GetSystem<SceneDeformationSystem>().Add( this );
	}

	protected override void OnDisabled()
	{
		Scene.GetSystem<SceneDeformationSystem>().Remove( this );
		_owner?.ModelDeformers.Remove( this );
		_owner = null;
		Data = default;
		_hasPackedData = false;
	}

	internal void UpdateVolume()
	{
		var owner = Target;
		if ( owner != _owner )
		{
			_owner?.ModelDeformers.Remove( this );
			_owner = owner;
			_owner?.ModelDeformers.Add( this );

			if ( _owner.IsValid() )
			{
				Scene.GetSystem<SceneDeformationSystem>().Track( _owner );
			}
		}

		if ( !_owner.IsValid() || !_owner.SceneObject.IsValid() || !IsActive )
		{
			return;
		}

		CreateDeformationData( _owner.WorldTransform.ToLocal( WorldTransform ) );
	}

	/// <summary>
	/// Validates and packs a volume for the native scene model and deformation shader.
	/// </summary>
	internal SceneDeformationVolumeData CreateDeformationData( Transform placement )
	{
		GetShapeParameters( out var center, out var size, out var radius );
		ValidateDeformation( center, size, radius );

		var scaleVolume = placement.Scale.x * placement.Scale.y * placement.Scale.z;
		if ( !placement.Position.IsFinite || !placement.Scale.IsFinite || !placement.Rotation.IsFinite ||
			!float.IsFinite( scaleVolume ) || MathF.Abs( scaleVolume ) < 1e-8f )
		{
			throw new ArgumentException( "Volume placement must be finite and invertible.", nameof( placement ) );
		}

		var data = Data;
		if ( !_hasPackedData || !_packedPlacement.Equals( placement ) )
		{
			System.Numerics.Matrix4x4 volumeToModel = Matrix.FromTransform( placement );
			if ( !System.Numerics.Matrix4x4.Invert( volumeToModel, out var modelToVolume ) )
			{
				throw new ArgumentException( "Volume placement must be invertible.", nameof( placement ) );
			}

			// The shader uses column positions, whereas System.Numerics uses row vectors.
			volumeToModel = System.Numerics.Matrix4x4.Transpose( volumeToModel );
			modelToVolume = System.Numerics.Matrix4x4.Transpose( modelToVolume );

			data.ModelToVolumeRow0 = new Vector4( modelToVolume.M11, modelToVolume.M12, modelToVolume.M13, modelToVolume.M14 );
			data.ModelToVolumeRow1 = new Vector4( modelToVolume.M21, modelToVolume.M22, modelToVolume.M23, modelToVolume.M24 );
			data.ModelToVolumeRow2 = new Vector4( modelToVolume.M31, modelToVolume.M32, modelToVolume.M33, modelToVolume.M34 );
			data.VolumeToModelRow0 = new Vector4( volumeToModel.M11, volumeToModel.M12, volumeToModel.M13, volumeToModel.M14 );
			data.VolumeToModelRow1 = new Vector4( volumeToModel.M21, volumeToModel.M22, volumeToModel.M23, volumeToModel.M24 );
			data.VolumeToModelRow2 = new Vector4( volumeToModel.M31, volumeToModel.M32, volumeToModel.M33, volumeToModel.M34 );
		}

		data.SizeRadius = new Vector4( size.x, size.y, size.z, radius );
		data.AmountFalloff = new Vector4( StretchRatio, Inflation, 0, Falloff );
		data.WeightOperation = new Vector4( Weight, (int)Operation, (int)NormalMode, NormalSmoothingRadius );
		data.CenterShape = new Vector4( center.x, center.y, center.z, (int)SceneVolume.Type );
		PackOperation( ref data );

		if ( !data.ModelToVolumeRow0.IsFinite || !data.ModelToVolumeRow1.IsFinite || !data.ModelToVolumeRow2.IsFinite ||
			!data.VolumeToModelRow0.IsFinite || !data.VolumeToModelRow1.IsFinite || !data.VolumeToModelRow2.IsFinite )
		{
			throw new ArgumentException( "Volume placement must produce finite transform matrices.", nameof( placement ) );
		}

		Data = data;
		_packedPlacement = placement;
		_hasPackedData = true;
		return data;
	}

	/// <summary>
	/// Checks parameters before passing them across the native boundary.
	/// </summary>
	private void ValidateDeformation( Vector3 center, Vector3 size, float radius )
	{
		if ( !Enum.IsDefined( SceneVolume.Type ) || !Enum.IsDefined( Operation ) || !Enum.IsDefined( NormalMode ) ||
			!float.IsFinite( NormalSmoothingRadius ) || NormalSmoothingRadius < 0 ||
			(NormalMode == NormalModeType.Smooth && NormalSmoothingRadius < 0.01f) || !center.IsFinite || !size.IsFinite ||
			!float.IsFinite( radius ) || radius < 0 ||
			!float.IsFinite( Falloff ) || Falloff < 0 || Falloff > 1 || !float.IsFinite( Weight ) || Weight < 0 || Weight > 1 ||
			(SceneVolume.Type == SceneVolume.VolumeTypes.Box && (size.x < 0 || size.y < 0 || size.z < 0)) )
		{
			throw new ArgumentException( "Deformation parameters must be finite, with nonnegative shape dimensions, falloff in [0,1] and weight in [0,1]." );
		}
	}

	private void GetShapeParameters( out Vector3 center, out Vector3 size, out float radius )
	{
		center = SceneVolume.Center;
		size = SceneVolume.Type == SceneVolume.VolumeTypes.Box ? SceneVolume.Box.Size * 0.5f : Vector3.Zero;
		radius = 0;
		if ( SceneVolume.Type == SceneVolume.VolumeTypes.Sphere )
		{
			radius = SceneVolume.Sphere.Radius;
		}
		else if ( SceneVolume.Type == SceneVolume.VolumeTypes.Capsule )
		{
			size = (SceneVolume.Capsule.CenterB - SceneVolume.Capsule.CenterA) * 0.5f;
			radius = SceneVolume.Capsule.Radius;
		}
	}
}
