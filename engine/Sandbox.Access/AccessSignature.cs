using System.Linq;
using Mono.Cecil;

namespace Sandbox;

/// <summary>
/// Produces the exact "touch name" string that access control matches whitelist/blacklist patterns
/// against. Kept in one place so tooling (the whitelist generator) emits strings that are guaranteed
/// to match what the scanner produces - see <see cref="AssemblyAccess"/>.
/// </summary>
public static class AccessSignature
{
	/// <summary>
	/// Signature for a type, e.g. <c>System.Private.CoreLib/System.Memory`1</c>.
	/// </summary>
	public static string Type( TypeDefinition type )
	{
		return $"{type.Module.Assembly.Name.Name}/{type.FullName}";
	}

	/// <summary>
	/// Signature for a method, e.g. <c>System.Private.CoreLib/System.Memory`1.Slice( System.Int32 )</c>.
	/// Constructors, operators and property accessors (<c>get_</c>/<c>set_</c>) are all methods.
	/// </summary>
	public static string Method( MethodDefinition method )
	{
		var name = $"{method.Module.Assembly.Name.Name}/{method.DeclaringType.FullName}.{method.Name}";

		if ( method.HasGenericParameters )
		{
			var gparms = string.Join( ",", method.GenericParameters.Select( x => x.Name.ToString() ) );
			if ( !string.IsNullOrWhiteSpace( gparms ) ) name += $"<{gparms}>";
		}

		if ( method.HasParameters )
		{
			var parms = string.Join( ", ", method.Parameters.Select( x => x.ParameterType.ToString() ) );
			if ( !string.IsNullOrWhiteSpace( parms ) ) parms = $" {parms} ";
			name += $"({parms})";
		}
		else
		{
			name += "()";
		}

		return name;
	}
}
