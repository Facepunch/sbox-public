using System;
using System.Threading;
namespace Editor;



public static class Progress
{
	static ProgressWindow currentWindow;
	static int Popups;

	class ProgressSection : IDisposable
	{
		bool disposed;
		string oldTitle;

		public ProgressSection( string name )
		{
			currentWindow ??= new ProgressWindow();
			//currentWindow.Parent = EditorMainWindow.Current;
			Popups++;

			// save state
			oldTitle = currentWindow.Window.WindowTitle;

			// new state
			currentWindow.Window.WindowTitle = name;

			currentWindow.Show();
		}

		public void Dispose()
		{
			if ( disposed ) return;
			disposed = true;

			Popups--;

			if ( Popups <= 0 )
			{
				Popups = 0;
				currentWindow.Window.Destroy();
				currentWindow = null;
			}
			else
			{
				// restore state
				currentWindow.Window.WindowTitle = oldTitle;
			}

		}
	}

	public static IDisposable Start( string name )
	{
		return new ProgressSection( name );
	}

	public static void Update( string title, float current = 0, float total = 0 )
	{
		if ( currentWindow == null )
			return;

		currentWindow.TaskTitle = title;
		currentWindow.ProgressCurrent = current;
		currentWindow.ProgressTotal = total;
		currentWindow.Update();

		Application.Spin();
	}

	public static CancellationToken GetCancel()
	{
		if ( currentWindow == null )
			return CancellationToken.None;

		return currentWindow.GetCancel();
	}
}
