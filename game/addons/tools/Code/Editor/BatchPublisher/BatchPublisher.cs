namespace Editor;

using Sandbox;
using Sandbox.Utility;
using System;
using Editor.Wizards;

public class BatchPublisher : BaseWindow
{
	public static void FromAssets( Asset[] assets )
	{
		var p = new BatchPublisher( assets );
		p.Show();
	}

	public static void FromAssetsWithEnablePublish( Asset[] assets )
	{
		if ( assets.All( x => x.Publishing.Enabled ) )
		{
			FromAssets( assets );
			return;
		}

		var popup = new BatchMarkPublishedWidget( assets );
		popup.SetModal( true, true );
		popup.Hide();
		popup.Show();
	}

	public List<Asset> Assets { get; set; }

	public ScrollArea AssetList { get; set; }


	public Button PublishButton { get; set; }


	private BatchPublisher( IEnumerable<Asset> assets ) : base()
	{
		Size = new Vector2( 900, 670 );
		MinimumSize = Size;
		TranslucentBackground = true;
		NoSystemBackground = true;

		SetWindowIcon( "backup" );

		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 4;

		WindowTitle = $"Batch Publish v1.0";

		var row = Layout.AddRow();

		BuildHeader( row );

		AssetList = new ScrollArea( this );
		AssetList.MinimumWidth = 700;
		row.Add( AssetList, 100 );

		AssetList.Canvas = new Widget( AssetList );
		AssetList.Canvas.Layout = Layout.Column();
		AssetList.Canvas.Layout.Margin = new Sandbox.UI.Margin( 8, 8, 16, 8 );
		AssetList.Canvas.Layout.Spacing = 1;

		var controls = Layout.AddRow();
		controls.Margin = 16;
		controls.Spacing = 8;

		controls.AddStretchCell();
		//controls.Add( new Button( "Refresh" ) { Clicked = () => _ = RefreshList( assets ) } );

		PublishButton = controls.Add( new Button.Primary( "Publish" ) );
		PublishButton.Enabled = false;
		PublishButton.Clicked = () => _ = Publish();

		_ = RefreshList( assets );
	}

	void BuildHeader( Layout layout )
	{
		var header = layout.AddColumn();
		header.Margin = 16;
		header.Spacing = 8;

		header.Add( new Label.Subtitle( "Batch Publish" ) );
		header.Add( new Label( "If you're editing multiple published assets it can be a pain to publish each one. Here you can publish all changed assets in one go." ) { WordWrap = true } );

		header.Add( new Label( "You must own the rights to content you're publishing. Don't upload a bunch of content from someone else's game and write fair use like it's all fine - it will get removed." ) { WordWrap = true } );

		header.Add( new Label( "You should visit <a href=\"https://sbox.game\">sbox.game</a> to fill in additional details, like description, tags and visibility. We won't change any of that here." ) { WordWrap = true } );

		header.AddStretchCell();

		header.Add( new Label( "Pay particular attention to the ident (in green) before first publish - you won't be able to edit this afterwards." ) { WordWrap = true } );
	}

	private async Task RefreshList( IEnumerable<Asset> assets )
	{
		EditorUtility.ClearPackageCache();

		AssetList.Canvas.Layout.Clear( true );

		Assets ??= new();
		Assets.Clear();

		foreach ( var asset in assets )
		{
			if ( !asset.Publishing.Enabled )
				continue;

			Assets.Add( asset );

			var row = new AssetRow( this, asset );

			AssetList.Canvas.Layout.Add( row, 0 );
		}

		AssetList.Canvas.Layout.AddStretchCell();

		var t = new List<Task>();

		foreach ( var a in AssetList.Canvas.Children.OfType<AssetRow>().OrderBy( x => Guid.NewGuid() ) )
		{
			if ( !a.IsValid ) continue;

			t.Add( a.RefreshStatus() );

			while ( t.Count > 32 )
			{
				await Task.WhenAny( t );
				t.RemoveAll( x => x.IsCompleted );
			}
		}

		await Task.WhenAll( t );

		var changed = AssetList.Canvas.Children.OfType<AssetRow>().Where( x => x.IsValid ).Count( x => x.NeedsUpload );
		if ( changed > 0 )
		{
			PublishButton.Text = $"Publish {changed} Assets";
			PublishButton.Enabled = true;
		}
		else
		{
			PublishButton.Text = $"No Changes Detected";
			PublishButton.Enabled = false;
		}
	}

	async Task Publish()
	{
		Enabled = false;

		HideCompleted();

		var t = new List<Task>();

		foreach ( var a in AssetList.Canvas.Children.OfType<AssetRow>() )
		{
			if ( !IsValid || !Visible ) return;

			t.Add( a.Publish() );

			while ( t.Count > 8 )
			{
				await Task.WhenAny( t );
				t.RemoveAll( x => x.IsCompleted );
				HideCompleted();
			}
		}

		await Task.WhenAll( t );

		//await RefreshList();
		Enabled = true;
	}

	void HideCompleted()
	{
		foreach ( var a in AssetList.Canvas.Children.OfType<AssetRow>() )
		{
			if ( !a.NeedsUpload )
				a.Visible = false;
		}
	}


}

