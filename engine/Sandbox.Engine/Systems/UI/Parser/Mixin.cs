using System.Text.RegularExpressions;

namespace Sandbox.UI;

/// <summary>
/// Represents a parsed @mixin definition that can be included elsewhere.
/// </summary>
public sealed partial class MixinDefinition
{
	/// <summary>
	/// The name of the mixin (e.g., "button" from "@mixin button")
	/// </summary>
	public string Name { get; init; }

	/// <summary>
	/// Parameter definitions in order, with optional default values.
	/// Key = parameter name (without $), Value = default value (null if required)
	/// </summary>
	public List<MixinParameter> Parameters { get; set; } = [];

	/// <summary>
	/// Whether this mixin has a variadic parameter (last param ends with ...)
	/// </summary>
	public bool HasVariadicParameter { get; set; }

	/// <summary>
	/// The raw content of the mixin body, to be expanded when included.
	/// This includes nested rules which will be parsed during expansion.
	/// </summary>
	public string Content { get; set; }

	/// <summary>
	/// Source file for error messages
	/// </summary>
	public string FileName { get; set; }

	/// <summary>
	/// Source line for error messages
	/// </summary>
	public int FileLine { get; set; }

	/// <summary>
	/// Expand this mixin with the given arguments, returning the CSS content
	/// with all parameters substituted.
	/// </summary>
	public string Expand( Dictionary<string, string> arguments, string contentBlock = null )
	{
		var result = Content;

		// Build final parameter values (arguments + defaults)
		var finalValues = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

		for ( int i = 0; i < Parameters.Count; i++ )
		{
			var param = Parameters[i];

			// Handle variadic parameter (collects remaining args)
			if ( param.IsVariadic )
			{
				if ( arguments.TryGetValue( param.Name, out var variadicValue ) )
				{
					finalValues[param.Name] = variadicValue;
				}
				else
				{
					finalValues[param.Name] = "";
				}
				continue;
			}

			if ( arguments.TryGetValue( param.Name, out var argValue ) )
			{
				finalValues[param.Name] = argValue;
			}
			else if ( param.DefaultValue != null )
			{
				finalValues[param.Name] = param.DefaultValue;
			}
			else
			{
				throw new Exception( $"Missing required parameter '${param.Name}' for mixin '{Name}'" );
			}
		}

		// Replace all parameter references in content
		// Sort by length descending so $bg-color doesn't get partially replaced by $bg
		foreach ( (string name, string value) in finalValues.OrderByDescending( x => x.Key.Length ) )
		{
			result = result.Replace( $"${name}", value );

			// Also handle variadic expansion with $name...
			if ( Parameters.Any( p => p.Name == name && p.IsVariadic ) )
			{
				result = result.Replace( $"${name}...", value );
			}
		}

		// Replace @content with the provided content block
		result = ReplaceContentDirective( result, contentBlock ?? "" );

		return result;
	}

	/// <summary>
	/// Replace @content directives, handling both standalone and with trailing semicolons
	/// </summary>
	private static string ReplaceContentDirective( string content, string replacement )
	{
		// Handle @content; (with semicolon)
		content = ReplaceContentSemicolonRegex().Replace( content, replacement );

		// Handle standalone @content (without a semicolon, e.g., at the end of a block)
		content = ReplaceContentRegex().Replace( content, replacement );

		return content;
	}

	[GeneratedRegex( @"@content\s*;", RegexOptions.IgnoreCase, "en-GB" )]
	private static partial Regex ReplaceContentSemicolonRegex();

	[GeneratedRegex( @"@content(?!\w)", RegexOptions.IgnoreCase, "en-GB" )]
	private static partial Regex ReplaceContentRegex();
}

/// <summary>
/// A single parameter in a mixin definition.
/// </summary>
public struct MixinParameter( string name, string defaultValue = null, bool isVariadic = false )
{
	/// <summary>
	/// Parameter name without the $ prefix (and without ... for variadic)
	/// </summary>
	public string Name = name;

	/// <summary>
	/// Default value, or null if the parameter is required
	/// </summary>
	public string DefaultValue = defaultValue;

	/// <summary>
	/// Whether this is a variadic parameter (collects remaining arguments)
	/// </summary>
	public bool IsVariadic = isVariadic;
}
