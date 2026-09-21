using System.Threading;

namespace Editor.Wizards;

/// <summary>
/// A page inside a BaseWizard
/// </summary>
public class BaseWizardPage : Widget
{
	protected Layout BodyLayout;
	internal CancellationTokenSource TokenSource;
	internal Task OpenTask;
	internal Task<bool> FinishTask;

	/// <summary>
	/// Automatically proceed to the next step instead of wanting the user to press next
	/// </summary>
	public virtual bool IsAutoStep => false;

	public BaseWizardPage() : base( null )
	{
		Visible = false;

		Layout = Layout.Column();
		BodyLayout = Layout.Add( Layout.Column(), 1 );
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		TokenSource?.Cancel();
	}

	public virtual async Task OpenAsync()
	{
		await Task.CompletedTask;
	}

	/// <summary>
	/// Rebuild the current page's UI after hotload using its existing state.
	/// This must not restart work performed by OpenAsync or FinishAsync.
	/// Pages drawn directly in OnPaint only need a repaint.
	/// </summary>
	public virtual void Rebuild()
	{
		Update();
	}

	public virtual async Task<bool> FinishAsync()
	{
		await Task.CompletedTask;
		return true;
	}

	public virtual void OnNavigateAway()
	{

	}

	public virtual bool CanProceed()
	{
		return true;
	}

	public virtual void OnSave()
	{

	}

	public virtual string PageTitle => "Page Title";
	public virtual string PageSubtitle => "Page Subtitle";
	public virtual string NextButtonText => "Next";
}
