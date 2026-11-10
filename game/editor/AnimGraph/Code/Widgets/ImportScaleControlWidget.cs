namespace Editor.AnimGraph;

/// <summary>
/// An import scale, as a unit to pick from and the number it works out to. Picking a unit fills in
/// the number, and a number that isn't one of them reads as Custom.
/// </summary>
[CustomEditor( typeof( float ), NamedEditor = "importscale" )]
sealed class ImportScaleControlWidget : ControlWidget
{
	public ImportScaleControlWidget( SerializedProperty property ) : base( property )
	{
		// Both halves paint their own control background, so there's nothing of ours behind them
		PaintBackground = false;

		Layout = Layout.Row();
		Layout.Spacing = 2;

		Layout.Add( new UnitsControlWidget( property ) { MinimumWidth = Theme.RowHeight }, 1 );
		Layout.Add( new FloatControlWidget( property ) { MinimumWidth = Theme.RowHeight }, 1 );
	}

	/// <summary>
	/// The unit the scale works out to, or Custom for a number that isn't one of them.
	/// </summary>
	private sealed class UnitsControlWidget : DropdownControlWidget<float>
	{
		private static readonly (string Name, float Scale)[] Units =
		[
			("Inches", 1.0f),
			("Feet", 12.0f),
			("Meters", 39.37f),
			("Centimeters", 0.3937f),
			("Millimeters", 0.03937f),
		];

		public UnitsControlWidget( SerializedProperty property ) : base( property )
		{
		}

		protected override IEnumerable<object> GetDropdownValues()
			=> Units.Select( unit => new Entry { Value = unit.Scale, Label = unit.Name } ).Cast<object>();

		protected override string GetDisplayText()
		{
			var scale = SerializedProperty.GetValue<float>();

			return Array.Find( Units, unit => scale.AlmostEqual( unit.Scale ) ).Name ?? "Custom";
		}
	}
}
