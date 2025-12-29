using NativeEngine;

namespace Sandbox.Engine;

internal unsafe interface IToolsDll
{
	public static IToolsDll Current { get; set; }


	public void Bootstrap();
	public Task Initialize();
	public void Tick();
	public void RunEvent( string name );
	public void RunEvent( string name, object argument );
	public void RunEvent( string name, object arg0, object arg1 );
	public void RunEvent<T>( Action<T> action );
	public void Exiting();
	public bool ConsoleFocus();
	public void ExitPlaymode();

	public void Spin();
	public void RunFrame();
	public void OnRender();

	public void OnFunctionKey( ButtonCode key, KeyboardModifiers modifiers );

	/// <summary>
	/// Registers exclusive Sandbox.Tools <see cref="Sandbox.IHandle"/> types
	/// </summary>
	public int RegisterHandle( IntPtr ptr, uint type );

	/// <summary>
	/// Load the startup project for the first time
	/// </summary>
	public Task LoadProject();

	public object InspectedObject { get; set; }

	/// <summary>
	/// Is the game view visible, or is it in a tab in the background?
	/// </summary>
	public bool IsGameViewVisible { get; }

	/// <summary>
	/// Called after the host network system is initialised, used to add additional package references etc. to dev servers 
	/// </summary>
	public Task OnInitializeHost();

	/// <summary>
	/// Get a thumbnail for the specified asset.Can return null if not immediately available. 
	/// </summary>
	Bitmap GetThumbnail( string filename );
}
