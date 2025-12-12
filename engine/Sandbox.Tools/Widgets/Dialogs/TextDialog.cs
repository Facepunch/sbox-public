using System;

namespace Editor;

public partial class Dialog : Widget
{
	/// <summary>
	/// Ask for a string which is intended to be a new folder name.. which means it shouldn't have / or " or : etc.
	/// </summary>
	public static void AskStringFolder( Action<string> OnSuccess, string question, string okay = "Okay", string cancel = "Cancel", string initialName = "" )
	{
		var modal = new TextDialog();
		modal.Window.SetWindowIcon( "folder" );
		modal.Window.Title = "Select a folder";
		modal.Window.Size = new Vector2( 400, 100 );
		modal.Label.Text = question;
		modal.OkayButton.Text = okay;
		modal.CancelButton.Text = cancel;
		modal.OnSuccess = OnSuccess;
		modal.Show();
		modal.LineEdit.Text = initialName;
		modal.LineEdit.Focus();
		modal.LineEdit.SelectAll();
	}

	/// <summary>
	/// Ask for a string which is intended to be a new file name.. which means it shouldn't have / or " or : etc.
	/// </summary>
	public static void AskStringFile( Action<string> OnSuccess, string question, string okay = "Okay", string cancel = "Cancel", string initialName = "" )
	{
		var modal = new TextDialog();
		modal.Window.SetWindowIcon( "description" );
		modal.Window.Title = "Select a file";
		modal.Window.Size = new Vector2( 400, 100 );
		modal.Label.Text = question;
		modal.OkayButton.Text = okay;
		modal.CancelButton.Text = cancel;
		modal.OnSuccess = OnSuccess;
		modal.Show();
		modal.LineEdit.Text = initialName;
		modal.LineEdit.Focus();
		modal.LineEdit.SelectAll();
	}

	/// <summary>
	/// Ask for a string
	/// </summary>
	public static void AskString( Action<string> OnSuccess, string question, string okay = "Okay", string cancel = "Cancel", string initialName = "", string title = "Input required", int minLength = 0 )
	{
		var modal = new TextDialog();
		modal.Window.SetWindowIcon( "question_mark" );
		modal.Window.Title = title;
		modal.Window.Size = new Vector2( 400, 100 );
		modal.Label.Text = question;
		modal.OkayButton.Text = okay;
		modal.OkayButton.Enabled = !string.IsNullOrWhiteSpace( initialName );
		modal.CancelButton.Text = cancel;
		modal.MinLength = minLength;
		modal.OnSuccess = OnSuccess;
		modal.Show();
		modal.LineEdit.Text = initialName;
		modal.LineEdit.Focus();
		modal.LineEdit.SelectAll();
	}

	/// <summary>
	/// Ask for a confirmation
	/// </summary>
	public static void AskConfirm( Action OnSuccess, string question, string title = "Confirmation", string okay = "Okay", string cancel = "Cancel" )
	{
		var modal = new TextDialog( false );
		modal.Window.SetWindowIcon( "question_mark" );
		modal.Window.Title = title;
		modal.Window.Size = new Vector2( 400, 100 );
		modal.Label.Text = question;
		modal.OkayButton.Text = okay;
		modal.CancelButton.Text = cancel;
		modal.OnSuccess = ( _ ) => OnSuccess();
		modal.Show();
	}

	/// <summary>
	/// Ask for a confirmation
	/// </summary>
	public static void AskConfirm( Action OnSuccess, Action OnCancel, string question, string title = "Confirmation", string okay = "Okay", string cancel = "Cancel" )
	{
		var modal = new TextDialog( false );
		modal.Window.SetWindowIcon( "question_mark" );
		modal.Window.Title = title;
		modal.Window.Size = new Vector2( 400, 100 );
		modal.Label.Text = question;
		modal.OkayButton.Text = okay;
		modal.CancelButton.Text = cancel;
		modal.OnSuccess = ( _ ) => OnSuccess();
		modal.OnCancel = ( _ ) => OnCancel();
		modal.Show();
	}

	/// <summary>
	/// A wrapper to more easily create dialog windows
	/// </summary>
	internal class TextDialog : Dialog
	{
		public Action<string> OnSuccess;
		public Action<string> OnCancel;

		public Button OkayButton { get; private set; }
		public Button CancelButton { get; private set; }
		public LineEdit LineEdit { get; private set; }
		public Label Label { get; private set; }

		public int MinLength { get; set; }

		bool WantsText = true;

		public TextDialog( bool wantsText = true )
		{
			Window.SetModal( true, true );

			Label = new Label( this );
			Label.WordWrap = true;
			Label.SetSizeMode( SizeMode.Default, SizeMode.Expand );

			LineEdit = new LineEdit( this );
			if ( wantsText )
			{
				LineEdit.TextEdited += x => Validate();
				LineEdit.ReturnPressed += Finish;
			}
			else
			{
				LineEdit.Visible = false;
				WantsText = false;
			}

			OkayButton = new Button( "Okay", this );
			OkayButton.SetProperty( "type", "primary" );
			OkayButton.Clicked = Okay;

			CancelButton = new Button( "Cancel", this );
			CancelButton.Clicked = Cancel;

			Layout = Layout.Column();

			var content = Layout.AddColumn();
			content.Margin = 16;
			content.Add( Label );
			if ( wantsText )
			{
				content.AddSpacingCell( 16 );
				content.Add( LineEdit );
			}

			var footer = Layout.AddRow();
			footer.Margin = 16;
			footer.Spacing = 8;
			footer.AddStretchCell();
			footer.Add( OkayButton );
			footer.Add( CancelButton );

			Validate();
		}

		/// <summary>
		/// Called when text changes to revalidate the input, disable Okay button, etc.
		/// </summary>
		protected virtual void Validate()
		{
			var valid = true;
			if ( string.IsNullOrWhiteSpace( LineEdit.Text ) ) valid = false;
			if ( LineEdit.Text.Trim().Length < MinLength ) valid = false;
			if ( !WantsText ) valid = true;

			OkayButton.Enabled = valid;
		}

		/// <summary>
		/// Called when the user presses the Okay button
		/// </summary>
		protected virtual void Okay()
		{
			OnSuccess?.Invoke( LineEdit.Text );
			Close();
		}

		/// <summary>
		/// Called when the user presses the Cancel button
		/// </summary>
		protected virtual void Cancel()
		{
			OnCancel?.Invoke( LineEdit.Text );
			Close();
		}

		/// <summary>
		/// Called when the user presses Enter in the text field
		/// </summary>
		protected virtual void Finish()
		{
			Validate();

			if ( !OkayButton.Enabled )
				return;

			Okay();
		}
	}

}
