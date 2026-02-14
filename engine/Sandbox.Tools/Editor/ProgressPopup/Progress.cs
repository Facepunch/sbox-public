using System;
using System.Threading;
namespace Editor;

class PopupProgress : IProgressSection
{
	public string Icon { get; set { field = value; UpdateWindow(); } }
	public string Title { get; set { field = value; UpdateWindow(); } }
	public string Subtitle { get; set { field = value; UpdateWindow(); } }
	public double Current { get; set { field = value; UpdateWindow(); } }
	public double TotalCount { get; set { field = value; UpdateWindow(); } }

	ProgressWindow currentWindow;

	public PopupProgress()
	{
		currentWindow ??= new ProgressWindow();

		// new state
		currentWindow.Window.WindowTitle = "Progress..";

		currentWindow.Show();
	}

	~PopupProgress()
	{
		MainThread.QueueDispose( this );
	}

	void UpdateWindow()
	{
		currentWindow.TaskTitle = Title;
		currentWindow.ProgressCurrent = Current;
		currentWindow.ProgressTotal = TotalCount;
		currentWindow.Update();

		Application.Spin();
	}

	public void Dispose()
	{
		currentWindow?.Window?.Destroy();
		currentWindow = null;

		GC.SuppressFinalize( this );
	}

	public CancellationToken GetCancel()
	{
		return currentWindow?.GetCancel() ?? CancellationToken.None;
	}

	public void Cancel()
	{
		// todo?
	}
}

class PopupToast : IProgressSection
{
	public string Icon { get; set; }
	public string Title { get; set; }
	public string Subtitle { get; set; }
	public double Current { get; set; }
	public double TotalCount { get; set; }

	CancellationTokenSource cts;

	public PopupToast()
	{
		cts = new CancellationTokenSource();
		Icon = "hourglass_bottom";
		Title = "Progress..";

		var toastManager = EditorTypeLibrary.GetType( "ToastManager" );
		var update = toastManager.GetStaticMethod( "AddProgress" );
		update?.Invoke( null, [(IProgressSection)this] );
	}

	~PopupToast()
	{
		MainThread.QueueDispose( this );
	}

	public void Dispose()
	{
		GC.SuppressFinalize( this );

		var toastManager = EditorTypeLibrary.GetType( "ToastManager" );
		var end = toastManager.GetStaticMethod( "RemoveProgress" );
		end?.Invoke( null, [(IProgressSection)this] );
	}

	public CancellationToken GetCancel()
	{
		return cts.Token;
	}

	public void Cancel()
	{
		cts?.Cancel();
	}
}
