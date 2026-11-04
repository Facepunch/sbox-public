using Sandbox.Volumes;
using System;

namespace Editor;

/// <summary>
/// Edits the deformer's existing shape fields with room for sphere radius and center on separate rows.
/// Other users of SceneVolume retain their usual inspector layout.
/// </summary>
[CustomEditor( NamedEditor = "model-deformer-shape" )]
public sealed class ModelDeformerShapeControlWidget : ControlWidget
{
	private readonly SerializedProperty _shapeType;
	private readonly Widget _radiusRow;
	private readonly Widget _centerRow;
	private readonly Widget _boxRow;
	private readonly Widget _capsuleRow;

	/// <inheritdoc />
	public override bool SupportsMultiEdit => true;

	/// <inheritdoc />
	public override bool IsWideMode => true;

	/// <inheritdoc />
	public override bool IncludeLabel => false;

	/// <summary>
	/// Builds controls over the original serialized fields, preserving scene data and Undo behavior.
	/// </summary>
	public ModelDeformerShapeControlWidget( SerializedProperty property ) : base( property )
	{
		PaintBackground = false;
		Layout = Layout.Column();
		Layout.Spacing = 4;

		property.TryGetAsObject( out var volume );
		_shapeType = volume.GetProperty( nameof( SceneVolume.Type ) );
		AddRow( "Shape Type", Create( _shapeType ) );

		volume.GetProperty( nameof( SceneVolume.Sphere ) ).TryGetAsObject( out var sphere );
		var radius = (FloatControlWidget)Create( sphere.GetProperty( nameof( Sphere.Radius ) ) );
		// The slider covers common sizes; typing still supports radii beyond its visible range.
		radius.MakeRanged( new Vector2( 0, 100 ), 0, false, true );
		_radiusRow = AddRow( "Radius", radius );
		_centerRow = AddRow( "Center", Create( sphere.GetProperty( nameof( Sphere.Center ) ) ) );
		_boxRow = AddRow( "Box", Create( volume.GetProperty( nameof( SceneVolume.Box ) ) ) );
		_capsuleRow = AddRow( "Capsule", Create( volume.GetProperty( nameof( SceneVolume.Capsule ) ) ) );

		UpdateShapeRows();
	}

	private Widget AddRow( string title, ControlWidget control )
	{
		var row = Layout.Add( new Widget( this ) );
		row.Layout = Layout.Row();
		row.Layout.Spacing = 4;
		// Match the standard inspector labels used by Normals, Smoothing and the operation controls.
		row.Layout.Add( new Label( title )
		{
			FixedWidth = 136,
			ToolTip = control.ToolTip,
			Color = Theme.TextControl.WithAlpha( 0.7f ),
			ContentMargins = new Sandbox.UI.Margin( 4, 0, 0, 0 )
		} );
		control.HorizontalSizeMode = SizeMode.Flexible;
		row.Layout.Add( control, 1 );
		return row;
	}

	protected override int ValueHash => HashCode.Combine( base.ValueHash, _shapeType?.IsMultipleDifferentValues );

	protected override void OnValueChanged()
	{
		base.OnValueChanged();
		UpdateShapeRows();
	}

	private void UpdateShapeRows()
	{
		var type = _shapeType.GetValue<SceneVolume.VolumeTypes>();
		var mixed = _shapeType.IsMultipleDifferentValues;
		_radiusRow.Visible = !mixed && type == SceneVolume.VolumeTypes.Sphere;
		_centerRow.Visible = !mixed && type == SceneVolume.VolumeTypes.Sphere;
		_boxRow.Visible = !mixed && type == SceneVolume.VolumeTypes.Box;
		_capsuleRow.Visible = !mixed && type == SceneVolume.VolumeTypes.Capsule;
	}
}

/// <summary>
/// Shows the selected deformation's name above its icon buttons without an extra inspector row.
/// </summary>
[CustomEditor( typeof( ModelDeformer.OperationType ), NamedEditor = "model-deformer-operation" )]
public sealed class ModelDeformerOperationControlWidget : ControlWidget
{
	private readonly Label.Header _heading;

	/// <inheritdoc />
	public override bool SupportsMultiEdit => true;

	/// <inheritdoc />
	public override bool IsWideMode => true;

	/// <inheritdoc />
	public override bool IncludeLabel => false;

	/// <summary>
	/// Reuses the standard enum buttons and adds a heading that follows selection and Undo.
	/// </summary>
	public ModelDeformerOperationControlWidget( SerializedProperty property ) : base( property )
	{
		PaintBackground = false;
		Layout = Layout.Column();
		_heading = Layout.Add( new Label.Header() );
		Layout.Add( new GroupButtonControlWidget( property ) );
		UpdateHeading();
	}

	protected override int ValueHash => HashCode.Combine( base.ValueHash, SerializedProperty.IsMultipleDifferentValues );

	protected override void OnValueChanged()
	{
		base.OnValueChanged();
		UpdateHeading();
	}

	private void UpdateHeading()
	{
		_heading.Text = SerializedProperty.IsMultipleDifferentValues ? "Multiple Values" : SerializedProperty.GetValue<ModelDeformer.OperationType>() switch
		{
			ModelDeformer.OperationType.Inflate => "Inflate - blow up like a balloon",
			ModelDeformer.OperationType.SquashStretch => "Squash - like stretching pizza dough",
			ModelDeformer.OperationType.Transform => "Transform - move however you want",
			ModelDeformer.OperationType.Suck => "Suck - like getting the vacuum too close",
			ModelDeformer.OperationType.Project => "Project - stretch to the shape",
			_ => "Choose an operation"
		};
	}
}
