namespace Editor.AnimGraph;

/// <summary>
/// Base for the bone properties - reads like a dropdown, but opens a searchable skeleton tree
/// instead of a flat list, because skeletons are deep and long.
/// </summary>
public abstract class BonePickerControlWidget : ControlWidget
{
	private BonePicker _picker;

	public override bool IsControlButton => true;
	public override bool SupportsMultiEdit => true;
	public override bool IsControlActive => base.IsControlActive || _picker.IsValid();
	public override bool IsControlHovered => base.IsControlHovered || _picker.IsValid();

	protected BonePickerControlWidget( SerializedProperty property ) : base( property )
	{
		Cursor = CursorShape.Finger;
		MinimumWidth = Theme.RowHeight * 2;
	}

	/// <summary>
	/// Everything the edited property belongs to, innermost first. List entries hang off the list
	/// rather than whatever owns it, so this walks up until it runs out.
	/// </summary>
	protected IEnumerable<object> Owners
	{
		get
		{
			for ( var owner = SerializedProperty.Parent; owner is not null; owner = owner.ParentProperty?.Parent )
			{
				foreach ( var target in owner.Targets ?? [] )
					yield return target;
			}
		}
	}

	/// <summary>
	/// The graph the edited property belongs to - either the one that owns the node, or the one
	/// the parameter was declared on.
	/// </summary>
	protected virtual Data TargetGraph
	{
		get
		{
			var node = Owners.OfType<Node>().FirstOrDefault()
				?? Owners.OfType<VariationDataNode.VariationData>().FirstOrDefault()?.Owner;

			return node?.Root ?? Owners.OfType<Parameter>().FirstOrDefault()?.Graph;
		}
	}

	/// <summary>
	/// The model the graph, or the variation being edited, animates.
	/// </summary>
	protected virtual Model TargetModel => TargetGraph?.TargetModel;
	protected virtual Skeleton TargetSkeleton => Owners.OfType<SkeletonDocument>().FirstOrDefault()?.SourceSkeleton
		?? Owners.OfType<ClipDocument>().FirstOrDefault()?.SourceSkeleton;

	protected override void PaintControl()
	{
		var color = IsControlHovered ? Theme.Blue : Theme.TextControl;
		if ( IsControlDisabled ) color = color.WithAlpha( 0.5f );

		var rect = new Rect( 0, 0, Width, Theme.RowHeight ).Shrink( 8, 0 );
		var value = SerializedProperty.GetValue<string>();

		Paint.SetDefaultFont();

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			Paint.SetPen( Theme.MultipleValues );
			Paint.DrawText( rect, "Multiple Values", TextFlag.LeftCenter );
		}
		else
		{
			Paint.SetPen( string.IsNullOrEmpty( value ) ? color.WithAlpha( 0.5f ) : color );
			Paint.DrawText( rect, string.IsNullOrEmpty( value ) ? "None" : value, TextFlag.LeftCenter );
		}

		Paint.SetPen( color );
		Paint.DrawIcon( rect, "Arrow_Drop_Down", 17, TextFlag.RightCenter );
	}

	public override void StartEditing() => Open();

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( e.LeftMouseButton )
			Open();
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		// The click already opened the picker
	}

	/// <summary>
	/// How wide the popup opens - by default it matches the control, which is enough for the
	/// property sheet, but not for narrow toolbar controls.
	/// </summary>
	protected virtual float PickerWidth => Math.Max( ScreenRect.Width, 240 );

	private void Open()
	{
		if ( IsControlDisabled || _picker.IsValid() )
			return;

		var model = TargetModel;
		var skeleton = TargetSkeleton;
		if ( model is null && skeleton is null )
			return;

		PropertyStartEdit();

		_picker = model is not null
			? new BonePicker( model, SerializedProperty.GetValue<string>(), SerializedProperty.SetValue )
			: new BonePicker( BoneMaskWeights.GetBones( skeleton ), SerializedProperty.GetValue<string>(), SerializedProperty.SetValue );
		_picker.MinimumWidth = PickerWidth;
		_picker.OnLostFocus = PropertyFinishEdit;
		_picker.AdjustSize();
		_picker.OpenAt( ScreenRect.BottomLeft );
	}
}
