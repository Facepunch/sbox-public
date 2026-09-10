using System.Threading;

namespace Editor.Wizards;

partial class PublishWizard
{
	/// <summary>
	/// Look for files, upload missing
	/// </summary>
	class UploadWizardPage : PublishWizardPage
	{
		public override string PageTitle => "File Uploads";
		public override string PageSubtitle => "Here's a list of files that we have determined are needed by your package. Some may have already been uploaded in the past.";

		ListView Pending;
		ListView Uploaded;
		FileUsageWidget UsageWidget;

		ProjectPublisher Publisher;

		LineEdit UploadedFilter { get; set; }
		LineEdit StagingFilter { get; set; }

		public override async Task OpenAsync()
		{
			Enabled = false;
			Visible = true;
			Rebuild();

			await Refresh();

			Enabled = true;
		}

		public override void Rebuild()
		{
			var stagingFilter = StagingFilter.IsValid() ? StagingFilter.Text : "";
			var uploadedFilter = UploadedFilter.IsValid() ? UploadedFilter.Text : "";

			BodyLayout.Clear( true );

			var row = Layout.Row();
			row.Spacing = 8;
			BodyLayout.Add( row );

			var left = row.AddColumn();
			left.Add( new Label( "Pending Upload" ) );
			left.Spacing = 8;
			StagingFilter = left.Add( new LineEdit( null ) { Text = stagingFilter } );
			StagingFilter.PlaceholderText = "Filter...";
			StagingFilter.TextChanged += ( string x ) => UpdateFileList();
			Pending = new( null );
			Pending.ItemSize = new Vector2( 0, 18 );
			Pending.OnPaintOverride = () => { Paint.ClearPen(); Paint.SetBrush( Theme.ControlBackground ); Paint.DrawRect( Pending.LocalRect, Theme.ControlRadius ); return false; };
			Pending.ItemPaint = PaintFile;
			left.Add( Pending, 1 );

			var right = row.AddColumn();
			right.Add( new Label( "Uploaded" ) );
			UploadedFilter = right.Add( new LineEdit( null ) { Text = uploadedFilter } );
			UploadedFilter.PlaceholderText = "Filter...";
			UploadedFilter.TextChanged += ( string x ) => UpdateFileList();

			right.Spacing = 8;
			Uploaded = new( null );
			Uploaded.ItemSize = new Vector2( 0, 18 );
			Uploaded.OnPaintOverride = () => { Paint.ClearPen(); Paint.SetBrush( Theme.ControlBackground ); Paint.DrawRect( Pending.LocalRect, Theme.ControlRadius ); return false; };
			Uploaded.ItemPaint = PaintFile;
			right.Add( Uploaded, 1 );

			BodyLayout.AddSpacingCell( 16 );

			var bottom = Layout.Column();
			bottom.Spacing = 8;

			{
				var layout = Layout.Column();
				UsageWidget = layout.Add( new FileUsageWidget() );
				bottom.Add( layout );
			}

			BodyLayout.Add( bottom );

			UpdateFileList();
		}

		void PaintFile( VirtualWidget item )
		{
			var file = item.Object as ProjectPublisher.ProjectFile;
			var fileSize = $"{file.Size.FormatBytes()}";

			var path = System.IO.Path.GetDirectoryName( file.Name );
			var filename = System.IO.Path.GetFileName( file.Name );

			path = $"{path.Replace( '\\', '/' ).Trim( '/' )}/";

			Paint.SetPen( Theme.TextControl );

			if ( file.SizeUploaded > 0 && !file.Skip )
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.Green.Darken( 0.3f ).WithAlpha( 0.2f ) );
				float delta = (float)file.SizeUploaded / (float)file.Size;
				var bg = item.Rect;
				bg.Right = bg.Left + (bg.Width * delta);

				Paint.DrawRect( bg );

				Paint.SetPen( Theme.Green );
				fileSize = $"{file.SizeUploaded.FormatBytes( true )}/{file.Size.FormatBytes( true )}";
			}

			var c = Theme.TextControl;

			if ( item.Hovered ) c = Color.White;
			if ( item.Selected ) c = Theme.Primary;

			var r = item.Rect.Shrink( 6, 0 );

			var size = Paint.DrawText( r, fileSize, TextFlag.RightCenter | TextFlag.SingleLine );

			r.Right = size.Left - 8;

