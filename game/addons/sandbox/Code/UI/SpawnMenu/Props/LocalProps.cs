/// <summary>
/// Hardcoded models that ship with the game and aren't available on the workshop.
/// Add new entries here — they'll appear automatically in the Local props section.
/// </summary>
public static class LocalProps
{
	/// <summary>
	/// A bundled model with a category identifier and optional localized title.
	/// </summary>
	public record Entry( string Path, string Category, string Title = null )
	{
		/// <summary>
		/// Derives a display name from the filename: strips the extension,
		/// replaces underscores with spaces, and title-cases each word.
		/// </summary>
		public string DisplayName
		{
			get
			{
				if ( !string.IsNullOrEmpty( Title ) ) return LocalizedText.Resolve( Title );

				var file = System.IO.Path.GetFileNameWithoutExtension( Path );
				// strip a second extension for compiled paths like .vmdl_c
				file = System.IO.Path.GetFileNameWithoutExtension( file );
				var words = file.Split( '_' );
				return string.Join( " ", words.Select( w => char.ToUpperInvariant( w[0] ) + w[1..] ) );
			}
		}
	}

	public static List<Entry> All => new()
	{
		// Humans
		new( "models/citizen_mannequin/mannequin.vmdl", "human", "#ui.prop.mannequin" ),
		new( "models/citizen_human/citizen_human_male.vmdl", "human", "#ui.prop.citizen_male" ),
		new( "models/citizen_human/citizen_human_female.vmdl", "human", "#ui.prop.citizen_female" ),
	};
}
