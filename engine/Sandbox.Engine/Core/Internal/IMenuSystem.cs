using Sandbox.Menu;

namespace Sandbox.Internal;

/// <summary>
/// This is how the engine communicates with the menu system
/// </summary>
public interface IMenuSystem
{
	internal static IMenuSystem Current { get; set; }

	/// <summary>
	/// Called to initialize the menu system
	/// </summary>
	public void Init();

	/// <summary>
	/// Close down the menu, delete everything
	/// </summary>
	public void Shutdown();

	/// <summary>
	/// Called every frame, to let the menu think
	/// </summary>
	public void Tick();

	/// <summary>
	/// Show a popup
	/// </summary>
	public void Popup( string type, string title, string subtitle );

	/// <summary>
	/// Show a question
	/// </summary>
	public void Question( string message, string icon, Action yes, Action no );

	/// <summary>
	/// Package closed. Add a toast asking if it was cool or not
	/// </summary>
	public void OnPackageClosed( Package package );

	/// <summary>
	/// The backend is telling us that the number of users playing has changed
	/// </summary>
	void PackageUsageChanged( string packageIdent, long userCount );

	/// <summary>
	/// Notifies that the number of favourites for the specified package has changed.
	/// </summary>
	void PackageFavouritesChanged( string packageIdent, long value );

	/// <summary>
	/// True if we want to force the cursor to be visible and swallow input.
	/// This is used for the developer console and loading screens.
	/// </summary>
	public bool ForceCursorVisible { get; }
}

/// <summary>
/// Used to talk to the menu's loading screen.
/// </summary>
internal interface ILoadingInterface : IDisposable
{
	public void LoadingProgress( LoadingProgress progress );
}
