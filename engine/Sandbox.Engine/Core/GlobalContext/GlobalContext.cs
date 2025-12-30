using Facepunch.ActionGraphs;
using Sandbox.Internal;
using Sandbox.Utility;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace Sandbox.Engine;

internal partial class GlobalContext
{
	/// <summary>
	/// Sandbox.GameInstance or Sandbox.Menu
	/// </summary>
	public Assembly LocalAssembly { get; set; }

	/// <summary>
	/// The input context for this game instance, which contains the current input state and bindings.
	/// </summary>
	public InputContext InputContext { get; set; }

	/// <summary>
	/// The active scene for this game instance.
	/// </summary>
	public Scene ActiveScene { get; set; }

	/// <summary>
	/// For the resource library
	/// </summary>
	public ResourceSystem ResourceSystem { get; set; }

	TypeLibrary _typeLibrary;

	/// <summary>
	/// Holds a list of available classes and types in the game, used for serialization and reflection.
	/// </summary>
	public TypeLibrary TypeLibrary
	{
		get
		{
			if ( _disabledReason is not null )
			{
				throw new InvalidOperationException( $"{nameof( TypeLibrary )} is currently inaccessible. Reason: {_disabledReason}" );
			}

			return _typeLibrary;
		}
		set => _typeLibrary = value;
	}

	/// <summary>
	/// For actiongraph
	/// </summary>
	public NodeLibrary NodeLibrary { get; set; }

	/// <summary>
	/// All of the mounted assets
	/// </summary>
	public BaseFileSystem FileMount { get; set; }

	/// <summary>
	/// A persistent data folder for the current package.
	/// </summary>
	public BaseFileSystem FileData { get; set; }

	/// <summary>
	/// Allows a common place for an org's data
	/// </summary>
	public BaseFileSystem FileOrg { get; set; }

	/// <summary>
	/// Special options for serializing json
	/// </summary>
	public JsonSerializerOptions JsonSerializerOptions { get; set; }

	/// <summary>
	/// For running tasks in a kind of sandboxed way
	/// </summary>
	public TaskSource TaskSource { get; set; }

	/// <summary>
	/// Creates tokens that will be cancelled at the end of the game
	/// </summary>
	public CancellationTokenSource CancellationTokenSource { get; set; }

	/// <summary>
	/// The event system isn't used that much anymore, we should move away from it.
	/// </summary>
	public EventSystem EventSystem { get; set; }

	/// <summary>
	/// The UI system for the game, which manages the user interface elements and their interactions.
	/// </summary>
	public UISystem UISystem { get; set; }

	/// <summary>
	/// Holds cookies for the game, which can be used for storing session data or other small pieces of information that need to persist across requests.
	/// </summary>
	public CookieContainer Cookies { get; set; }

	/// <summary>
	/// Holds language data for the game.
	/// </summary>
	public LanguageContainer Language { get; set; }

	/// <summary>
	/// The global context for the game, which holds references to various systems and libraries used throughout the game.
	/// </summary>
	public GlobalContext()
	{
		CancellationTokenSource = new CancellationTokenSource();
		ResourceSystem = new ResourceSystem();
		EventSystem = new EventSystem();
	}

	/// <summary>
	/// Should be called before using. This will re-initialize the task source stuff, ready to use.
	/// </summary>
	public void Reset()
	{
		var oldCts = CancellationTokenSource;
		CancellationTokenSource = new CancellationTokenSource();

		oldCts?.Cancel();
		oldCts?.Dispose();

		ActiveScene = null;

		TaskSource = new TaskSource( 1 );

		EventSystem?.Dispose();
		EventSystem = new EventSystem();

		UISystem?.Clear();

		Cookies?.Dispose();
		Cookies = null;

		ResourceSystem?.Clear();
		ResourceSystem = new ResourceSystem();
	}

	string _disabledReason;

	/// <summary>
	/// We sometimes want to disable access to certain things, like when we're running static initializers.
	/// This prevents users from relying on behaviours that are not stable.
	/// </summary>
	public IDisposable DisableTypelibraryScope( string reason )
	{
		var oldReason = _disabledReason;
		_disabledReason = reason;

		return new DisposeAction( () => _disabledReason = oldReason );
	}

	internal void OnHotload()
	{
		ResourceSystem.OnHotload();
		UISystem.OnHotload();
	}
}
