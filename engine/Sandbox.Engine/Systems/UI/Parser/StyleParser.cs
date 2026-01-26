namespace Sandbox.UI;

internal static partial class StyleParser
{
	/// <summary>
	/// Get the appropriate property name from a potential alias (color -> font-color).
	/// This means we can define all our aliases in one place without any code re-use.
	/// </summary>
	internal static string GetPropertyFromAlias( string name )
	{
		switch ( name )
		{
			case "background-image-tint":
				return "background-tint";
			case "color":
				return "font-color";
			default:
				return name;
		}
	}

	/// <summary>
	/// Parse the styles as you would if they were passed in an style="width: 100px" attribute
	/// </summary>
	internal static void ParseStyles( ref Parse p, Styles style, bool parentheses = false, StyleSheet sheet = null )
	{
		if ( parentheses )
		{
			p = p.SkipWhitespaceAndNewlines();
			if ( !p.TrySkip( "{" ) )
				throw new Exception( $"Expected {{ {p.FileAndLine}" );
		}

		while ( !p.IsEnd )
		{
			p = p.SkipWhitespaceAndNewlines( ":;" );

			if ( p.Current == ':' )
				throw new System.Exception( "Parsing error - unexpected ':' at " );

			var name = p.ReadUntil( ":" );
			name = GetPropertyFromAlias( name );
			if ( name == null )
				break;

			p.Pointer++;

			p = p.SkipWhitespaceAndNewlines();

			var value = p.ReadUntilOrEnd( ";" );
			if ( value == null )
				break;

			p.Pointer++;

			// Replace SCSS variables if stylesheet is provided
			if ( sheet != null && value.IndexOf( '$' ) >= 0 )
			{
				try
				{
					value = sheet.ReplaceVariables( value );
				}
				catch ( System.Exception e )
				{
					throw new System.Exception( $"{e.Message} {p.FileAndLine}" );
				}
			}

			if ( !style.Set( name, value ) )
			{
				throw new Exception( $"Unknown Property: {name} / {value} {p.FileAndLine}" );
			}

			p = p.SkipWhitespaceAndNewlines();

			if ( parentheses && p.TrySkip( "}" ) )
				break;
		}
	}
}
