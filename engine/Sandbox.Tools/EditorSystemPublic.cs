
using System;
using System.Threading;

namespace Sandbox;

public class EditorSystemPublic : EditorSystem
{
	public override Scene Scene => SceneEditorSession.Active?.Scene;

	/// <summary>
	/// Run a process on multiple items, show progress bar
	/// </summary>
	public override async Task ForEachAsync<T>( IEnumerable<T> list, string title, Func<T, Task> worker, CancellationToken cancel = default )
	{
		int count = list.Count();
		if ( count <= 0 ) return;

		using ( var p = Progress.Start( title ) )
		{
			var ct = Progress.GetCancel();
			Progress.Update( title, 0, count );
			await Task.Delay( 1 );

			int current = 0;
			FastTimer ft = FastTimer.StartNew();
			foreach ( var item in list )
			{
				current++;
				await worker( item );

				Progress.Update( title, current, count );

				if ( ft.ElapsedMilliSeconds > 50 )
				{
					await Task.Delay( 1 );
					ft = FastTimer.StartNew();
				}

				if ( ct.IsCancellationRequested ) return;
				if ( cancel.IsCancellationRequested ) return;
			}
		}
	}
}
