namespace Editor.MeshEditor;

[CustomEditor( typeof( BlockPrimitive.Side ), NamedEditor = "horizontal-box-sides" )]
internal sealed class BoxSidesControlWidget : ControlWidget
{
	readonly List<(Button Button, BlockPrimitive.Side Side)> _faces = [];

	public override bool IsWideMode => true;

	public BoxSidesControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Grid();
		FixedHeight = 170;
		PaintBackground = false;

		BuildUI();
	}

	void BuildUI()
	{
		Layout.Clear( true );
		_faces.Clear();

		var grid = Layout.Grid();
		grid.Spacing = 1;
		((GridLayout)Layout).AddCell( 0, 0, grid, alignment: TextFlag.Center );

		(BlockPrimitive.Side Side, int Column, int Row, string Axis)[] faces =
		[
			(BlockPrimitive.Side.Top, 2, 0, "+Z"),
			(BlockPrimitive.Side.Back, 0, 1, "-Y"),
			(BlockPrimitive.Side.Left, 1, 1, "-X"),
			(BlockPrimitive.Side.Front, 2, 1, "+Y"),
			(BlockPrimitive.Side.Right, 3, 1, "+X"),
			(BlockPrimitive.Side.Bottom, 2, 2, "-Z")
		];

		foreach ( var (side, column, row, axis) in faces )
		{
			var button = new Button
			{
				FixedSize = 45,
				ToolTip = $"{side} ({axis})",
				Clicked = () => Toggle( side ),
				OnPaintOverride = () => PaintFace( side )
			};

			_faces.Add( (button, side) );
			grid.AddCell( column, row, button );
		}

		UpdateFaces();
	}

	protected override void OnValueChanged() => UpdateFaces();

	void Toggle( BlockPrimitive.Side side )
	{
		var value = SerializedProperty.GetValue<BlockPrimitive.Side>();
		value ^= side;

		PropertyStartEdit();
		SerializedProperty.SetValue( value );
		PropertyFinishEdit();
		SignalValuesChanged();
	}

	void UpdateFaces()
	{
		foreach ( var (button, _) in _faces )
		{
			button.Enabled = !IsControlDisabled;
			button.Update();
		}
	}

	bool PaintFace( BlockPrimitive.Side side )
	{
		var selected = (SerializedProperty.GetValue<BlockPrimitive.Side>() & side) != 0;
		var disabled = IsControlDisabled;
		var fill = selected
			? Theme.Blue.WithAlpha( Paint.HasMouseOver && !disabled ? 1.0f : 0.8f )
			: Paint.HasMouseOver && !disabled ? Theme.ControlBackground.Lighten( 0.2f ) : Theme.ControlBackground;

		Paint.ClearPen();
		Paint.SetBrush( disabled ? fill.WithAlpha( 0.5f ) : fill );
		Paint.DrawRect( Paint.LocalRect, 2 );
		Paint.SetDefaultFont( 7, 500 );
		Paint.SetPen( selected
			? Color.White.WithAlpha( disabled ? 0.3f : 0.75f )
			: Theme.Text.WithAlpha( disabled ? 0.25f : 0.5f ) );
		Paint.DrawText( Paint.LocalRect, side.ToString(), TextFlag.Center );
		return true;
	}

}
