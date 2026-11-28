using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sandbox.Diagnostics;

internal static class ProfileCallbackScopes
{
	[SkipHotload] static readonly ConditionalWeakTable<Type, ConcurrentDictionary<string, ProfileScope>> Types = new();
	[SkipHotload] static readonly ConditionalWeakTable<MethodInfo, ProfileScope> Methods = new();
	[SkipHotload] static readonly ConditionalWeakTable<MethodInfo, ProfileScope> RpcMethods = new();

	// Call only from recording paths: weak keys let old addon types/methods unload after hotload.
	internal static ProfileScope ForType( Type type, string operation ) => Types.GetValue( type, static _ => new() )
		.GetOrAdd( operation, static ( name, owner ) => new( $"{owner.FullName ?? owner.Name}.{name}", "", 0 ), type );

	internal static ProfileScope ForMethod( MethodInfo method ) => Methods.GetValue( method, static m => new( MethodName( m ), "", 0 ) );

	/// <summary>
	/// A method's scope name. Lambdas and local functions read as the method they're written in:
	/// <c>McpProtocol+&lt;&gt;c__DisplayClass2_0.&lt;OnMainThreadAsync&gt;b__0()</c> becomes <c>McpProtocol.OnMainThreadAsync (lambda)</c>.
	/// </summary>
	internal static string MethodName( MethodInfo m )
	{
		var generated = System.Text.RegularExpressions.Regex.Match( m.Name, @"^<(?<outer>[^>]*)>(?<kind>[bg])__(?<local>[^|]*)" );
		if ( generated.Success && m.DeclaringType is { } type )
		{
			// Closures and the shared lambda cache are nested types whose names start with '<'
			while ( type.DeclaringType is not null && type.Name.StartsWith( '<' ) ) type = type.DeclaringType;
			var outer = generated.Groups["outer"].Value;
			var suffix = generated.Groups["kind"].Value == "g" ? $".{generated.Groups["local"].Value} (local function)" : " (lambda)";
			return $"{(type.FullName ?? type.Name).Replace( '+', '.' )}.{(outer.Length > 0 ? outer : "<field initializer>")}{suffix}";
		}

		var generics = m.IsGenericMethod ? $"<{string.Join( ",", m.GetGenericArguments().Select( x => x.FullName ?? x.Name ) )}>" : "";
		var parameters = string.Join( ",", m.GetParameters().Select( x => x.ParameterType.FullName ?? x.ParameterType.Name ) );
		return $"{m.DeclaringType?.FullName ?? "Callback"}.{m.Name}{generics}({parameters})";
	}

	internal static ProfileScope ForRpc( WrappedMethod method )
	{
		var resume = method.Resume.Method;
		if ( RpcMethods.TryGetValue( resume, out var scope ) ) return scope;
		return CreateRpc( resume, method.TypeName, method.MethodName );
	}

	static ProfileScope CreateRpc( MethodInfo resume, string type, string method )
	{
		var name = $"RPC.{type}.{method}";
		return RpcMethods.GetValue( resume, _ => new( name, "", 0 ) );
	}
}
