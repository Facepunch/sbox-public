namespace Sandbox.Engine;

internal interface IMenuDll
{
	public static IMenuDll Current { get; set; }

	public void Bootstrap();
	public Task Initialize();
	public void Tick();
	public void RunEvent( string name );
	public void RunEvent( string name, object argument );
	public void RunEvent( string name, object arg0, object arg1 );
	public void RunEvent<T>( Action<T> action );
	public void Exiting();

	public InputContext InputContext => default;

	public void SimulateUI();
	public void ClosePopups( object p );
	bool HasOverlayMouseInput();
	void OnRender( SwapChainHandle_t swapChain );
	void LateTick();
	void Reset();

	IDisposable PushScope();

	public Scene Scene { get; }
}