			Paint.SetPen( c.WithAlpha( 0.5f ) );
			var p = Paint.DrawText( r, $"{path}", TextFlag.LeftCenter | TextFlag.SingleLine );

			Paint.SetPen( c );
			r.Left = p.Right;
			Paint.DrawText( r, $"{filename}", TextFlag.LeftCenter | TextFlag.SingleLine );
		}

		public async Task Refresh()
		{
			Publisher = await ProjectPublisher.FromProject( Project );

			if ( Project.Config.Type == "game" )
			{
				var settings = Publisher.GetGameSettings( PublishConfig.CompilerOutput );
				Publisher.SetMeta( "GameSettings", settings );
			}

			if ( PublishConfig.AssemblyFiles is not null )
			{
				foreach ( var f in PublishConfig.AssemblyFiles )
				{
					if ( f.Value is byte[] bytes )
					{
						await Publisher.AddFile( bytes, f.Key );
					}

					if ( f.Value is string json )
					{
						await Publisher.AddFile( json, f.Key );
					}
				}
			}

			// Add assets from Cloud.Model( .. ) etc
			foreach ( var package in PublishConfig.CodePackages )
			{
				await Publisher.AddCodePackageReference( package );
			}

			UpdateFileList();

			await Publisher.PrePublish();

			UpdateFileList();
		}

		public void UpdateFileList()
		{
			Pending.Clear();
			Uploaded.Clear();
			UsageWidget.Clear();

			if ( Publisher is null ) return;

			var files = Publisher.Files;
			files = files.OrderByDescending( x => x.Size );

			foreach ( var entry in files )
			{
				// File is empty, don't upload it
				if ( entry.Size < 1 ) continue;

				UsageWidget.AddFile( entry );
				if ( !entry.Skip )
				{
					if ( StagingFilter.IsValid() && !string.IsNullOrEmpty( StagingFilter.Text ) && !entry.Name.Contains( StagingFilter.Text, StringComparison.OrdinalIgnoreCase ) ) continue;
					Pending.AddItem( entry );
				}
				else
				{
					if ( UploadedFilter.IsValid() && !string.IsNullOrEmpty( UploadedFilter.Text ) && !entry.Name.Contains( UploadedFilter.Text, StringComparison.OrdinalIgnoreCase ) ) continue;
					Uploaded.AddItem( entry );
				}
			}
		}

		public override async Task<bool> FinishAsync()
		{
			await RunUploads();

			PublishConfig.Publisher = Publisher;
			var notUploaded = Publisher.Files.Where( x => x.Skip == false ).ToArray();

			if ( notUploaded.Length > 0 )
			{
				Log.Warning( "Not all uploaded? Some uploads failed?" );

				foreach ( var file in notUploaded )
				{
					Log.Warning( $"File: {file.Name} {file.Skip}" );
				}
			}

			// all should be uploaded
			return notUploaded.Length == 0;
		}

		async Task RunUploads()
		{
			var token = TokenSource.Token;
			var uploads = Publisher.Files.Where( x => !x.Skip ).ToArray();

			var tasks = new List<Task>();

			foreach ( var upload in uploads )
			{
				if ( token.IsCancellationRequested ) break;

				var t = UploadFile( upload, token );

				tasks.Add( t );

				//
				// max 8 uploads at the same time then wait for one to complete
				//
				while ( tasks.Count > 8 )
				{
					await Task.WhenAny( tasks.ToArray() );
					tasks.RemoveAll( x => x.IsCompleted );
				}
			}

			// Drain active uploads before the wizard can restart and replace their publisher/UI.
			await Task.WhenAll( tasks.ToArray() );
			token.ThrowIfCancellationRequested();
		}

		void FileUploadProgress( ProjectPublisher.ProjectFile file, Sandbox.Utility.DataProgress progress )
		{
			file.SizeUploaded = progress.ProgressBytes;
			Pending.Dirty( file );
		}

		async Task UploadFile( ProjectPublisher.ProjectFile file, CancellationToken token = default )
		{
			file.SizeUploaded = 1;
			Pending.Dirty( file );

			if ( file.Contents is not null )
			{
				var r = await Project.Package.UploadFile( file.Contents, file.Name, p => FileUploadProgress( file, p ), token );
				if ( r ) file.Skip = true;
			}
			else if ( file.AbsolutePath is not null )
			{
				var r = await Project.Package.UploadFile( file.AbsolutePath, file.Name, p => FileUploadProgress( file, p ), token );
				if ( r ) file.Skip = true;
			}
			else
			{
				Log.Warning( $"Unable to upload {file.Name} - has no content defined!" );
			}

			UpdateFileList();
		}

		public override string NextButtonText
		{
			get
			{
				var pendingFiles = Publisher?.Files?.Count( x => !x.Skip );

				if ( pendingFiles > 1 )
					return "Upload Files";


				if ( pendingFiles == 1 )
					return "Upload File";


				return base.NextButtonText;
			}
		}

		public override bool CanProceed()
		{
			// we need SOME files to upload, even if they've already been uploaded
			// having no files here indicates an error.
			if ( Publisher.Files.Count() == 0 ) return false;

			return true;
		}
	}
}

