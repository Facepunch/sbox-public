namespace Editor.Wizards;

/// <summary>
/// A window that has multiple steps, talks you through a process
/// </summary>
public abstract class BaseWizard : Widget
{
	public virtual string Title => "Base Page";
	public virtual string Icon => "people";

	public Layout BodyLayout;
	public Layout HeaderLayout;
	public Layout FooterRight;
	public Layout FooterLeft;
	public Layout Footer;

	protected ScrollArea ScrollArea;

	protected Button BackButton { get; private set; }
	protected Button NextButton { get; private set; }
	protected Label PageTitle { get; private set; }
	protected Label PageSubtitle { get; private set; }

	bool loading;
	BaseWizardPage _current;

	protected BaseWizardPage Current
	{
		get => _current;
		set
		{
			if ( _current == value ) return;

			foreach ( var p in Steps )
				p.Visible = false;

			_current?.TokenSource?.Cancel();

			_current = value;
			_ = SwitchCurrentPage();
			Update();
		}
	}

	protected List<BaseWizardPage> Steps = new();

	public BaseWindow CreateWindow( int width = 1280, int height = 770 )
	{
		var window = new BaseWindow()
		{
			Size = new Vector2( width, height ),
			MinimumSize = new Vector2( width, height ),
			TranslucentBackground = true,
			NoSystemBackground = true,
			WindowTitle = "Wizard"
		};

		window.SetModal( true, true );

		window.Layout = Layout.Column();
		window.Layout.Margin = 0;
		window.Layout.Spacing = 0;

		window.Layout.Add( this );

		window.WindowTitle = $"{Title}";
		window.SetWindowIcon( Icon );
		window.Show();

		return window;
	}

	internal BaseWizard() : base( null )
	{
		Layout = Layout.Column();

		HeaderLayout = Layout.AddColumn();
		var body = Layout.AddColumn( 1 );
		ScrollArea = new ScrollArea( this );
		body.Add( ScrollArea );
		ScrollArea.Canvas = new Widget( ScrollArea );
		ScrollArea.Canvas.Layout = Layout.Column();
		BodyLayout = ScrollArea.Canvas.Layout;
		BodyLayout.Margin = 32;

		Layout.AddStretchCell( 0 );

		Footer = Layout.AddRow();
		Footer.Margin = 16;

		FooterLeft = Footer.AddRow();
		Footer.AddStretchCell();
		FooterRight = Footer.AddRow();
		FooterRight.Spacing = 4;

		BuildNavigation();
	}

	void BuildNavigation()
	{
		HeaderLayout.Clear( true );
		FooterLeft.Clear( true );
		FooterRight.Clear( true );

		HeaderLayout.Margin = new Sandbox.UI.Margin( 0, 0, 0, 0 );
		PageTitle = HeaderLayout.Add( new Label.Title( "Title" ) );
		PageTitle.ContentMargins = new Sandbox.UI.Margin( 32, 16, 0, 0 );
		PageSubtitle = HeaderLayout.Add( new Label.Body( "Subtitle" ) { Color = Theme.Primary.Lighten( 0.3f ) } );
		PageSubtitle.ContentMargins = new Sandbox.UI.Margin( 32, 0, 0, 16 );

		FooterRight.Spacing = 8;
		BackButton = FooterRight.Add( new Button( "Back", "navigate_before" ) );
		BackButton.Clicked = LastPage;

		NextButton = FooterRight.Add( new Button.Primary( "Next", "navigate_next" ) );
		NextButton.Clicked = NextPage;
	}

	public Layout StartSection( string name, Layout layout = null )
	{
		layout ??= BodyLayout;

		var block = layout.AddColumn();
		block.Margin = 8;

		block.Add( new Label.Subtitle( name ) );
		var inner = block.AddColumn();
		inner.Margin = 8;

		return inner;
	}

	public void AddFooterDefaults()
	{
		var revert = new Button( "Revert Changes", "history", this );
		revert.Clicked = Reset;

		var save = new Button.Primary( "Save", "save", this );
		save.Clicked = OnSave;

		FooterRight.Add( revert );
		FooterRight.Add( save );
	}

	public virtual void OnSave()
	{

	}

	/// <summary>
	/// Rebuild the UI without reopening pages or restarting their operations.
	/// </summary>
	[EditorEvent.Hotload]
	public virtual void Reset()
	{
		if ( !IsValid ) return;

		BuildNavigation();
		Current?.Rebuild();
		Tick();
		Update();
	}

