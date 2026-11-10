using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Sandbox;

public partial class Package
{
	/// <summary>
	/// Small icon badges shown over the package's thumbnail - workshop-approved,
	/// updated-since-you-played, favourited, contest-winner, etc. Computed on the backend
	/// when the package is served. Never default; empty when there's no flair.
	/// </summary>
	public ImmutableArray<PackageFlair> Flair { get; internal set; } = ImmutableArray<PackageFlair>.Empty;

	/// <summary>
	/// A single badge shown over a package's thumbnail.
	/// </summary>
	/// <param name="Kind">What this flair is, e.g. "workshop-approved". Drives the icon/tooltip and lets UI style or de-duplicate them.</param>
	/// <param name="Icon">Material Symbols icon name, e.g. "verified".</param>
	/// <param name="Style">Raw CSS applied inline to the badge, e.g. "background-color: #2d8cf0; color: #fff;".</param>
	/// <param name="Tooltip">Hover text explaining why the flair is shown.</param>
	public readonly record struct PackageFlair( string Kind, string Icon, string Style, string Tooltip )
	{
		internal static ImmutableArray<PackageFlair> FromDto( List<Sandbox.Services.PackageFlair> flair )
		{
			if ( flair is null || flair.Count == 0 )
				return ImmutableArray<PackageFlair>.Empty;

			var array = new PackageFlair[flair.Count];

			for ( int i = 0; i < array.Length; i++ )
			{
				var x = flair[i];
				array[i] = new PackageFlair( x.Kind, x.Icon, x.Style, x.Tooltip );
			}

			return ImmutableCollectionsMarshal.AsImmutableArray( array );
		}
	}
}