sealed class FileUsageWidget : Widget
{
	List<ProjectPublisher.ProjectFile> Files = new();
	Dictionary<string, double> Usage = new();
	Dictionary<string, int> FileCount = new();
	double TotalUsage => Usage.Values.Sum();
	int TotalFiles => Files.Count;

	string HoveringKey = "";

	public FileUsageWidget()
	{
		Layout = Layout.Column();
		FixedHeight = 52;
		HorizontalSizeMode = SizeMode.CanGrow;
		MouseTracking = true;
	}

	public void AddFile( ProjectPublisher.ProjectFile file )
	{
		Files.Add( file );

		string extension = System.IO.Path.GetExtension( file.Name ).TrimStart( '.' );
		if ( extension.EndsWith( "_c" ) ) extension = extension.Remove( extension.Length - 2 );

		if ( !Usage.ContainsKey( extension ) ) Usage[extension] = 0;
		Usage[extension] += file.Size;

		if ( !FileCount.ContainsKey( extension ) ) FileCount[extension] = 0;
		FileCount[extension]++;

		Usage = Usage.OrderBy( x => x.Value ).ToDictionary( x => x.Key, x => x.Value );
	}

	protected override void OnPaint()
	{
		// Draw Bar
		Paint.SetBrush( Theme.ControlBackground );
		Paint.SetPen( Theme.ControlBackground );
		Paint.DrawRect( new Rect( 0, 0, Width, 32 ) );

		var totalUsage = TotalUsage;
		if ( totalUsage == 0 ) return;

		float x = 0;
		foreach ( var kv in Usage )
		{
			var w = ((float)kv.Value / (float)totalUsage) * Width;
			Paint.ClearPen();
			Paint.SetPen( GetColor( kv.Key ) ?? Color.White, 2 );
			Paint.SetBrush( GetColor( kv.Key ) ?? Color.White );
			var rect = new Rect( x, 0, w, 32 );
			Paint.DrawRect( rect );

			x += w;
		}

		if ( string.IsNullOrEmpty( HoveringKey ) )
		{
			Paint.SetPen( Theme.TextControl );
			Paint.DrawText( new Vector2( 2, 34 ), $"{TotalFiles} Files. {TotalUsage.FormatBytes().ToUpper()} Total." );
		}
		else
		{
			Paint.SetPen( GetColor( HoveringKey ) ?? Theme.TextControl );
			Paint.DrawText( new Vector2( 2, 34 ), $"[{HoveringKey}]: {FileCount[HoveringKey]} Files. {Usage[HoveringKey].FormatBytes().ToUpper()} total. {(Usage[HoveringKey] / (float)totalUsage * 100f).ToString( "0.##" )}%." );
		}
	}

	Color? GetColor( string key )
	{
		var asset = AssetType.FromExtension( key );
		if ( asset == null )
			return Color.Gray;

		return asset.Color;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );

		string wasHovering = HoveringKey;

		HoveringKey = "";

		float x = 0;
		foreach ( var kv in Usage )
		{
			var w = ((float)(kv.Value / TotalUsage)) * Width;
			var rect = new Rect( x, 0, w, 32 );
			// Check if mouse is in rect
			if ( rect.IsInside( e.LocalPosition ) )
			{
				HoveringKey = kv.Key;
			}

			x += w;
		}

		if ( HoveringKey != wasHovering )
		{
			Update();
		}
	}

	internal void Clear()
	{
		Files.Clear();
		Usage.Clear();
		FileCount.Clear();
		HoveringKey = "";
	}
}
