namespace Editor;

internal class WelcomePopup : Widget
{
	public Label MessageLabel { get; private set; }
	public Button PreviousButton { get; private set; }
	public Button NextButton { get; private set; }
	public Button SkipButton { get; private set; }
	public Checkbox NeverAskCheckbox { get; private set; }

	public Action OnNext { get; set; }
	public Action OnPrevious { get; set; }
	public Action OnSkip { get; set; }
	public Action OnNeverAskChanged { get; set; }

	Widget contentContainer;
	Layout header;
	Widget headerIcon;
	Widget headerTitle;

	public WelcomePopup( Widget parent ) : base( parent )
	{
		WindowFlags = WindowFlags.FramelessWindowHint | WindowFlags.Tool | WindowFlags.WindowStaysOnTopHint;
		FixedWidth = 500;
		MinimumHeight = 200;
		FocusMode = FocusMode.TabOrClick;
		NoSystemBackground = true;
		TranslucentBackground = true;

		Layout = Layout.Column();
		Layout.Margin = 24;
		Layout.Spacing = 16;

		header = Layout.AddRow();
		header.Spacing = 12;
		header.Alignment = TextFlag.LeftCenter;

		headerIcon = header.Add( new IconButton( "waving_hand" ) { IconSize = 32, FixedWidth = 48, FixedHeight = 48, Background = Color.Transparent, TransparentForMouseEvents = true } );

		headerTitle = header.Add( new Label( "Welcome to the s&box editor!" ) );

		header.AddStretchCell();

		// Message content
		contentContainer = Layout.Add( new Widget( this ) );
		contentContainer.Layout = Layout.Column();
		contentContainer.MinimumHeight = 80;

		MessageLabel = contentContainer.Layout.Add( new Label() );
		MessageLabel.WordWrap = true;

		Layout.AddStretchCell();

		// Never ask again checkbox
		var checkboxRow = Layout.AddRow();
		checkboxRow.Spacing = 8;
		checkboxRow.Alignment = TextFlag.LeftCenter;

		NeverAskCheckbox = checkboxRow.Add( new Checkbox() );
		NeverAskCheckbox.Value = true;
		NeverAskCheckbox.Text = "Don't show this again";
		NeverAskCheckbox.Toggled += () => OnNeverAskChanged?.Invoke();

		checkboxRow.AddStretchCell();

		// Navigation buttons
		var buttonRow = Layout.AddRow();
		buttonRow.Spacing = 8;
		buttonRow.Alignment = TextFlag.RightCenter;

		SkipButton = buttonRow.Add( new Button( "Skip" ) );
		SkipButton.Clicked = () => OnSkip?.Invoke();

		buttonRow.AddStretchCell();

		PreviousButton = buttonRow.Add( new Button( "Previous" ) );
		PreviousButton.Clicked = () => OnPrevious?.Invoke();

		NextButton = buttonRow.Add( new Button.Primary( "Next" ) );
		NextButton.Clicked = () => OnNext?.Invoke();
	}

	public void SetMessage( string message )
	{
		MessageLabel.Text = message;
	}

	public void SetStepInfo( int currentStep, int totalSteps )
	{
		PreviousButton.Enabled = currentStep > 0;

		if ( currentStep == totalSteps - 1 )
		{
			NextButton.Text = "Finish";
		}
		else
		{
			NextButton.Text = "Next";
		}

		// Show header only on first and last steps
		bool showHeader = currentStep == 0 || currentStep == totalSteps - 1;
		headerIcon.Visible = showHeader;
		headerTitle.Visible = showHeader;

		// Shorter popup if header is hidden
		MinimumHeight = showHeader ? 200 : 150;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;

		Paint.ClearPen();
		Paint.SetBrush( Theme.WindowBackground );
		Paint.DrawRect( LocalRect, 8 );

		Paint.SetPen( Theme.Primary.WithAlpha( 0.3f ), 2 );
		Paint.ClearBrush();
		Paint.DrawRect( LocalRect.Shrink( 1 ), 8 );

		Paint.SetPen( Color.Black.WithAlpha( 0.1f ), 4 );
		Paint.DrawRect( LocalRect.Shrink( -2 ), 8 );
	}
}
