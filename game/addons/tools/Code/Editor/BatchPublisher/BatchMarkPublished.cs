namespace Editor;

public class BatchMarkPublishedWidget : Widget
{
	public Label MessageLabel { get; set; }

	public Layout ButtonLayout { get; private set; }

	Asset[] assets;

	string _org;
	Button continueButton;

	[Editor( "organization" )]
	public string TargetOrg
	{
		get => _org;
		set
		{
			if ( _org == value ) return;

			if ( continueButton.IsValid() )
			{
				continueButton.Enabled = !string.IsNullOrWhiteSpace( value ) && value != "local";
			}

			_org = value;
		}
	}

	public BatchMarkPublishedWidget( Asset[] assets ) : base( null, true )
	{
		this.assets = assets;
		WindowFlags = WindowFlags.Window | WindowFlags.Customized | WindowFlags.WindowTitle | WindowFlags.MSWindowsFixedSizeDialogHint;
		WindowTitle = "Publishing Unpublished Assets";
		SetWindowIcon( "check_circle" );

		FixedWidth = 450;

		Layout = Layout.Row();
		Layout.Margin = 16;

		var iconColumn = Layout.AddColumn();

		iconColumn.Margin = 0;
		iconColumn.Add( new IconButton( "🌥️" ) { IconSize = 48, FixedHeight = 64, FixedWidth = 64, Background = Color.Transparent, TransparentForMouseEvents = true } );
		iconColumn.AddStretchCell();

		Layout.Spacing = 32;

		var column = Layout.AddColumn();

		column.AddSpacingCell( 16 );
		column.Spacing = 16;

		MessageLabel = column.Add( new Label() );
		MessageLabel.WordWrap = true;
		MessageLabel.MinimumWidth = 600;
		MessageLabel.TextSelectable = true;
		MessageLabel.Text = "Please select an org to publish these assets to:";

		column.Add( ControlWidget.Create( this.GetSerialized().GetProperty( "TargetOrg" ) ) );

		column.AddSpacingCell( 16 );

		column.AddStretchCell();

		ButtonLayout = column.AddRow();
		ButtonLayout.Spacing = 8;

		ButtonLayout.AddStretchCell();

		continueButton = ButtonLayout.Add( new Button.Primary( "Continue" ) );
		continueButton.Clicked = () => Proceed();

		var cancel = ButtonLayout.Add( new Button( "Cancel" ) );
		cancel.Clicked = () => Close();

		TargetOrg = Project.Current.Config.Org;
	}

	void Proceed()
	{
		foreach ( var a in assets )
		{
			a.Publishing.Enabled = true;
			a.Publishing.ProjectConfig.Org = TargetOrg;
			a.Publishing.Save();
		}

		BatchPublisher.FromAssets( assets );
		Close();
	}

	protected override bool OnClose()
	{
		return true;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.SetBrushAndPen( Theme.WidgetBackground );
		Paint.DrawRect( LocalRect );
	}
}
