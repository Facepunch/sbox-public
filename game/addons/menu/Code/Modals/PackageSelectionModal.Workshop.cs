using Sandbox;

namespace MenuProject.Modals;

/// <summary>
/// Browsing the workshop, or listing the maps we're subscribed to on it
/// </summary>
public partial class PackageSelectionModal
{
	/// <summary>
	/// Where the packages come from, when it's not a mounted game - browsing the workshop, or what we're subscribed to
	/// </summary>
	enum PackageSource
	{
		Browse,
		Subscribed
	}

	PackageSource packageSource;

	bool IsWorkshopSource => !IsMountedSource && packageSource != PackageSource.Browse;

	/// <summary>
	/// What we're subscribed to on the workshop, for <see cref="PackageSource.Subscribed"/> - and what it was found for,
	/// so it's found again when that changes
	/// </summary>
	Package[] workshopPackages;
	string workshopPackagesFor;

	async void RefreshWorkshopPackages()
	{
		var key = $"{packageSource}|{debouncedSearch}|{string.Join( ",", packageTypes )}";
		if ( key == workshopPackagesFor ) return;

		workshopPackagesFor = key;
		workshopPackages = null;

		Package[] packages;

		try
		{
			packages = await MenuUtility.FindWorkshopPackagesAsync( debouncedSearch, true, packageTypes );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't list workshop packages ({e.Message})" );
			packages = [];
		}

		// Something else was asked for while we were finding these
		if ( key != workshopPackagesFor ) return;

		workshopPackages = packages;
		StateHasChanged();
	}

	void SelectPackageSource( PackageSource source )
	{
		SelectSource( null );
		packageSource = source;
	}
}
