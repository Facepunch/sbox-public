using Microsoft.CodeAnalysis;
using System.Threading.Tasks;

namespace Sandbox;

internal partial class AssemblyAccess
{
	bool CheckPassesRules()
	{
		Parallel.ForEach( Touched, touch =>
		{
			// Any and all user code is a package, all assemblies are prefixed with this
			// And any code calling to within a package or across packages is free game
			if ( touch.Key.StartsWith( "package." ) )
				return;

			if ( Global.Rules.IsInWhitelist( touch.Key ) )
				return;

			var locations = string.Join( "\n", touch.Value.Locations.Select( x => $"\t{x.Text}" ) );

			Result.Errors.Add( $"{touch.Key}\n{locations}" );
			Result.WhitelistErrors.Add( (touch.Key, touch.Value.Locations.ToArray()) );
		} );

		return Result.Errors.Count == 0;
	}
}
