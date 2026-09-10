namespace Editor;

[Dock( "Editor", "Allocations", "timer", DockArea.Bottom )]
public class Allocations : Widget
{
	Sandbox.Diagnostics.Allocations.Scope scope;

	Layout Header;
	Layout Body;

	Button StopStart;

	public Allocations( Widget parent ) : base( parent )
	{
		MinimumSize = 200;
		scope = new Sandbox.Diagnostics.Allocations.Scope();

		Layout = Layout.Column();
		Header = Layout.AddRow();
		Header.Spacing = 2;
		Header.Margin = 4;
		Body = Layout.AddColumn( 1 );
		Body.AddStretchCell();

		StopStart = Header.Add( new Button( "Start", this ) { Clicked = () => Start() } );
		Header.Add( new Button( "Clear", this ) { Clicked = () => scope.Clear() } );
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		scope?.Stop();
		scope = null;
	}

	public void Start()
	{
		scope.Start();

		StopStart.Text = "Stop";
		StopStart.Clicked = Stop;
	}

	void Stop()
	{
		scope.Stop();

		StopStart.Text = "Start";
		StopStart.Clicked = Start;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		const int lineHeight = 16;

		var y = Body.OuterRect.Top + 8;
		foreach ( var line in scope.Entries.OrderByDescending( x => x.Count ).Take( 64 ) )
		{
			Paint.DrawText( new Rect( 0, y, Width, lineHeight ), $"{line.Count}: {line.Name}", TextFlag.LeftBottom );
			y += lineHeight;
		}

		Update();
	}
}
