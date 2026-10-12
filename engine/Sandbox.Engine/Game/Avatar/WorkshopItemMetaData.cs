namespace Sandbox;

/// <summary>
/// What a workshop item says it is, kept in the item's Steam metadata - so a query says which package it's for
/// without downloading it. Steam answers from its cache offline, for items it's seen before.
/// </summary>
public struct WorkshopItemMetaData
{
	public string Title { get; set; }
	public string PackageIdent { get; set; }
	public ulong WorkshopId { get; set; }

	/// <summary>
	/// The package type, e.g. "map"
	/// </summary>
	public string TypeName { get; set; }

	/// <summary>
	/// The part of the revision's meta json playing it needs without the backend - PrimaryAsset and ParentPackage
	/// </summary>
	public string Meta { get; set; }

	/// <summary>
	/// The engine's <see cref="Engine.Protocol.Api"/> when it was published - one newer than ours can't be played
	/// </summary>
	public int ApiVersion { get; set; }
}