	async Task SwitchCurrentPage()
	{
		var current = Current;
		var source = new System.Threading.CancellationTokenSource();
		current.TokenSource = source;
		current.FinishTask = null;
		try
		{
			loading = true;
			current.OpenTask = current.OpenAsync();
			await current.OpenTask;
			if ( !IsValid || Current != current || source.IsCancellationRequested ) return;
			loading = false;

			if ( _current.IsAutoStep && _current.CanProceed() )
			{
				NextPage();
			}
		}
		catch ( System.OperationCanceledException ) when ( source.IsCancellationRequested )
		{
		}
		catch ( System.Exception e )
		{
			Log.Error( e );
		}
	}

	/// <summary>
	/// Stop the current page and wait for its work to finish before reopening an earlier step.
	/// </summary>
	protected async Task ReturnToPageAsync( BaseWizardPage page )
	{
		var current = Current;
		loading = true;
		current.TokenSource?.Cancel();

		try
		{
			await Task.WhenAll( current.OpenTask ?? Task.CompletedTask, current.FinishTask ?? Task.CompletedTask );
		}
		catch ( System.Exception )
		{
			// The page navigation handlers already report failures. Restart even if its work failed.
		}

		if ( !IsValid ) return;
		Current = page;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		var steps = (float)Steps.Count - 1;
		var index = (float)Steps.IndexOf( Current );
		var progress = index / steps;

		var left = Color.Lerp( Theme.WindowBackground, Theme.Primary, 0.5f );

		var r = HeaderLayout.InnerRect;
		Paint.Antialiasing = true;
		Paint.SetBrushAndPen( left );
		Paint.DrawRect( HeaderLayout.InnerRect );

		Paint.SetBrushAndPen( Theme.WindowBackground );
		Paint.DrawRect( Footer.OuterRect );
	}



	[EditorEvent.Frame]
	public void Tick()
	{
		if ( !IsValid ) return;

		if ( Current == null )
			Current = Steps.FirstOrDefault();

		if ( Current == null ) return;

		bool finalpage = Current == Steps.LastOrDefault();

		PageTitle.Text = Current.PageTitle;
		PageSubtitle.Text = Current.PageSubtitle;

		// special case for end of wizard
		if ( finalpage && Current.CanProceed() && !loading )
		{
			BackButton.Visible = false;
			NextButton.Enabled = true;
			NextButton.Text = "Finished";
			NextButton.Icon = "done";
			return;
		}

		BackButton.Visible = true;
		BackButton.Enabled = !loading && Steps.First() != Current;
		NextButton.Enabled = !loading && Steps.Last() != Current && Current.CanProceed();
		NextButton.Text = Current.NextButtonText;
		NextButton.Icon = "navigate_next";
	}

	protected void AddStep( BaseWizardPage p )
	{
		Steps.Add( p );
		BodyLayout.Add( p );
		p.Visible = false;
	}

	protected void LastPage()
	{
		if ( loading ) return;

		var i = Steps.IndexOf( Current );
		if ( i <= 0 ) return;

		OnSave();

		Current = Steps[i - 1];
	}

	protected void NextPage()
	{
		if ( loading ) return;

		if ( !Current.CanProceed() )
			return;

		OnSave();

		// Finish closes the window
		bool finalpage = Current == Steps.Last();
		if ( finalpage )
		{
			var p = Parent;
			while ( p.IsValid() )
			{
				if ( p is BaseWindow )
				{
					p.Close();
					return;
				}

				p = p.Parent;
			}

			return;
		}

		_ = TrySwitchToNextPage();
	}

	async Task TrySwitchToNextPage()
	{
		loading = true;
		var i = Steps.IndexOf( Current );
		var current = Current;
		var next = Steps[i + 1];
		var source = current.TokenSource;

		try
		{
			current.FinishTask = current.FinishAsync();
			var result = await current.FinishTask;
			if ( !IsValid || Current != current || source.IsCancellationRequested ) return;

			loading = false;

			if ( !result )
				return;

			Current = next;
		}
		catch ( System.OperationCanceledException ) when ( source.IsCancellationRequested )
		{
		}
		catch ( System.Exception e )
		{
			Log.Error( e );
		}
	}

}
