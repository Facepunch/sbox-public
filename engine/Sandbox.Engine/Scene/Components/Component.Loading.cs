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
		// A previous callback in this batch may have destroyed the component.
		var owner = GameObject;
		if ( owner is null )
			return;

		var scene = owner.Scene;
		var loadContext = new LoadingContext();
		var task = OnLoad( loadContext );
		if ( task is null || task.IsCompletedSuccessfully || task.IsCanceled )
			return;

		loadContext.Task = WaitForLoad( task, owner );
		if ( !loadContext.IsCompleted && scene.IsValid() )
			scene.AddLoadingTask( loadContext );
	}

	private async Task WaitForLoad( Task task, GameObject owner )
	{
		owner.BeginLoading();
		try
		{
			await task;
		}
		catch ( OperationCanceledException ) when ( task.IsCanceled )
		{
			// Owner-bound tasks can be cancelled when the object is disabled or destroyed.
		}
		catch ( Exception e )
		{
			Log.Error( e, $"Exception when calling 'OnLoad' on {GetType().Name}: {e.Message}" );
		}
		finally
		{
			owner.EndLoading();
		}
	}

	internal void OnLoadInternal()
	{
		CallbackBatch.Add( CommonCallback.Loading, this, "LaunchLoader" );
	}
}
