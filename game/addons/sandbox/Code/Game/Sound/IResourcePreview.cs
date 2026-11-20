/// <summary>
/// Implement on a <see cref="GameResource"/> to enable in-picker previewing.
/// When the user clicks an item in the resource picker, <see cref="OnPreview"/> is called
/// instead of immediately selecting it. A "Select" button confirms the choice.
/// </summary>
public interface IResourcePreview
{
	/// <summary>
	/// Called when the user clicks this resource in the picker for preview.
	/// Use this to play a sound, show a visual, etc.
	/// </summary>
	void OnPreview();

	/// <summary>
	/// Called when the preview should stop (another item previewed, picker closed, or resource selected).
	/// </summary>
	void OnPreviewStop();
}
