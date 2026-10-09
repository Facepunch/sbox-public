namespace Editor;

[CustomEditor( typeof( float ), NamedEditor = "depth-of-field-focus-range" )]
public sealed class DepthOfFieldFocusRangeControlWidget : ControlWidget
{
	public override bool SupportsMultiEdit => false;

	public DepthOfFieldFocusRangeControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Column();
		Layout.Spacing = 2;
		Layout.Add( new FloatControlWidget( property ) );

		var button = Layout.Add( new Button( "Pick Focus", "center_focus_strong" ) );
		button.FixedHeight = Theme.RowHeight;
		button.HorizontalSizeMode = SizeMode.CanGrow;
		button.Clicked = PickFocus;
	}

	protected override void OnPaint()
	{
	}

	private void PickFocus()
	{
		var viewport = SceneViewWidget.Current?.LastSelectedViewportWidget;
		var focalDistance = SerializedProperty.Parent?.GetProperty( nameof( DepthOfField.FocalDistance ) );
		var session = SceneEditorSession.Resolve( SerializedProperty.GetContainingGameObject() );
		var target = focalDistance?.Parent?.Targets.OfType<DepthOfField>().FirstOrDefault();

		if ( !viewport.IsValid() || !target.IsValid() || viewport.SceneView.Session != session )
			return;

		var originalDistance = focalDistance.As.Float;
		ScenePositionPicker picker = null;

		float GetDistance( Vector3 position )
		{
			return picker.GetViewDepth( position ).Clamp( 1.0f, 16000.0f );
		}

		void SetDistance( Vector3 position )
		{
			if ( target.IsValid() )
				focalDistance.As.Float = GetDistance( position );
		}

		void Finish( Vector3 position )
		{
			if ( !target.IsValid() )
				return;

			var pickedDistance = GetDistance( position );
			focalDistance.As.Float = originalDistance;

			using ( session.UndoScope( "Pick Depth Of Field Focus" ).WithComponentChanges( target ).Push() )
			{
				focalDistance.As.Float = pickedDistance;
			}
		}

		void Cancel()
		{
			if ( target.IsValid() )
				focalDistance.As.Float = originalDistance;
		}

		picker = new ScenePositionPicker(
			viewport,
			position => $"{GetDistance( position ):0.##} units",
			SetDistance,
			Finish,
			Cancel
		);
	}
}
