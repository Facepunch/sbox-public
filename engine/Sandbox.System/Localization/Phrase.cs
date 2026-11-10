namespace Sandbox.Localization;

/// <summary>
/// A translated string. ie "Hello World".
/// It might also have variables, ie "Hello {PlayerName}".
/// Todo support for conditionals and plurals
/// </summary>
public class Phrase
{
	internal string Value { get; set; }

	internal string[] Parts { get; set; }

	/// <summary>
	/// Create a SmartString from a phrase.
	/// </summary>
	public Phrase( string value )
	{
		if ( value == null )
			throw new ArgumentNullException();

		Value = value;
		Parts = null;

		if ( value.Contains( '{' ) && value.Contains( '}' ) )
		{
			List<string> parts = new List<string>();

			for ( int i = 0; i < Value.Length; i++ )
			{
				// find first '{'
				var idx = Value.IndexOf( '{', i );
				if ( idx < 0 )
				{
					parts.Add( Value[i..] );
					break;
				}

				if ( idx != i )
				{
					parts.Add( Value[i..idx] );
				}

				// find closer
				var cls = Value.IndexOf( '}', idx );
				if ( cls == -1 )
				{
					return;
				}

				var inner = Value.Substring( idx, cls - idx + 1 );
				parts.Add( inner );
				i = cls;
			}

			Parts = parts.ToArray();
		}
	}

	/// <summary>
	/// Render with no data - basically just returns Value
	/// </summary>
	public string Render()
	{
		return Value;
	}

	/// <summary>
	/// Render with variables
	/// </summary>
	public string Render( Dictionary<string, object> data )
	{
		if ( Parts == null || data == null )
			return Value;

		StringBuilder sb = new StringBuilder();

		for ( int i = 0; i < Parts.Length; i++ )
		{
			var part = Parts[i];

			//
			// If it's a not a variable just add it
			//
			if ( part[0] != '{' )
			{
				sb.Append( part );
				continue;
			}

			//
			// Strip off the { and } and look in the dictionary for the variable
			//
			var varName = part.Substring( 1, part.Length - 2 );
			if ( data.TryGetValue( varName, out var val ) )
			{
				sb.Append( val );
				continue;
			}

			//
			// If the variable wasn't found just print the {Variable} to embarass them into adding it
			//
			sb.Append( part );
		}

		return sb.ToString();
	}
}
