using Sandbox;
using Sandbox.Modals;
namespace MenuProject.Modals;

public partial class WorkshopPublishModal : MenuProject.Modals.BaseModal
{
	public string Title;
	public string Description;
	Storage.Visibility Visibility;

	public WorkshopPublishOptions Options { get; set; }

	Panel ThumbnailPanel = default;

	Texture thumbnailTexture;

	StoragePublish item;

	protected override int BuildHash() => HashCode.Combine( item?.PercentComplete, item?.IsPublishing, item?.IsFinished );

	protected override void OnParametersSet()
	{
		Title = Options.Title ?? "Item Title";
		Description = Options.Description ?? "";
		Visibility = Options.Visibility;

		thumbnailTexture = Options.Thumbnail?.ToTexture();
	}

	protected override void OnAfterTreeRender( bool firstTime )
	{
		base.OnAfterTreeRender( firstTime );

		if ( ThumbnailPanel != null )
		{
			ThumbnailPanel.Style.BackgroundImage = thumbnailTexture;
		}
	}

	public async Task PublishItem()
	{
		item = new StoragePublish();
		if ( item == null ) return;

		item.Title = Title;
		item.Description = Description;
		item.Metadata = Options.Metadata;
		item.Visibility = Visibility;
		item.KeyValues = Options.KeyValues;
		item.Tags = Options.Tags;
		item.Thumbnail = Options.Thumbnail;
		item.FileSystem = Options.StorageEntry.Files;

		StateHasChanged();

		await item.Submit();

		StateHasChanged();

		Log.Info( $"Published new workshop file {item.ItemId}" );
	}

	void ViewOnWeb()
	{
		MenuUtility.OpenUrl( $"https://steamcommunity.com/sharedfiles/filedetails/?id={item.ItemId}" );
	}

}
