namespace Sandbox.PanelGallery;

public partial class TextEntryPage
{
	readonly List<(Sandbox.UI.TextEntry Entry, Sandbox.UI.Label Status)> _fields = [];

	void AddAutocompleteExamples()
	{
		string[] names = ["Amber", "Amethyst", "Azure", "Coral", "Crimson", "Emerald", "Indigo", "Jade", "Ruby", "Sapphire"];
		var entry = new Sandbox.UI.TextEntry { Placeholder = "Try am, co or sa…" };
		entry.AutoComplete = query => names.Where( name => name.Contains( query, StringComparison.OrdinalIgnoreCase ) ).Cast<object>().ToArray();
		Case( "Autocomplete: string suggestions" ).AddChild( entry );
		var commands = new[] { ("Open scene", "folder_open"), ("Save scene", "save"), ("Play scene", "play_arrow"), ("Create object", "add_box") };
		var command = new Sandbox.UI.TextEntry { Placeholder = "Search commands…" };
		command.AutoComplete = query => commands.Where( item => item.Item1.Contains( query, StringComparison.OrdinalIgnoreCase ) )
			.Select( item => (object)new Sandbox.UI.TextEntry.AutocompleteEntry { Title = item.Item1, Icon = item.Item2, Value = item.Item1 } ).ToArray();
		Case( "Autocomplete: suggestions with icons" ).AddChild( command );
	}

	void AddValidationExamples()
	{
		Field( "Length: 3–12 characters", new() { Text = "ab", MinLength = 3, MaxLength = 12 } );
		Field( "Letters and spaces only", new() { Text = "Hello", CharacterRegex = "^[a-zA-Z ]$" } );
		Field( "Asset name: lowercase letters, digits and underscores", new() { Text = "Invalid Name", StringRegex = "^[a-z][a-z0-9_]*$" } );
		Field( "Whole number between 0 and 100", new() { Text = "42", Numeric = true, WholeNumbers = true, MinValue = 0, MaxValue = 100 } );
	}

	void Field( string title, Sandbox.UI.TextEntry entry )
	{
		var row = Case( $"Validation: {title}" );
		row.AddChild( entry );
		_fields.Add( (entry, row.Add.Label( "", "validation-result" )) );
	}

	public override void Tick()
	{
		base.Tick();
		foreach ( var (entry, status) in _fields )
		{
			entry.UpdateValidation();
			status.Text = entry.HasValidationErrors ? "Invalid" : "Valid";
			status.SetClass( "invalid", entry.HasValidationErrors );
		}
	}
}
