namespace Sandbox.UI;

internal static partial class StyleParser
{
	/// <summary>
	/// Parse a @mixin definition and add it to the <see cref="StyleSheet"/>.
	/// Returns true if we found and parsed a mixin, false otherwise.
	/// </summary>
	internal static bool ParseMixinDefinition( ref Parse p, StyleSheet sheet )
	{
		if ( !p.Is( "@mixin", 0, true ) )
			return false;

		p.Pointer += 6; // skip "@mixin"
		p = p.SkipWhitespaceAndNewlines();

		// Read mixin name
		var name = p.ReadUntilOrEnd( "({" ).Trim();
		if ( string.IsNullOrWhiteSpace( name ) )
			throw new Exception( $"Expected mixin name after @mixin {p.FileAndLine}" );

		var mixin = new MixinDefinition
		{
			Name = name,
			FileName = p.FileName,
			FileLine = p.CurrentLine
		};

		p = p.SkipWhitespaceAndNewlines();

		// Parse parameters if present
		if ( p.Current == '(' )
		{
			var paramsContent = p.ReadInnerBrackets();
			mixin.Parameters = ParseMixinParameters( paramsContent, p.FileAndLine );
			mixin.HasVariadicParameter = mixin.Parameters.Any( param => param.IsVariadic );
		}

		p = p.SkipWhitespaceAndNewlines();

		// Read the mixin body
		if ( p.Current != '{' )
			throw new Exception( $"Expected '{{' after mixin declaration {p.FileAndLine}" );

		mixin.Content = ReadBracedContent( ref p );

		sheet.SetMixin( mixin );
		return true;
	}

	/// <summary>
	/// Parse mixin parameter list like "$color, $padding: 8px, $args..."
	/// </summary>
	private static List<MixinParameter> ParseMixinParameters( string content, string fileAndLine )
	{
		var parameters = new List<MixinParameter>();

		if ( string.IsNullOrWhiteSpace( content ) )
			return parameters;

		var parts = SplitParameters( content );
		bool foundVariadic = false;

		foreach ( var part in parts )
		{
			var trimmed = part.Trim();
			if ( string.IsNullOrWhiteSpace( trimmed ) )
				continue;

			if ( foundVariadic )
				throw new Exception( $"Variadic parameter must be last: '{trimmed}' {fileAndLine}" );

			// Parameter format: $name, $name: default, or $name...
			if ( !trimmed.StartsWith( '$' ) )
				throw new Exception( $"Mixin parameter must start with $: '{trimmed}' {fileAndLine}" );

			// Check for variadic
			bool isVariadic = trimmed.EndsWith( "..." );
			if ( isVariadic )
			{
				trimmed = trimmed.Substring( 0, trimmed.Length - 3 );
				foundVariadic = true;
			}

			var colonIndex = trimmed.IndexOf( ':' );
			if ( colonIndex >= 0 )
			{
				var name = trimmed.Substring( 1, colonIndex - 1 ).Trim();
				var defaultValue = trimmed.Substring( colonIndex + 1 ).Trim();
				parameters.Add( new MixinParameter( name, defaultValue, isVariadic ) );
			}
			else
			{
				var name = trimmed.Substring( 1 ).Trim();
				parameters.Add( new MixinParameter( name, isVariadic ? "" : null, isVariadic ) );
			}
		}

		return parameters;
	}

	/// <summary>
	/// Parse @include directive arguments like "button(#3498db, 16px)" or "button($radius: 8px)".
	/// </summary>
	private static Dictionary<string, string> ParseIncludeDirective( string content, MixinDefinition mixin, string fileAndLine )
	{
		var p = new Parse( content );
		p = p.SkipWhitespaceAndNewlines();

		// Skip "@include"
		if ( p.Is( "@include", 0, true ) )
			p.Pointer += 8;

		p = p.SkipWhitespaceAndNewlines();

		// Read mixin name
		var name = p.ReadUntilOrEnd( "(;{" ).Trim();
		if ( string.IsNullOrWhiteSpace( name ) )
			throw new Exception( $"Expected mixin name after @include {fileAndLine}" );

		var arguments = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

		p = p.SkipWhitespaceAndNewlines();

		// Parse arguments if present
		if ( !p.IsEnd && p.Current == '(' )
		{
			var argsContent = p.ReadInnerBrackets();
			arguments = ParseIncludeArguments( argsContent, mixin, fileAndLine );
		}

		return arguments;
	}

