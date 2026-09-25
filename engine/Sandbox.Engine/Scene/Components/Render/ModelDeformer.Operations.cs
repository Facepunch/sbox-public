using System.Text.Json.Serialization;

namespace Sandbox;

public sealed partial class ModelDeformer
{
	/// <summary>
	/// Operation with an editable edge transition; infinite shapes do not have an edge.
	/// </summary>
	[Hide, JsonIgnore]
	public OperationType? BoundedOperation => IsInfinite ? null : Operation;

	/// <summary>
	/// How gradually inflation fades near the shape's edge. Zero keeps inflation broad;
	/// one softens it across the whole shape, concentrating the effect toward the center.
	/// </summary>
	[Property, JsonIgnore, Order( 110 ), Title( "Edge Softness" ), Range( 0, 1 ), ShowIf( nameof( BoundedOperation ), OperationType.Inflate )]
	public float InflationSoftness
	{
		get => Falloff;
		set => Falloff = value;
	}

	/// <summary>
	/// How much of the shape blends the transform into the surrounding mesh.
	/// Zero transforms the entire interior equally; one blends from the center to the edge.
	/// </summary>
	[Property, JsonIgnore, Order( 110 ), Title( "Blend Width" ), Range( 0, 1 ), ShowIf( nameof( BoundedOperation ), OperationType.Transform )]
	public float TransformBlend
	{
		get => Falloff;
		set => Falloff = value;
	}

	/// <summary>
	/// Width of the transition between the squashed or stretched area and the surrounding mesh.
	/// Zero gives an abrupt boundary; one spreads the transition across the whole shape.
	/// </summary>
	[Property, JsonIgnore, Order( 110 ), Title( "Transition Width" ), Range( 0, 1 ), ShowIf( nameof( BoundedOperation ), OperationType.SquashStretch )]
	public float StretchTransition
	{
		get => Falloff;
		set => Falloff = value;
	}

	/// <summary>
	/// How strongly the pull is concentrated near the center. Zero pulls the entire interior equally;
	/// one gradually weakens the pull from the center toward the edge, leaving the rim in place.
	/// </summary>
	[Property, JsonIgnore, Order( 110 ), Title( "Pull Focus" ), Range( 0, 1 ), ShowIf( nameof( BoundedOperation ), OperationType.Suck )]
	public float PullFocus
	{
		get => Falloff;
		set => Falloff = value;
	}

	/// <summary>
	/// Local Z scale. One is unchanged; below one squashes and above one stretches.
	/// X/Y compensate to preserve volume before Value and the shape's falloff are applied.
	/// At zero the height is flat; width compensation is capped at ten near zero to stay finite.
	/// </summary>
	[Property, Order( 102 ), Range( 0, 2 ), ShowIf( nameof( Operation ), OperationType.SquashStretch )]
	public float StretchRatio
	{
		get;
		set => field = float.IsFinite( value ) ? Math.Clamp( value, 0, 2 ) : 1;
	} = 1.25f;

	/// <summary>
	/// Signed inflation: negative values contract, zero leaves the model unchanged, and positive values expand.
	/// The shape boundary limits displacement; capsules expand or contract around their axis.
	/// </summary>
	[Property, Order( 102 ), Range( -1, 1 ), ShowIf( nameof( Operation ), OperationType.Inflate )]
	public float Inflation
	{
		get;
		set => field = float.IsFinite( value ) ? Math.Clamp( value, -1, 1 ) : 0;
	} = 1;

	/// <summary>
	/// Position offset in the shape's local axes.
	/// </summary>
	[Property, Order( 102 ), Title( "Offset" ), ShowIf( nameof( Operation ), OperationType.Transform )]
	public Vector3 Translation
	{
		get;
		set => field = value.IsFinite ? value : Vector3.Zero;
	}

	/// <summary>
	/// Rotation around the shape center, applied after scale and before translation.
	/// </summary>
	[Property, Order( 103 ), Title( "Rotation" ), ShowIf( nameof( Operation ), OperationType.Transform )]
	public Angles RotationOffset
	{
		get;
		set => field = Rotation.From( value ).IsFinite ? value : Angles.Zero;
	}

	/// <summary>
	/// Scale on each local axis around the shape center.
	/// </summary>
	[Property, Order( 104 ), Title( "Scale" ), ShowIf( nameof( Operation ), OperationType.Transform )]
	public Vector3 ScaleFactor
	{
		get;
		set => field = value.IsFinite ? value : Vector3.One;
	} = Vector3.One;

	private void PackOperation( ref SceneDeformationVolumeData data )
	{
		if ( Operation != OperationType.Transform )
		{
			// Clear cached transform rows when switching modes; the other operations never read them.
			data.TransformRow0 = default;
			data.TransformRow1 = default;
			data.TransformRow2 = default;
			return;
		}

		var transform = new Transform( Translation, Rotation.From( RotationOffset ), ScaleFactor );
		System.Numerics.Matrix4x4 matrix = Matrix.FromTransform( transform );
		matrix = System.Numerics.Matrix4x4.Transpose( matrix );
		data.TransformRow0 = new( matrix.M11, matrix.M12, matrix.M13, matrix.M14 );
		data.TransformRow1 = new( matrix.M21, matrix.M22, matrix.M23, matrix.M24 );
		data.TransformRow2 = new( matrix.M31, matrix.M32, matrix.M33, matrix.M34 );
	}
}
