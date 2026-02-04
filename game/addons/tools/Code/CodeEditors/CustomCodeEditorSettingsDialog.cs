namespace Editor.CodeEditors;

/// <summary>
/// Dialog for adding or editing custom code editor configurations.
/// </summary>
public class CustomCodeEditorSettingsDialog : Dialog
{
	private CustomCodeEditorConfig _config;
	private bool _isNew;
	private Action _onSaved;

	private LineEdit _nameInput;
	private LineEdit _pathInput;
	private LineEdit _argsInput;
	private LineEdit _solutionArgsInput;

	/// <summary>
	/// Creates a new custom code editor settings dialog.
	/// </summary>
	/// <param name="config">Existing config to edit, or null to create a new one.</param>
	/// <param name="onSaved">Callback invoked when the configuration is saved or deleted. Optional.</param>
	public CustomCodeEditorSettingsDialog( CustomCodeEditorConfig config = null, Action onSaved = null )
	{
		_isNew = config == null;
		_config = config?.Clone() ?? new CustomCodeEditorConfig();
		_onSaved = onSaved;

		Window.MinimumWidth = 560;
		Window.MinimumHeight = 420;
		Window.Size = new Vector2( 560, 420 );
		Window.WindowTitle = _isNew ? "Add Custom Editor" : $"Configure {_config.Name}";
		Window.SetWindowIcon( "code" );
		Window.SetModal( true, true );

		Layout = Layout.Column();
		Layout.Margin = 20;
		Layout.Spacing = 16;

		BuildHeader();
		BuildForm();
		BuildHelpSection();
		BuildButtons();

		Show();
	}

