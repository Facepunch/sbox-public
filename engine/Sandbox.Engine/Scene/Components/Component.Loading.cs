namespace Sandbox;

public partial class Component
{
	protected virtual Task OnLoad()
	{
		return Task.CompletedTask;
	}

	protected virtual Task OnLoad( LoadingContext context )
	{
		return OnLoad();
	}

	private void LaunchLoader()
	{
		var loadContext = new LoadingContext();

		loadContext.Task = OnLoad( loadContext );
		if ( loadContext.IsCompleted )
			return;

		loadContext.Task = WaitForLoad( loadContext.Task );

		GameObject.Flags |= GameObjectFlags.Loading;
		Scene.AddLoadingTask( loadContext );
	}

	private async Task WaitForLoad( Task task )
	{
		await task;

		if ( !this.IsValid() ) return;
		if ( !GameObject.IsValid() ) return;

		GameObject.Flags &= ~GameObjectFlags.Loading;
	}

	internal void OnLoadInternal()
	{
		CallbackBatch.Add( CommonCallback.Loading, LaunchLoader, this, "LaunchLoader" );
	}
}
