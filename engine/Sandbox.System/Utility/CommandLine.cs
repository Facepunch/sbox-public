namespace Sandbox.Utility;

/// <summary>
/// Parses command line arguments into switches with optional values.
/// Supports both +switch and -switch syntax with optional values.
/// Example: -console +map "de_dust" -maxplayers 32
/// </summary>
internal static class CommandLine
{
	private static readonly Dictionary<string, string> switches = new( StringComparer.OrdinalIgnoreCase );
	private static string commandLine = "";

	/// <summary>
	/// Returns the full command line string.
	/// </summary>
	public static string Full => commandLine;

	/// <summary>
	/// Allows unit tests to override the command line string.
	/// If null, uses Environment.CommandLine.
	/// </summary>
	internal static string CommandLineString { get; set; }

	static CommandLine()
	{
		Parse();
	}

	/// <summary>
	/// Parses the command line into a dictionary of switches and values.
	/// Call this again if CommandLineString is modified.
	/// </summary>
	public static void Parse()
	{
		commandLine = CommandLineString ?? Environment.CommandLine;

		foreach ( var (key, value) in Parse( commandLine ) )
		{
			switches[key] = value;
		}
	}

	/// <summary>
	/// Parses a command line into a new dictionary of switches and values, without touching the
	/// process command line.
	/// </summary>
	internal static Dictionary<string, string> Parse( string commandLine )
	{
		var switches = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

		if ( string.IsNullOrEmpty( commandLine ) )
			return switches;

		var currentKey = "";
		var inQuotes = false;
		var quoteChar = '"';
		var currentValue = new StringBuilder();
		var isBuildingKey = false;

		for ( var i = 0; i < commandLine.Length; i++ )
		{
			var c = commandLine[i];

			// Handle quote toggling
			if ( c == quoteChar )
			{
				inQuotes = !inQuotes;
				currentValue.Append( c );
				continue;
			}

			// Check if we can start a new switch (must be at start or after whitespace)
			var canStartSwitch = i == 0 || char.IsWhiteSpace( commandLine[i - 1] );

			// New switch detected ('+' or '-')
			if ( canStartSwitch && !inQuotes && c is '+' or '-' )
			{
				// Save previous switch if it exists
				if ( !string.IsNullOrEmpty( currentKey ) )
				{
					switches[currentKey] = currentValue.ToString().Trim();
				}

				// Reset for new switch
				currentKey = "";
				currentValue.Clear();
				isBuildingKey = true;
				continue;
			}

			// Handle whitespace
			if ( !inQuotes && char.IsWhiteSpace( c ) )
			{
				// If we're still building the key, this ends it
				if ( isBuildingKey )
				{
					isBuildingKey = false;
					continue;
				}

				// Otherwise, preserve spaces in values
				currentValue.Append( ' ' );
				continue;
			}

			// Append character to either key or value
			if ( isBuildingKey )
				currentKey += c;
			else
				currentValue.Append( c );
		}

		// Save final switch
		if ( !string.IsNullOrEmpty( currentKey ) )
		{
			switches[currentKey] = currentValue.ToString().Trim();
		}

		return switches;
	}

	/// <summary>
	/// Checks if a command line switch is present.
	/// </summary>
	/// <param name="strName">Switch name (with or without + or - prefix)</param>
	/// <returns>True if the switch was specified on the command line</returns>
	/// <example>if ( HasSwitch( "-console" ) ) EnableConsole();</example>
	public static bool HasSwitch( string strName ) => switches.ContainsKey( strName.Trim( '+', '-' ) );

	/// <summary>
	/// Gets the value of a command line switch, or a default if not present.
	/// </summary>
	/// <param name="strName">Switch name (with or without + or - prefix)</param>
	/// <param name="strDefault">Default value if switch not found</param>
	/// <returns>The switch value or default</returns>
	/// <example>map = GetSwitch( "+map", "de_dust" );</example>
	public static string GetSwitch( string strName, string strDefault )
	{
		return switches.GetValueOrDefault( strName.Trim( '+', '-' ), strDefault );
	}

	/// <summary>
	/// Gets the integer value of a command line switch, or a default if not present or invalid.
	/// </summary>
	/// <param name="strName">Switch name (with or without + or - prefix)</param>
	/// <param name="iDefault">Default value if switch not found or not an integer</param>
	/// <returns>The parsed integer value or default</returns>
	/// <example>maxplayers = GetSwitchInt( "+maxplayers", 32 );</example>
	public static int GetSwitchInt( string strName, int iDefault )
	{
		if ( !switches.TryGetValue( strName.Trim( '+', '-' ), out var strValue ) )
			return iDefault;

		return int.TryParse( strValue, out var outval ) ? outval : iDefault;
	}

	/// <summary>
	/// Gets all parsed command line switches and their values.
	/// </summary>
	/// <returns>Dictionary of switch names to values (switch prefixes removed)</returns>
	public static Dictionary<string, string> GetSwitches()
	{
		return switches;
	}
}
