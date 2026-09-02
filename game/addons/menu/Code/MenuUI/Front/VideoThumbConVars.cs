using Sandbox;
using Sandbox.UI;
using PackageCard = MenuProject.UI.PackageCard;

namespace MenuProject.MenuUI.Front;

/// <summary>
/// Console knobs for PackageCard's video thumbnails. They have to be declared in a menu
/// assembly: a plain [ConVar] in addon code gets a null Context and never registers here.
/// </summary>
internal static class VideoThumbConVars
{
	[MenuConVar( "menu_videothumbs", Help = "Autoplay video thumbnails on package cards, not just on hover", Saved = true )]
	public static bool VideoThumbs
	{
		get => PackageCard.VideoThumbs;
		set => PackageCard.VideoThumbs = value;
	}

	[MenuConVar( "menu_videothumbs_per_shelf", Help = "How many package videos play at once in each front page shelf", Saved = true, Min = 0, Max = 12 )]
	public static int VideoThumbsPerShelf
	{
		get => PackageCard.VideoThumbsPerShelf;
		set => PackageCard.VideoThumbsPerShelf = value;
	}

	[MenuConVar( "menu_videothumbs_per_grid", Help = "How many package videos play at once in a package grid", Saved = true, Min = 0, Max = 32 )]
	public static int VideoThumbsPerGrid
	{
		get => PackageCard.VideoThumbsPerGrid;
		set => PackageCard.VideoThumbsPerGrid = value;
	}

	[MenuConVar( "menu_videothumbs_seconds", Help = "Seconds a package video plays before handing over to the next card", Saved = true, Min = 1, Max = 120 )]
	public static float VideoThumbsSeconds
	{
		get => PackageCard.VideoThumbsSeconds;
		set => PackageCard.VideoThumbsSeconds = value;
	}
}
