namespace Editor;

internal sealed class ParentPackageUpdateToast : ToastWidget
{
	readonly Button.Primary updateButton;
	readonly Button dismissButton;
	bool isUpdating;

	public string PackageIdent { get; }
	public long RevisionId { get; private set; }

	ParentPackageUpdateToast( string packageIdent, long revisionId )
	{
		PackageIdent = packageIdent;
		Icon = "system_update_alt";
		DrawTimer = false;

		updateButton = new Button.Primary( "Update", "download", this )
		{
			FixedWidth = 112,
			FixedHeight = 28,
			Clicked = UpdatePackage
		};

		dismissButton = new Button( "Not Now", this )
		{
			FixedWidth = 92,
			FixedHeight = 28,
			Clicked = () => ToastManager.Dismiss( this )
		};

		SetAvailableRevision( revisionId );
	}

	void SetAvailableRevision( long revisionId )
	{
		if ( isUpdating )
			return;

		base.Reset();
		RevisionId = revisionId;
		Title = "Parent package update available";
		Subtitle = $"A new revision of {PackageIdent} is available. Update it without restarting the editor?";
		BorderColor = Theme.Primary;
		IsRunning = false;
		updateButton.Visible = true;
		updateButton.Enabled = true;
		updateButton.Text = "Update";
		updateButton.Icon = "download";
		dismissButton.Visible = true;
	}

	protected override Vector2 SizeHint() => new( 390, 118 );

	protected override void DoLayout()
	{
		base.DoLayout();

		const float margin = 16;
		var y = Height - updateButton.Height - margin;
		dismissButton.Position = new Vector2( Width - dismissButton.Width - margin, y );
		updateButton.Position = new Vector2( dismissButton.Position.x - updateButton.Width - 8, y );
	}

	async void UpdatePackage()
	{
		if ( isUpdating )
			return;

		isUpdating = true;
		IsRunning = true;
		Title = "Updating parent package";
		Subtitle = $"Downloading revision {RevisionId} of {PackageIdent}...";
		updateButton.Enabled = false;
		updateButton.Text = "Updating";
		updateButton.Icon = "sync";
		dismissButton.Visible = false;

		try
		{
			await EditorUtility.Projects.UpdateParentPackage( PackageIdent, RevisionId );

			if ( !IsValid() )
				return;

			isUpdating = false;
			IsRunning = false;
			Title = "Parent package updated";
			Subtitle = $"{PackageIdent} is now using revision {RevisionId}.";
			BorderColor = Theme.Green;
			updateButton.Visible = false;
			ToastManager.Remove( this, 4 );
		}
		catch ( System.Exception exception )
		{
			Log.Warning( exception, $"Unable to update parent package {PackageIdent}: {exception.Message}" );

			if ( !IsValid() )
				return;

			isUpdating = false;
			IsRunning = false;
			Title = "Parent package update failed";
			Subtitle = exception.Message;
			BorderColor = Theme.Red;
			updateButton.Visible = true;
			updateButton.Enabled = true;
			updateButton.Text = "Retry";
			updateButton.Icon = "refresh";
			dismissButton.Visible = true;
		}
	}

	[Event( "package.updated" )]
	public static void OnPackageUpdated( string packageIdent, long revisionId )
	{
		var project = Project.Current;
		var parentPackage = project?.Config.GetMetaOrDefault<string>( "ParentPackage", null );

		if ( project?.Config.Type != "addon" || !IsSamePackage( parentPackage, packageIdent ) )
			return;

		var existing = ToastManager.All
			.OfType<ParentPackageUpdateToast>()
			.FirstOrDefault( x => IsSamePackage( x.PackageIdent, packageIdent ) );

		if ( existing.IsValid() )
		{
			existing.SetAvailableRevision( revisionId );
			return;
		}

		_ = new ParentPackageUpdateToast( packageIdent, revisionId );
	}

	static bool IsSamePackage( string left, string right )
	{
		if ( !Package.TryParseIdent( left, out var leftParts ) || leftParts.local )
			return false;

		if ( !Package.TryParseIdent( right, out var rightParts ) || rightParts.local )
			return false;

		return string.Equals( leftParts.org, rightParts.org, System.StringComparison.OrdinalIgnoreCase )
			&& string.Equals( leftParts.package, rightParts.package, System.StringComparison.OrdinalIgnoreCase );
	}
}
