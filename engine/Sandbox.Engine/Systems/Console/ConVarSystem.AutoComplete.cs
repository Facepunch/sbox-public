namespace Sandbox;

internal static partial class ConVarSystem
{
	// [name=default] for optional params, <name:Type> for required.
	static string FormatParamHint( System.Reflection.ParameterInfo p )
		=> p.HasDefaultValue
			? $"[{p.Name}={p.DefaultValue}]"
			: $"<{p.Name}:{p.ParameterType.Name}>";

	// Space-separated hints for all params from fromIndex onward.
	static string BuildRemainingHint( System.Reflection.ParameterInfo[] parameters, int fromIndex )
	{
		if ( fromIndex >= parameters.Length ) return string.Empty;
		return string.Join( " ", parameters[fromIndex..].Select( FormatParamHint ) );
	}

	public static ConCmdAttribute.AutoCompleteResult[] GetAutoComplete( string partial, int count )
	{
		var parts = partial.SplitQuotesStrings();

		return partial.Contains( ' ' )
			? GetArgumentAutoComplete( partial, parts, count )
			: GetCommandAutoComplete( partial, parts, count );
	}

	// Completes argument values once a command name has been typed.
	static ConCmdAttribute.AutoCompleteResult[] GetArgumentAutoComplete( string partial, string[] parts, int count )
	{
		if ( !Members.TryGetValue( parts[0], out var command ) )
			return Array.Empty<ConCmdAttribute.AutoCompleteResult>();

		if ( command is not ManagedCommand managed )
			return Array.Empty<ConCmdAttribute.AutoCompleteResult>();

		// Connection is injected at call time, not supplied by the user.
		var paramOffset = managed.parameters.Length > 0 && managed.parameters[0].ParameterType == typeof( Connection ) ? 1 : 0;

		// Trailing space → user started a new arg. No trailing space → still mid-token.
		var startedNewArg = partial[^1] == ' ';
		var argIndex = startedNewArg ? parts.Length - 1 : parts.Length - 2;
		var partialArg = startedNewArg ? "" : parts[^1];
		var commandPrefix = startedNewArg ? partial.TrimEnd() : string.Join( " ", parts[..^1] );
		var paramIndex = paramOffset + argIndex;

		if ( paramIndex >= managed.parameters.Length )
			return Array.Empty<ConCmdAttribute.AutoCompleteResult>();

		var param = managed.parameters[paramIndex];
		var remainingHint = BuildRemainingHint( managed.parameters, paramIndex + 1 );

		IEnumerable<string> suggestions = param.ParameterType switch
		{
			var t when t == typeof( bool ) => ["true", "false"],
			var t when t.IsEnum => Enum.GetNames( t ),
			_ => null,
		};

		List<ConCmdAttribute.AutoCompleteResult> results = new();

		if ( suggestions is not null )
		{
			foreach ( var s in suggestions
				.Where( s => s.StartsWith( partialArg, StringComparison.OrdinalIgnoreCase ) )
				.Take( count ) )
			{
				var cmd = string.IsNullOrEmpty( remainingHint )
					? $"{commandPrefix} {s}"
					: $"{commandPrefix} {s} {remainingHint}";
				results.Add( new ConCmdAttribute.AutoCompleteResult
				{
					Command = cmd,
					Description = $"{param.Name} ({param.ParameterType.Name})",
				} );
			}
		}
		else
		{
			// Non-enum/bool: show a type hint so the user knows what to type.
			var currentHint = FormatParamHint( param );
			var fullCmd = string.IsNullOrEmpty( remainingHint )
				? $"{commandPrefix} {currentHint}"
				: $"{commandPrefix} {currentHint} {remainingHint}";
			results.Add( new ConCmdAttribute.AutoCompleteResult
			{
				Command = fullCmd,
				Description = $"{param.Name} ({param.ParameterType.Name})",
			} );
		}

		return results.ToArray();
	}

	// Completes command names, and for an exact match shows the full parameter signature.
	static ConCmdAttribute.AutoCompleteResult[] GetCommandAutoComplete( string partial, string[] parts, int count )
	{
		List<ConCmdAttribute.AutoCompleteResult> results = new();

		foreach ( var option in Members.Values
										.Where( x => !x.IsHidden )
										.Where( x => x.Name.StartsWith( partial, StringComparison.OrdinalIgnoreCase ) )
										.OrderBy( x => x.Name ) )
		{
			if ( string.Equals( option.Name, partial, StringComparison.OrdinalIgnoreCase )
				&& option is ManagedCommand exactManaged )
			{
				var paramOffset = exactManaged.parameters.Length > 0 && exactManaged.parameters[0].ParameterType == typeof( Connection ) ? 1 : 0;
				var hint = BuildRemainingHint( exactManaged.parameters, paramOffset );
				if ( !string.IsNullOrEmpty( hint ) )
				{
					results.Add( new ConCmdAttribute.AutoCompleteResult
					{
						Command = $"{option.Name} {hint}",
						Description = option.BuildDescription(),
					} );
				}
				continue;
			}

			results.Add( new ConCmdAttribute.AutoCompleteResult
			{
				Command = option.Name,
				Description = option.BuildDescription(),
			} );
		}

		return results.Take( count ).ToArray();
	}
}