file class AssetRow : Widget
{
	Asset Asset { get; init; }

	string _status;

	string Status
	{
		get => _status;
		set
		{
			if ( _status == value ) return;
			_status = value;
			Update();
		}
	}

	public bool NeedsUpload { get; set; }

	public AssetRow( Widget parent, Asset asset ) : base( parent )
	{
		Height = 25;
		MinimumHeight = 25;

		Asset = asset;

		Layout = Layout.Row();
		Layout.Spacing = 4;

		RefreshRow();
	}

	void RefreshRow()
	{
		Layout.Clear( true );
		Layout.AddStretchCell();
		Layout.Add( new ToolButton( "", "drive_file_move", this ) { FixedHeight = 25, FixedWidth = 25, ToolTip = "Inspect Asset", MouseClick = InspectAsset } );
		Layout.Add( new ToolButton( "", "edit", this ) { FixedHeight = 25, FixedWidth = 25, ToolTip = "Asset Properties", MouseClick = EditAsset } );
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect );

		var fg = Color.White;

		Paint.SetPen( Theme.Green );
		var rect = LocalRect.Shrink( 8, 0 );
		var r = Paint.DrawText( rect, $"{Asset.Publishing.ProjectConfig.FullIdent}", TextFlag.LeftCenter );

		Paint.SetPen( fg.WithAlphaMultiplied( 0.5f ) );
		rect.Left = r.Right + 8;
		r = Paint.DrawText( rect, $"{Asset.Path}", TextFlag.LeftCenter );

		Paint.SetPen( fg.WithAlphaMultiplied( 0.5f ) );
		rect.Left = r.Right + 8;
		rect.Right -= 70;
		//r = Paint.DrawTextBox( rect, $"{Status}", Theme.Black, new Sandbox.UI.Margin( 4, 2 ), 4, TextFlag.RightCenter );
		r = Paint.DrawText( rect, $"{Status}", TextFlag.RightCenter );
	}

	void InspectAsset()
	{
		EditorUtility.InspectorObject = Asset;
	}

	void EditAsset()
	{
		var addon = Asset.Publishing.CreateTemporaryProject();
		addon.Config.Org = Project.Current.Config.Org;

		ProjectSettingsWindow.OpenForProject( addon );
	}

	public async Task RefreshStatus()
	{
		if ( Asset.Publishing.ProjectConfig.Org != Project.Current.Config.Org && Asset.Publishing.ProjectConfig.Org == "local" )
		{
			Log.Info( $"Changing org to {Project.Current.Config.Org}" );
			Asset.Publishing.ProjectConfig.Org = Project.Current.Config.Org;
			Asset.Publishing.Save();
		}

		SetEffectOpacity( 1.0f );
		NeedsUpload = true;

		var package = await Package.FetchAsync( Asset.Publishing.ProjectConfig.FullIdent, false );
		if ( package is null )
		{
			Status = "Doesn't Exist";
			return;
		}

		if ( package.Revision is null )
		{
			Status = "No Revision";
			return;
		}

		await package.Revision.DownloadManifestAsync();

		var schema = package.Revision.Manifest;
		if ( schema is null || schema.Files is null )
		{
			Status = "Invalid Revision";
			return;
		}

		var files = Asset.GetReferences( true );
		files.Add( Asset );
		foreach ( var file in files )
		{
			if ( !ProjectPublisher.CanPublishFile( file ) ) continue;

			var compileFileAbsPath = file.GetCompiledFile( true );
			var compileFilePath = file.GetCompiledFile( false );

			var existingFile = schema.Files.FirstOrDefault( x => x.Path == compileFilePath );
			if ( existingFile.Path is null )
			{
				Status = $"Changed";
				return;
			}

			var fileInfo = new System.IO.FileInfo( compileFileAbsPath );

			// size the same?
			if ( existingFile.Size != fileInfo.Length )
			{
				Status = $"Changed";
				return;
			}

			using var reader = fileInfo.OpenRead();
			var crc = await Crc64.FromStreamAsync( reader );

			// checksum the same?
			if ( existingFile.Crc != crc.ToString( "x" ) )
			{
				Status = $"Changed";
				return;
			}

		}

		Status = "";
		SetEffectOpacity( 0.3f );
		NeedsUpload = false;
	}

	public async Task Publish()
	{
		if ( !NeedsUpload )
			return;

		Status = "Scanning";

		var upload = await ProjectPublisher.FromAsset( Asset );

		try
		{
			Status = "Checking Files";
			await upload.PrePublish();

			if ( upload.MissingFileCount > 0 )
			{
				upload.OnProgressChanged = () => Status = $"{(upload.MissingFileSize).FormatBytes()}";
				await upload.UploadFiles();
				upload.OnProgressChanged = null;
			}

			Status = "Publishing";
			await upload.Publish();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Error when publising: {e.Message}" );
			await RefreshStatus();
			return;
		}

		Status = "Video";

		await PublishWizard.UploadMediaPage.CreateAndUploadVideo( Asset, s => Status = s );

		Status = "Completed";

		EditorUtility.ClearPackageCache();

		await RefreshStatus();
	}

}
