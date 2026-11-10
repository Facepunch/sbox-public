namespace Editor;
/// <summary>
/// A dropdown control widget for selecting an asset license.
/// Delegates value storage to SerializedProperty so that hashing, multi-edit, and undo/redo work correctly.
/// </summary>
sealed class LicenseControlWidget : DropdownControlWidget<string>
{
	IReadOnlyList<(string Name, string Title, string Description)> LicenseOptions { get; set; }
		= Array.Empty<(string, string, string)>();

	public LicenseControlWidget( SerializedProperty property ) : base( property )
	{
	}

	public void SetLicenseOptions( IReadOnlyList<(string Name, string Title, string Description)> options )
	{
		LicenseOptions = options;
		Update();
	}

	protected override string GetDisplayText()
	{
		var value = SerializedProperty.GetValue<string>();
		if ( string.IsNullOrEmpty( value ) )
			return "None";

		var match = LicenseOptions.FirstOrDefault( o => o.Name == value );
		if ( !string.IsNullOrEmpty( match.Title ) )
			return match.Title;

		return value;
	}

	protected override IEnumerable<object> GetDropdownValues()
	{
		yield return new Entry { Value = null, Label = "None" };

		foreach ( var option in LicenseOptions )
		{
			yield return new Entry
			{
				Value = option.Name,
				Label = option.Title,
				Description = option.Description
			};
		}
	}
}
