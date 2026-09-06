using System.Globalization;

namespace Sandbox.Layout;

/// <summary>
/// Parses the CSS grid property value syntax: track lists (<c>grid-template-columns</c>), track sizes
/// (<c>grid-auto-rows</c>), placements (<c>grid-column-start</c>) and <c>grid-auto-flow</c>.
/// Lengths accept <c>px</c>, <c>%</c>, <c>fr</c> and unitless numbers (pixels); any other unit is handed to
/// an optional resolver so callers can support em/rem/vw and friends.
/// </summary>
internal static class GridParser
{
	/// <summary>Resolves a value with an unknown unit to pixels, or returns null if it can't.</summary>
	public delegate float? UnitResolver( float value, string unit );

	public static bool TryParseTrackList( string text, out TrackList result, UnitResolver unitResolver = null )
	{
		result = TrackList.None;
		if ( string.IsNullOrWhiteSpace( text ) )
		{
			return false;
		}

		text = text.Trim();
		if ( text.Equals( "none", StringComparison.OrdinalIgnoreCase ) )
		{
			return true;
		}

		var tokens = Tokenize( text );
		if ( tokens is null )
		{
			return false;
		}

		var components = new List<TrackTemplateComponent>();
		var pendingNames = new List<string>();
		int i = 0;

		while ( i < tokens.Count )
		{
			var token = tokens[i];

			if ( token.Kind == TokenKind.Names )
			{
				pendingNames.AddRange( token.Names );
				i++;
				continue;
			}

			if ( token.Kind == TokenKind.Function
				&& token.Text.Equals( "repeat", StringComparison.OrdinalIgnoreCase ) )
			{
				if ( !TryParseRepeat( token, pendingNames.ToArray(), out var repeat, unitResolver ) )
				{
					return false;
				}

				pendingNames.Clear();
				components.Add( repeat );
				i++;
				continue;
			}

			if ( !TryParseTrackSize( token, out var size, unitResolver ) )
			{
				return false;
			}

			components.Add( new TrackTemplateComponent( size, pendingNames.ToArray(), null ) );
			pendingNames.Clear();
			i++;
		}

		if ( components.Count == 0 )
		{
			return false;
		}

		if ( pendingNames.Count > 0 )
		{
			// Trailing line names attach to the last track.
			var last = components[^1];
			var trailing = (string[][])last.TrailingNameItems.Clone();
			var merged = new List<string>( trailing[^1] );
			merged.AddRange( pendingNames );
			trailing[^1] = merged.ToArray();
			components[^1] = last.IsRepeat
				? new TrackTemplateComponent( last.Repetition, last.RepeatCount, last.TrackItems, last.LeadingNameItems, trailing, last.RepeatLeadingNameItems )
				: new TrackTemplateComponent( last.TrackItems[0], last.LeadingNameItems, trailing[0] );
		}

		result = new TrackList( components.ToArray() );
		return true;
	}

	/// <summary>Parses a space separated list of track sizes (<c>grid-auto-columns</c> / <c>grid-auto-rows</c>).</summary>
	public static bool TryParseTrackSizes( string text, out TrackSizingFunction[] result, UnitResolver unitResolver = null )
	{
		result = [];
		if ( string.IsNullOrWhiteSpace( text ) )
		{
			return false;
		}

		text = text.Trim();
		if ( text.Equals( "auto", StringComparison.OrdinalIgnoreCase ) )
		{
			return true;
		}

		var tokens = Tokenize( text );
		if ( tokens is null or { Count: 0 } )
		{
			return false;
		}

		var sizes = new TrackSizingFunction[tokens.Count];
		for ( int i = 0; i < tokens.Count; i++ )
		{
			if ( !TryParseTrackSize( tokens[i], out sizes[i], unitResolver ) )
			{
				return false;
			}
		}

		result = sizes;
		return true;
	}

