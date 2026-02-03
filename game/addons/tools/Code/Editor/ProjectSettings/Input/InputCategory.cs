namespace Editor.ProjectSettingPages;

[Title( "Input" ), Icon( "input" )]
internal sealed class InputCategory : ProjectSettingsWindow.Category
{
	private Widget ActionsTree { get; set; }
	private InputSettings InputSettings { get; set; }
	public InputAction SelectedAction { get; set; }

	ControlModeSettings controlSettings;

	public override void OnInit( Project project )
	{
		base.OnInit( project );

		var actions = EditorUtility.LoadProjectSettings<InputSettings>( "Input.config" );

		InputSettings = actions;

		var row1 = BodyLayout.AddColumn();
		row1.Spacing = 4;
		{
			row1.Add( new InformationBox( "You can define your own input actions here. You can access them with Input.Down/Pressed/Released, using the name of the input action." ) );

			ActionsTree = new Widget( null );
			ActionsTree.Layout = Layout.Column();
			ActionsTree.Layout.Margin = 2;
			ActionsTree.Layout.Spacing = 2;

			ActionsTree.OnPaintOverride += () =>
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.5f ) );
				Paint.DrawRect( ActionsTree.LocalRect );
				return true;
			};

			row1.Add( ActionsTree );

			CreateDefaultActions();
			UpdateActionList();
		}

		StartSection( "Control Modes" );

		if ( !project.Config.TryGetMeta( "ControlModes", out controlSettings ) )
			controlSettings = new();

		BodyLayout.Add( new InformationBox( "" +
			"<p>This is <b>specifically</b> for informing users which control modes your game supports. It'll show on the game list this way.</p>" ) );

		{
			var so = controlSettings.GetSerialized();

			var sheet = new ControlSheet();
			sheet.AddObject( so );
			ListenForChanges( so );

			BodyLayout.Add( sheet );
		}
	}

	internal void UpdateActionList()
	{
		ActionsTree.Layout.Clear( true );

		string lastGroup = null;
		foreach ( var group in InputSettings.Actions.GroupBy( x => x.GroupName ) )
		{
			var collapsibleCategory = ActionsTree.Layout.Add( new CollapsibleCategory( null, group.Key ) );

			foreach ( var action in group )
			{
				var p = collapsibleCategory.Container.Layout.Add( new InputActionPanel( action, this ) );
				p.Changed = () => StateHasChanged( null );
			}

			collapsibleCategory.StateCookieName = $"inputpage.category.{group.Key}";
			lastGroup = group.Key;
		}

		ActionsTree.Layout.AddSpacingCell( 2 );

		var footer = ActionsTree.Layout.AddRow();
		footer.Margin = new( 4, 1, 4, 3 );
		footer.Spacing = 4;

		var entry = footer.Add( new LineEdit() { MaximumHeight = 24, PlaceholderText = "Add New Action..." }, 2 );

		var add = () =>
		{
			var name = string.IsNullOrEmpty( entry.Text ) ? $"Action {InputSettings.Actions.Count}" : entry.Text;
			AddAction( new InputAction()
			{
				Name = name,
				GroupName = lastGroup ?? "Other"
			}, updateList: true );
		};

		entry.ReturnPressed += add;

		var maxAmount = 64;
		var btn = footer.Add( new Button.Primary( "Add", "new_label" ), 0 );
		btn.Clicked += add;
		btn.Enabled = InputSettings.Actions.Count < maxAmount;

		if ( InputSettings.Actions.Count >= maxAmount ) btn.ToolTip = "We only support a limit of 64 input actions to keep things speedy.";

		var clear = footer.Add( new Button( "Clear", "auto_fix_normal" ) );
		clear.Clicked += () =>
		{
			var confirm = new PopupWindow(
				"Clear Input Actions", "Are you sure you want to clear all input actions?", "Cancel",
				new Dictionary<string, System.Action>()
				{
					{ "Yes", () => { InputSettings.Actions = new(); add(); } }
				}
			);
			confirm.Show();
		};

		var reset = footer.Add( new Button( "Reset to Default", "restart_alt" ) );
		reset.Clicked += () =>
		{
			InputSettings.Actions = new();
			CreateDefaultActions();
			UpdateActionList();
			StateHasChanged();
		};
	}

	void CreateDefaultActions()
	{
		if ( InputSettings.Actions.Count > 0 ) return;

		foreach ( var input in InputSystem.GetCommonInputs() )
		{
			AddAction( input, updateList: false );
		}
	}

	internal void RemoveAction( InputAction action )
	{
		var index = InputSettings.Actions.IndexOf( action );

		if ( index >= 0 )
		{
			InputSettings.Actions.RemoveAt( index );
			UpdateActionList();
		}

		StateHasChanged();
	}

	internal void AddAction( InputAction action, bool updateList = true )
	{
		InputSettings.Actions.Add( action );
		StateHasChanged();

		if ( updateList ) UpdateActionList();
	}

	public override void OnSave()
	{
		EditorUtility.SaveProjectSettings( InputSettings, "Input.config" );
		Project.Config.SetMeta( "ControlModes", controlSettings );

		base.OnSave();
	}
}