	/// <summary>
	/// Builds the header section with icon and title.
	/// </summary>
	private void BuildHeader()
	{
		var header = Layout.AddRow();
		header.Spacing = 12;

		var iconContainer = new Widget( this );
		iconContainer.MinimumWidth = 48;
		iconContainer.MinimumHeight = 48;
		iconContainer.MaximumWidth = 48;
		iconContainer.MaximumHeight = 48;
		iconContainer.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Primary.WithAlpha( 0.15f ) );
			Paint.DrawRect( iconContainer.LocalRect, 8 );
			Paint.SetPen( Theme.Primary );
			Paint.DrawIcon( iconContainer.LocalRect, "edit", 28, TextFlag.Center );
			return true;
		};
		header.Add( iconContainer );

		var textColumn = header.AddColumn();
		textColumn.Spacing = 2;

		var title = new Label( _isNew ? "Add Custom Code Editor" : "Configure Code Editor" );
		title.SetStyles( "font-size: 16px; font-weight: 600;" );
		textColumn.Add( title );

		var subtitle = new Label( "Configure a custom IDE or text editor with custom arguments." );
		subtitle.SetStyles( "color: rgba(255,255,255,0.5);" );
		textColumn.Add( subtitle );

		header.AddStretchCell();
	}

	/// <summary>
	/// Builds the main form with input fields for editor configuration.
	/// </summary>
	private void BuildForm()
	{
		var formContainer = new Widget( this );
		formContainer.Layout = Layout.Column();
		formContainer.Layout.Spacing = 12;
		Layout.Add( formContainer, 1 );

		AddFormRow( formContainer, "Name", "Display name for this editor", out _nameInput, _config.Name );
		AddPathRow( formContainer, "Executable", "Path to the editor executable", out _pathInput, _config.ExecutablePath );
		AddFormRow( formContainer, "File Arguments", "e.g. {path} -n{line} -c{column}", out _argsInput, _config.Arguments );
		AddFormRow( formContainer, "Solution Arguments", "e.g. \"{solution}\"", out _solutionArgsInput, _config.SolutionArguments );

		var info = new Label( "For file and solution arguments, check your editors documentation for proper syntax and usage." );
		info.SetStyles( "font-size: 11px; color: rgba(255, 255, 255, 0.34); font-style: italic;" );
		formContainer.Layout.Add( info );
	}

	/// <summary>
	/// Adds a labeled text input row to the form.
	/// </summary>
	private void AddFormRow( Widget parent, string label, string placeholder, out LineEdit lineEdit, string value )
	{
		var row = parent.Layout.AddColumn();
		row.Spacing = 4;

		var labelWidget = new Label( label );
		labelWidget.SetStyles( "font-weight: 500; font-size: 12px;" );
		row.Add( labelWidget );

		lineEdit = new LineEdit( parent );
		lineEdit.Text = value ?? "";
		lineEdit.PlaceholderText = placeholder;
		lineEdit.MinimumHeight = 32;
		row.Add( lineEdit );
	}

	/// <summary>
	/// Adds a labeled file path input row with a browse button.
	/// </summary>
	private void AddPathRow( Widget parent, string label, string placeholder, out LineEdit lineEdit, string value )
	{
		var row = parent.Layout.AddColumn();
		row.Spacing = 4;

		var labelWidget = new Label( label );
		labelWidget.SetStyles( "font-weight: 500; font-size: 12px;" );
		row.Add( labelWidget );

		var inputRow = row.AddRow();
		inputRow.Spacing = 8;

		lineEdit = new LineEdit( parent );
		lineEdit.Text = value ?? "";
		lineEdit.PlaceholderText = placeholder;
		lineEdit.MinimumHeight = 32;
		inputRow.Add( lineEdit, 1 );

		var capturedLineEdit = lineEdit;
		var browseBtn = new Button.Primary( "Browse..." );
		browseBtn.MinimumHeight = 32;
		browseBtn.Clicked = () => BrowseForExecutable( capturedLineEdit );
		inputRow.Add( browseBtn );
	}

	/// <summary>
	/// Opens a file dialog to browse for an executable file.
	/// Auto-fills the name and arguments based on the selected executable.
	/// </summary>
	private void BrowseForExecutable( LineEdit target )
	{
		var fd = new FileDialog( null );
		fd.Title = "Select Editor Executable";
		fd.SetFindExistingFile();
		fd.SetModeOpen();
		fd.SetNameFilter( "Executable Files (*.exe)" );

		if ( !string.IsNullOrEmpty( target.Text ) )
		{
			try
			{
				var dir = System.IO.Path.GetDirectoryName( target.Text );
				if ( !string.IsNullOrEmpty( dir ) && System.IO.Directory.Exists( dir ) )
					fd.Directory = dir;
			}
			catch ( Exception )
			{
				Log.Error( $"Invalid path format: {target.Text}" );
			}
		}

		if ( fd.Execute() && !string.IsNullOrEmpty( fd.SelectedFile ) )
		{
			target.Text = fd.SelectedFile;

			// Auto-fill the name if empty
			if ( string.IsNullOrWhiteSpace( _nameInput.Text ) )
			{
				var fileName = System.IO.Path.GetFileNameWithoutExtension( fd.SelectedFile );
				_nameInput.Text = fileName.Replace( "-", " " ).Replace( "_", " " ).ToTitleCase();
			}

			// Auto-fill arguments based on detected editor (only for new editors or if using defaults)
			if ( _isNew || IsDefaultArguments() )
			{
				var preset = EditorArgumentPresets.GetPresetForExecutable( fd.SelectedFile );
				_argsInput.Text = preset.FileArgs;
				_solutionArgsInput.Text = preset.SolutionArgs;
			}
		}
	}

	/// <summary>
	/// Checks if the current arguments are default/empty values.
	/// </summary>
	private bool IsDefaultArguments()
	{
		var args = _argsInput.Text.Trim();
		return string.IsNullOrEmpty( args ) ||
			   args == "\"{path}\"" ||
			   args == "\"{path}\" --goto \"{line}:{column}\"";
	}

	/// <summary>
	/// Builds the help section showing available argument variables.
	/// </summary>
	private void BuildHelpSection()
	{
		var helpContainer = new Widget( this );
		helpContainer.Layout = Layout.Column();
		helpContainer.Layout.Spacing = 6;
		helpContainer.Layout.Margin = 12;
		helpContainer.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.WidgetBackground.WithAlpha( 0.5f ) );
			Paint.DrawRect( helpContainer.LocalRect, 6 );
			return false;
		};
		Layout.Add( helpContainer );

		var helpTitle = new Label( "Available Variables" );
		helpTitle.SetStyles( "font-weight: 600; font-size: 11px; color: rgba(255,255,255,0.7);" );
		helpContainer.Layout.Add( helpTitle );

		var variablesRow = helpContainer.Layout.AddRow();
		variablesRow.Spacing = 24;

		var col1 = variablesRow.AddColumn();
		col1.Spacing = 2;
		AddVariableLabel( col1, "{path}", "Full path to the file" );
		AddVariableLabel( col1, "{line}", "Line number (default: 1)" );

		var col2 = variablesRow.AddColumn();
		col2.Spacing = 2;
		AddVariableLabel( col2, "{column}", "Column number (default: 1)" );
		AddVariableLabel( col2, "{solution}", "Path to the solution" );
	}

	/// <summary>
	/// Adds a variable help label to a layout.
	/// </summary>
	private void AddVariableLabel( Layout layout, string variable, string description )
	{
		var row = layout.AddRow();
		row.Spacing = 8;

		var varLabel = new Label( variable );
		varLabel.SetStyles( "font-size: 11px; color: rgba(255,255,255,0.8); font-family: monospace; font-weight: 600;" );
		varLabel.MinimumWidth = 70;
		row.Add( varLabel );

		var descLabel = new Label( description );
		descLabel.SetStyles( "font-size: 11px; color: rgba(255,255,255,0.4);" );
		row.Add( descLabel );
	}

	/// <summary>
	/// Builds the bottom button row with Save, Cancel, and Delete buttons.
	/// </summary>
	private void BuildButtons()
	{
		var buttonRow = Layout.AddRow();
		buttonRow.Spacing = 8;

		if ( !_isNew )
		{
			var deleteBtn = new Button( "Delete" );
			deleteBtn.SetStyles( "color: #ff6b6b;" );
			deleteBtn.Clicked = OnDelete;
			buttonRow.Add( deleteBtn );
		}

		buttonRow.AddStretchCell();

		var cancelBtn = new Button( "Cancel" );
		cancelBtn.Clicked = Close;
		buttonRow.Add( cancelBtn );

		var saveBtn = new Button.Primary( _isNew ? "Add Editor" : "Save" );
		saveBtn.Clicked = OnSave;
		buttonRow.Add( saveBtn );
	}

	/// <summary>
	/// Validates and saves the editor configuration.
	/// </summary>
	private void OnSave()
	{
		if ( string.IsNullOrWhiteSpace( _nameInput.Text ) )
		{
			EditorUtility.DisplayDialog( "Validation Error", "Please enter a name for this editor." );
			return;
		}

		if ( string.IsNullOrWhiteSpace( _pathInput.Text ) || !System.IO.File.Exists( _pathInput.Text ) )
		{
			EditorUtility.DisplayDialog( "Validation Error", "Please select a valid executable file." );
			return;
		}

		// Ensure {path} is present in arguments, if not add it in at the start
		var args = _argsInput.Text;
		if ( !args.Contains( "{path}" ) )
		{
			args = $"\"{'{'}path{'}'}\" {args}".Trim();
			_argsInput.Text = args;
		}

		_config.Name = _nameInput.Text;
		_config.ExecutablePath = _pathInput.Text;
		_config.Arguments = _argsInput.Text;
		_config.SolutionArguments = _solutionArgsInput.Text;

		if ( _isNew )
		{
			CustomCodeEditorStorage.Add( _config );
		}
		else
		{
			CustomCodeEditorStorage.Update( _config );
		}

		CustomCodeEditorRegistry.RefreshInstances();
		CodeEditor.InvalidateCache();
		_onSaved?.Invoke();
		Close();
	}

	/// <summary>
	/// Prompts for confirmation and deletes the editor configuration.
	/// </summary>
	private void OnDelete()
	{
		EditorUtility.DisplayDialog(
			"Delete Custom Editor",
			$"Are you sure you want to delete '{_config.Name}'?",
			"Cancel",
			"Delete",
			() =>
			{
				CustomCodeEditorStorage.Remove( _config.Id );
				CustomCodeEditorRegistry.RefreshInstances();
				CodeEditor.InvalidateCache();
				_onSaved?.Invoke();
				Close();
			} );
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground );
		Paint.DrawRect( LocalRect, 0 );
	}
}
