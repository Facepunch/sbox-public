namespace Editor;

internal class ProgressToast : ToastWidget
{
	Button _cancel;

	public ProgressToast( IProgressSection section )
	{
		_cancel = new Button( "Cancel", this );
		_cancel.Clicked = CancelToast;
		Section = section;
		DrawTimer = false;
	}

	protected override void DoLayout()
	{
		base.DoLayout();

		_cancel.Position = new Vector2( Width - _cancel.Width - 16, Height - _cancel.Height - 16 );
	}

	protected override Vector2 SizeHint()
	{
		return new Vector2( 400, 80 );
	}

	public override bool WantsVisible => true;

	public IProgressSection Section { get; set; }

	public override void Tick()
	{
		if ( !IsRunning )
			return;

		Icon = Section.Icon;
		Title = Section.Title;
		Subtitle = Section.Subtitle;
		ProgressDelta = (float)Section.ProgressDelta;

		BorderColor = Theme.Primary;

		if ( !IsRunning ) BorderColor = Theme.Green;
		if ( Section.GetCancel().IsCancellationRequested ) BorderColor = Theme.Red;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		e.Accepted = true;
	}

	void CancelToast()
	{
		Section?.Cancel();
	}
}
