namespace Editor;

/// <summary>
/// Panel version of <see cref="FontControlWidget"/>: dropdown selection for font names.
/// </summary>
[CustomEditor( typeof( string ), WithAllAttributes = new[] { typeof( FontNameAttribute ) } )]
sealed class FontControl : DropdownControl<string>
{
	protected override IEnumerable<object> GetDropdownValues()
	{
		return EditorUtility.FontFamilies.Order().Cast<object>();
	}
}
