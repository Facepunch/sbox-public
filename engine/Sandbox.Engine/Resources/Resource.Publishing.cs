namespace Sandbox;

public partial class Resource
{
	/// <summary>
	/// When publishing an asset we'll call into this method to allow the resource to configure how it wants to be published.
	/// This allows your resource to make bespoke decisions to configure publishing based on its content.
	/// </summary>
	public virtual void ConfigurePublishing( ResourcePublishContext context )
	{
		// nothing
	}

}

/// <summary>
/// Created by the editor when publishing a resource, passed into Resource.ConfigurePublishing. This allows
/// the resource to configure how it wants to be published.
/// </summary>
public sealed class ResourcePublishContext
{
	/// <summary>
	/// Can be set to false using SetPublishingDisabled
	/// </summary>
	public bool PublishingEnabled { get; private set; } = true;

	/// <summary>
	/// If publishing is disabled this will be the message to display why.
	/// </summary>
	public string ReasonForDisabling { get; private set; }

	/// <summary>
	/// Allows you to disable publishing for this resource, with a reason that'll be shown
	/// to the user.
	/// </summary>
	public void SetPublishingDisabled( string reason )
	{
		PublishingEnabled = false;
		ReasonForDisabling = reason;
	}

	/// <summary>
	/// A function to create a thumbnail for this resource. If not null, this will be called to create the thumbnail.
	/// </summary>
	public Func<Bitmap> CreateThumbnailFunction { get; set; }

	/// <summary>
	/// Retained for compatibility. Always returns false.
	/// </summary>
	[Obsolete( "Publishing a resource no longer includes project code." )]
	public bool IncludeCode
	{
		get => false;
		set { }
	}

	/// <summary>
	/// If true then we'll offer an option to upload source files with this asset. This will make it easier for people
	/// who want to download and add it to their project, but make their own changes.
	/// </summary>
	public bool CanIncludeSourceFiles { get; set; } = true;
}