	/// <summary>
	/// Parse include arguments, handling positional, named, and variadic arguments.
	/// </summary>
	private static Dictionary<string, string> ParseIncludeArguments( string content, MixinDefinition mixin, string fileAndLine )
	{
		var arguments = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

		if ( string.IsNullOrWhiteSpace( content ) )
			return arguments;

		var parts = SplitParameters( content );
		int positionalIndex = 0;
		bool namedStarted = false;
		var variadicValues = new List<string>();

		foreach ( var part in parts )
		{
			var trimmed = part.Trim();
			if ( string.IsNullOrWhiteSpace( trimmed ) )
				continue;

			// Check if this is a named argument ($param: value)
			if ( trimmed.StartsWith( '$' ) && trimmed.Contains( ':' ) )
			{
				namedStarted = true;
				var colonIndex = trimmed.IndexOf( ':' );
				var name = trimmed.Substring( 1, colonIndex - 1 ).Trim();
				var value = trimmed.Substring( colonIndex + 1 ).Trim();
				arguments[name] = value;
			}
			else
			{
				// Positional argument
				if ( namedStarted )
					throw new Exception( $"Positional argument cannot follow named argument {fileAndLine}" );

				// Check if we've reached the variadic parameter
				if ( positionalIndex >= mixin.Parameters.Count )
				{
					// Check if last param is variadic
					if ( mixin.HasVariadicParameter )
					{
						variadicValues.Add( trimmed );
						continue;
					}
					throw new Exception( $"Too many arguments for mixin '{mixin.Name}' {fileAndLine}" );
				}

				var param = mixin.Parameters[positionalIndex];

				if ( param.IsVariadic )
				{
					// Collect this and all remaining positional args
					variadicValues.Add( trimmed );
				}
				else
				{
					arguments[param.Name] = trimmed;
					positionalIndex++;
				}
			}
		}

		// Combine variadic values
		if ( variadicValues.Count > 0 && mixin.HasVariadicParameter )
		{
			var variadicParam = mixin.Parameters.Last( p => p.IsVariadic );
			arguments[variadicParam.Name] = string.Join( ", ", variadicValues );
		}

		return arguments;
	}

	/// <summary>
	/// Split the parameter / argument list by commas, respecting nested parentheses.
	/// </summary>
	private static List<string> SplitParameters( string content )
	{
		var result = new List<string>();
		var current = new System.Text.StringBuilder();
		int depth = 0;

		foreach ( var c in content )
		{
			if ( c == '(' )
			{
				depth++;
			}
			else if ( c == ')' )
			{
				depth--;

				if ( depth < 0 )
					throw new Exception( "Unbalanced parentheses in parameter list" );
			}
			else if ( c == ',' && depth == 0 )
			{
				result.Add( current.ToString() );
				current.Clear();
				continue;
			}

			current.Append( c );
		}

		if ( current.Length > 0 )
			result.Add( current.ToString() );

		return result;
	}

	/// <summary>
	/// Check if content starts with @include and expand it if so.
	/// Returns false if not an include directive.
	/// </summary>
	private static bool TryExpandInclude( string content, string contentBlock, StyleSheet sheet, string fileAndLine, out string mixinName, out string expandedContent )
	{
		mixinName = null;
		expandedContent = null;

		var trimmed = content.TrimStart();
		if ( !trimmed.StartsWith( "@include", StringComparison.OrdinalIgnoreCase ) )
			return false;

		// Parse the include directive to get mixin name
		var p = new Parse( trimmed );
		p.Pointer += 8; // skip "@include"
		p = p.SkipWhitespaceAndNewlines();

		mixinName = p.ReadUntilOrEnd( "(;{" ).Trim();

		if ( !sheet.TryGetMixin( mixinName, out var mixin ) )
			throw new Exception( $"Unknown mixin '{mixinName}' {fileAndLine}" );

		var arguments = ParseIncludeDirective( trimmed, mixin, fileAndLine );
		expandedContent = mixin.Expand( arguments, contentBlock );

		return true;
	}

	/// <summary>
	/// Overload for backward compatibility - no content block
	/// </summary>
	private static bool TryExpandInclude( string content, StyleSheet sheet, string fileAndLine, out string mixinName, out string expandedContent )
	{
		return TryExpandInclude( content, null, sheet, fileAndLine, out mixinName, out expandedContent );
	}
}

