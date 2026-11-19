using Sandbox.Internal;
using System.Reflection;

namespace Sandbox;

internal sealed partial class TypeLibraryScriptResolver
{
	// Generated wrappers use these contracts without exposing unrelated helper APIs.
	private static readonly Type[] GeneratedTypes =
	[
		typeof( SyncAttribute ), typeof( Rpc.BroadcastAttribute ), typeof( Rpc.HostAttribute ), typeof( Rpc.OwnerAttribute ),
		typeof( WrappedMethod ), typeof( WrappedPropertyGet<> ), typeof( WrappedPropertySet<> ),
		typeof( ChangeCallback ), typeof( ConsoleSystem ), typeof( System.Buffers.ArrayPool<> ),
		typeof( SourceLocationAttribute ), typeof( DescriptionAttribute ), typeof( SkipHotloadAttribute ), typeof( Attribute ),
		typeof( IUpdateSubscriber ), typeof( IFixedUpdateSubscriber ), typeof( IPreRenderSubscriber )
	];

	internal static bool IsNetworkingAttribute( Type type ) => type == typeof( SyncAttribute )
		|| type == typeof( Rpc.BroadcastAttribute ) || type == typeof( Rpc.HostAttribute ) || type == typeof( Rpc.OwnerAttribute );

	internal static bool IsNetworkingAttributeName( string name ) => GeneratedTypes.Where( IsNetworkingAttribute )
		.Any( type => MatchesGeneratedType( type, name ) || MatchesGeneratedType( type, name + "Attribute" ) );

	private static bool MatchesGeneratedType( Type type, string name )
	{
		var normalized = name.Replace( "global::", "", StringComparison.Ordinal ).Replace( '+', '.' );
		var fullName = type.FullName.Replace( '+', '.' );
		return normalized == fullName || (!type.IsNested && normalized == type.Name)
			|| (type.Namespace == "Sandbox" && normalized == fullName["Sandbox.".Length..]);
	}

	private static Type ResolveGeneratedType( string name ) => GeneratedTypes.FirstOrDefault( type => MatchesGeneratedType( type, name ) );

	/// <summary>
	/// Supplies generated wrapper payloads and metadata without exposing general reflection APIs.
	/// </summary>
	private static void GetGeneratedMembers( Type type, List<MemberInfo> members )
	{
		if ( type == typeof( Rpc ) )
		{
			members.AddRange( type.GetMethods( BindingFlags.Public | BindingFlags.Static ).Where( method => method.Name == "OnCallRpc" ) );
		}
		if ( type == typeof( ChangeCallback ) )
		{
			members.AddRange( type.GetMethods( BindingFlags.Public | BindingFlags.Static ).Where( method => method.Name == "OnPropertySet" ) );
		}
		if ( type == typeof( ConsoleSystem ) )
		{
			members.AddRange( type.GetMethods( BindingFlags.Public | BindingFlags.Static ).Where( method => method.Name is "OnWrappedGet" or "OnWrappedSet" ) );
		}

		// The safe shared pool returns ArrayPool<T>, whose concrete implementation is private.
		// Allow its buffer operations without exposing the BCL shared pool or pool factories.
		for ( var contract = type; contract is not null; contract = contract.BaseType )
		{
			if ( !contract.IsGenericType || contract.GetGenericTypeDefinition() != typeof( System.Buffers.ArrayPool<> ) ) continue;
			members.AddRange( contract.GetMethods( BindingFlags.Public | BindingFlags.Instance ).Where( method => method.Name is "Rent" or "Return" ) );
			break;
		}
		var definition = type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type;
		if ( !GeneratedTypes.Contains( definition ) ) return;
		members.AddRange( type.GetProperties( BindingFlags.Public | BindingFlags.Instance ).Where( property => property.DeclaringType != typeof( Attribute ) ) );
		members.AddRange( type.GetFields( BindingFlags.Public | BindingFlags.Instance ) );
		if ( typeof( Attribute ).IsAssignableFrom( type ) && type != typeof( Attribute ) )
		{
			members.AddRange( type.GetConstructors() );
		}
	}

	private static IEnumerable<MethodInfo> GetGeneratedBaseMethods( Type type ) => type.GetMethods( BindingFlags.NonPublic | BindingFlags.Instance )
		.Where( method => method.IsFamily && method.Name is "__sync_SetValue" or "__sync_GetValue" or "__rpc_Wrapper" )
		.Where( method => method.DeclaringType == typeof( Component ) || method.DeclaringType == typeof( GameObject ) || method.DeclaringType == typeof( GameObjectSystem ) );
}