	/// <summary>Parses one <c>grid-row-start</c> / <c>grid-column-end</c> style value.</summary>
	public static bool TryParsePlacement( string text, out GridPlacement result )
	{
		result = GridPlacement.Auto;
		if ( string.IsNullOrWhiteSpace( text ) )
		{
			return false;
		}

		if ( text.AsSpan().Trim().Equals( "auto", StringComparison.OrdinalIgnoreCase ) )
		{
			return true;
		}

		var parts = text.Trim().Split( ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
		if ( parts.Length is 0 or > 3 )
		{
			return false;
		}

		var isSpan = parts[0].Equals( "span", StringComparison.OrdinalIgnoreCase );
		var rest = isSpan ? parts.AsSpan( 1 ) : parts.AsSpan();
		if ( isSpan && rest.Length == 0 )
		{
			return false;
		}

		int? number = null;
		string name = null;
		foreach ( var part in rest )
		{
			if ( int.TryParse( part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n ) )
			{
				if ( number.HasValue )
				{
					return false;
				}

				number = n;
			}
			else if ( IsIdentifier( part ) )
			{
				if ( name is not null )
				{
					return false;
				}

				name = part;
			}
			else
			{
				return false;
			}
		}

		if ( isSpan )
		{
			if ( number is <= 0 )
			{
				return false;
			}

			result = name is null ? GridPlacement.Span( number ?? 1 ) : GridPlacement.NamedSpan( name, number ?? 1 );
			return true;
		}

		if ( name is null )
		{
			if ( !number.HasValue || number == 0 )
			{
				return false;
			}

			result = GridPlacement.Line( number.Value );
			return true;
		}

		if ( number == 0 )
		{
			return false;
		}

		result = GridPlacement.NamedLine( name, number ?? 1 );
		return true;
	}

	/// <summary>
	/// Parses the <c>grid-row</c> / <c>grid-column</c> shorthand: <c>start [/ end]</c>. A single value sets
	/// only the start; the end becomes <c>auto</c> unless the start is a lone named line, in which case the
	/// end takes the same name (css-grid-1 §8.4).
	/// </summary>
	public static bool TryParsePlacementShorthand( string text, out GridPlacement start, out GridPlacement end )
	{
		start = end = GridPlacement.Auto;
		if ( string.IsNullOrWhiteSpace( text ) )
		{
			return false;
		}

		var parts = text.Split( '/' );
		if ( parts.Length > 2 )
		{
			return false;
		}

		if ( !TryParsePlacement( parts[0], out start ) )
		{
			return false;
		}

		if ( parts.Length == 2 )
		{
			return TryParsePlacement( parts[1], out end );
		}

		end = start.Kind == GridPlacementKind.NamedLine && start.Value == 1 ? start : GridPlacement.Auto;
		return true;
	}

	public static bool TryParseAutoFlow( string text, out GridAutoFlow result )
	{
		result = GridAutoFlow.Row;
		if ( string.IsNullOrWhiteSpace( text ) )
		{
			return false;
		}

		var row = false;
		var column = false;
		var dense = false;
		foreach ( var part in text.Trim().Split( ' ', StringSplitOptions.RemoveEmptyEntries ) )
		{
			if ( part.Equals( "row", StringComparison.OrdinalIgnoreCase ) )
			{
				row = true;
			}
			else if ( part.Equals( "column", StringComparison.OrdinalIgnoreCase ) )
			{
				column = true;
			}
			else if ( part.Equals( "dense", StringComparison.OrdinalIgnoreCase ) )
			{
				dense = true;
			}
			else
			{
				return false;
			}
		}

		if ( row && column )
		{
			return false;
		}

		result = column ? (dense ? GridAutoFlow.ColumnDense : GridAutoFlow.Column) : (dense ? GridAutoFlow.RowDense : GridAutoFlow.Row);
		return true;
	}

	// -----------------------------------------------------------------------------------------------

	private static bool TryParseRepeat(
		Token token,
		string[] leadingNames,
		out TrackTemplateComponent result,
		UnitResolver unitResolver )
	{
		result = null;
		var arguments = token.Arguments;
		if ( arguments.Count != 2 )
		{
			return false;
		}

		var countText = arguments[0].Trim();
		RepetitionKind repetition;
		int count = 1;
		if ( countText.Equals( "auto-fill", StringComparison.OrdinalIgnoreCase ) )
		{
			repetition = RepetitionKind.AutoFill;
		}
		else if ( countText.Equals( "auto-fit", StringComparison.OrdinalIgnoreCase ) )
		{
			repetition = RepetitionKind.AutoFit;
		}
		else if ( int.TryParse( countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out count ) && count > 0 )
		{
			repetition = RepetitionKind.Count;
		}
		else
		{
			return false;
		}

		var inner = Tokenize( arguments[1] );
		if ( inner is null )
		{
			return false;
		}

		var tracks = new List<TrackSizingFunction>();
		var trailing = new List<string[]>();
		var pending = new List<string>();
		string[] innerLeading = null;

		foreach ( var tokenPart in inner )
		{
			if ( tokenPart.Kind == TokenKind.Names )
			{
				pending.AddRange( tokenPart.Names );
				continue;
			}

			if ( !TryParseTrackSize( tokenPart, out var size, unitResolver ) )
			{
				return false;
			}

			if ( tracks.Count == 0 )
			{
				innerLeading = pending.ToArray();
			}
			else
			{
				trailing[^1] = pending.ToArray();
			}
			pending.Clear();

			tracks.Add( size );
			trailing.Add( [] );
		}

		if ( tracks.Count == 0 )
		{
			return false;
		}

		if ( pending.Count > 0 )
		{
			trailing[^1] = pending.ToArray();
		}

		// Auto repeats may only contain fixed track sizes.
		if ( repetition != RepetitionKind.Count )
		{
			foreach ( var track in tracks )
			{
				if ( !track.IsFixed
					&& !(track.Min.IsFixed && track.Max.IsFraction)
					&& !(track.Min.IsFixed && !track.Max.IsFixed)
					&& !(!track.Min.IsFixed && track.Max.IsFixed) )
				{
					return false;
				}
			}
		}

		result = new TrackTemplateComponent(
			repetition,
			count,
			tracks.ToArray(),
			leadingNames,
			trailing.ToArray(),
			innerLeading );
		return true;
	}

	private static bool TryParseTrackSize( Token token, out TrackSizingFunction result, UnitResolver unitResolver )
	{
		result = TrackSizingFunction.Auto;

		if ( token.Kind == TokenKind.Function )
		{
			if ( token.Text.Equals( "minmax", StringComparison.OrdinalIgnoreCase ) )
			{
				if ( token.Arguments.Count != 2 )
				{
					return false;
				}

				if ( !TryParseBreadth( token.Arguments[0], out var min, unitResolver )
					|| !TryParseBreadth( token.Arguments[1], out var max, unitResolver ) )
				{
					return false;
				}

				if ( min.IsFraction )
				{
					return false;
				}

				result = TrackSizingFunction.MinMax( min, max );
				return true;
			}

			if ( token.Text.Equals( "fit-content", StringComparison.OrdinalIgnoreCase ) )
			{
				if ( token.Arguments.Count != 1 )
				{
					return false;
				}

				if ( !TryParseLength( token.Arguments[0], out var limit, unitResolver ) )
				{
					return false;
				}

				result = TrackSizingFunction.FitContent( limit );
				return true;
			}

			return false;
		}

		if ( token.Kind != TokenKind.Word )
		{
			return false;
		}

		if ( !TryParseBreadth( token.Text, out var breadth, unitResolver ) )
		{
			return false;
		}

		result = breadth.IsFraction ? TrackSizingFunction.Fraction( breadth.Value ) : TrackSizingFunction.Single( breadth );
		return true;
	}

	private static bool TryParseBreadth( string text, out TrackBreadth result, UnitResolver unitResolver )
	{
		result = TrackBreadth.Auto;
		text = text.Trim();

		if ( text.Equals( "auto", StringComparison.OrdinalIgnoreCase ) )
		{
			return true;
		}

		if ( text.Equals( "min-content", StringComparison.OrdinalIgnoreCase ) )
		{
			result = TrackBreadth.MinContent;
			return true;
		}

		if ( text.Equals( "max-content", StringComparison.OrdinalIgnoreCase ) )
		{
			result = TrackBreadth.MaxContent;
			return true;
		}

		if ( text.EndsWith( "fr", StringComparison.OrdinalIgnoreCase )
			&& float.TryParse( text.AsSpan()[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction ) )
		{
			if ( fraction < 0 )
			{
				return false;
			}

			result = TrackBreadth.Fraction( fraction );
			return true;
		}

		if ( !TryParseLength( text, out var length, unitResolver ) || length.IsAuto )
		{
			return false;
		}

		result = length.IsPercent ? TrackBreadth.Percent( length.Value ) : TrackBreadth.Points( length.Value );
		return true;
	}

	/// <summary>Parses <c>12px</c>, <c>50%</c>, <c>12</c>, or another unit via the resolver.</summary>
	internal static bool TryParseLength( string text, out StyleLength result, UnitResolver unitResolver )
	{
		result = StyleLength.Undefined;
		text = text.Trim();
		if ( text.Length == 0 )
		{
			return false;
		}

		if ( text.Equals( "auto", StringComparison.OrdinalIgnoreCase ) )
		{
			result = StyleLength.Auto;
			return true;
		}

		int split = text.Length;
		while ( split > 0 && !(char.IsDigit( text[split - 1] ) || text[split - 1] == '.') )
		{
			split--;
		}

		if ( split == 0 )
		{
			return false;
		}

		if ( !float.TryParse( text.AsSpan()[..split], NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) )
		{
			return false;
		}

		var unit = text[split..];
		switch ( unit.ToLowerInvariant() )
		{
			case "":
			case "px":
				result = StyleLength.Points( value );
				return true;
			case "%":
				result = StyleLength.Percent( value );
				return true;
			default:
				var resolved = unitResolver?.Invoke( value, unit );
				if ( !resolved.HasValue )
				{
					return false;
				}

				result = StyleLength.Points( resolved.Value );
				return true;
		}
	}

	private static bool IsIdentifier( string value )
	{
		if ( value.Length == 0 || char.IsDigit( value[0] ) )
		{
			return false;
		}

		if ( value.Equals( "span", StringComparison.OrdinalIgnoreCase )
			|| value.Equals( "auto", StringComparison.OrdinalIgnoreCase ) )
		{
			return false;
		}

		foreach ( var character in value )
		{
			if ( !(char.IsLetterOrDigit( character ) || character is '-' or '_') )
			{
				return false;
			}
		}

		return true;
	}

	// -----------------------------------------------------------------------------------------------
	// Tokenizer: words, [line names], function(args, ...)
	// -----------------------------------------------------------------------------------------------

	private enum TokenKind { Word, Names, Function }

	private sealed class Token
	{
		public TokenKind Kind;
		public string Text;
		public List<string> Names;
		public List<string> Arguments;
	}

	private static List<Token> Tokenize( string text )
	{
		var tokens = new List<Token>();
		int i = 0;

		while ( i < text.Length )
		{
			var character = text[i];
			if ( char.IsWhiteSpace( character ) )
			{
				i++;
				continue;
			}

			if ( character == '[' )
			{
				var close = text.IndexOf( ']', i );
				if ( close < 0 )
				{
					return null;
				}

				var names = text[(i + 1)..close]
					.Split( ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
				foreach ( var name in names )
				{
					if ( !IsIdentifier( name ) )
					{
						return null;
					}
				}

				tokens.Add( new Token { Kind = TokenKind.Names, Names = new List<string>( names ) } );
				i = close + 1;
				continue;
			}

			int start = i;
			while ( i < text.Length
				&& !char.IsWhiteSpace( text[i] )
				&& text[i] != '('
				&& text[i] != '['
				&& text[i] != ']' )
			{
				i++;
			}

			var word = text[start..i];

			if ( i < text.Length && text[i] == '(' )
			{
				// Function: find the matching close paren, split arguments on top-level commas.
				int depth = 0;
				int argStart = i + 1;
				var arguments = new List<string>();
				int j = i;
				for ( ; j < text.Length; j++ )
				{
					if ( text[j] == '(' )
					{
						depth++;
					}
					else if ( text[j] == ')' )
					{
						depth--;
						if ( depth == 0 )
						{
							break;
						}
					}
					else if ( text[j] == ',' && depth == 1 )
					{
						arguments.Add( text[argStart..j] );
						argStart = j + 1;
					}
				}
				if ( j >= text.Length )
				{
					return null;
				}

				arguments.Add( text[argStart..j] );
				tokens.Add( new Token { Kind = TokenKind.Function, Text = word, Arguments = arguments } );
				i = j + 1;
				continue;
			}

			if ( word.Length == 0 )
			{
				return null;
			}
			tokens.Add( new Token { Kind = TokenKind.Word, Text = word } );
		}

		return tokens;
	}
}
