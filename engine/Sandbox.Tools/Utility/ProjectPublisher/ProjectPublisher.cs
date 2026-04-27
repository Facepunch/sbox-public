using Sandbox.DataModel;
using Sandbox.Services;
using System;
using System.Text.Json;
using System.Threading;

namespace Editor;

public partial class ProjectPublisher
{

	public string TargetPackageIdent => Project.Config.FullIdent;

	Project Project;
	public PackageManifest Manifest { get; protected set; }

	Dictionary<string, object> Meta { get; set; } = new();

	public int TotalFileCount => Manifest.Assets.Count();
	public int MissingFileCount => Manifest.Assets.Where( x => !x.Skip ).Count();
	public long MissingFileSize => Manifest.Assets.Sum( x => x.Size - x.SizeUploaded );

	public Action OnProgressChanged { get; set; }

	public void SetMeta( string key, object obj )
	{
		Meta[key] = obj;
	}

	public static async Task<ProjectPublisher> FromAsset( Asset asset )
	{
		await asset.CompileIfNeededAsync( 60 );

		if ( asset.Publishing is null )
			return null;

		var fakeProject = asset.Publishing.CreateTemporaryProject();

		var p = new ProjectPublisher( fakeProject );
		// p.Manifest.IncludeSourceFiles = asset.Publishing.ProjectConfig.IncludeSourceFiles;
		p.Manifest.IncludeSourceFiles = false; // tony: Disabled this until we implement it in a better way
		p.SetMetaFromAsset( asset );
		await p.Manifest.BuildFrom( asset );

		// include thumbnail!
		var thumb = await asset.RenderThumb();
		if ( thumb is not null )
		{
			await p.AddFile( thumb.GetPng(), $"/{p.TargetPackageIdent}/thumb.png" );
		}

		return p;
	}

	public static async Task<ProjectPublisher> FromProject( Project project )
	{
		// library
		if ( project.IsSourcePublish() )
		{
			var p = new ProjectPublisher( project );
			await p.GenerateSourceManifest();
			return p;
		}

		if ( project.ProjectSourceObject is Asset asset )
		{
			return await FromAsset( asset );
		}

		// game
		{
			var p = new ProjectPublisher( project );
			await p.GenerateAssetManifest();
			return p;
		}
	}

	/// <summary>
	/// Fetch a list of game settings to be added to the project's metadata
	/// </summary>
	/// <param name="assemblies"></param>
	/// <returns></returns>
	public List<GameSetting> GetGameSettings( CompilerOutput[] assemblies )
	{
		var library = EditorUtility.CreateTypeLibrary( assemblies );
		var vars = library.GetMembersWithAttribute<ConVarAttribute>()
			.Where( x => x.Attribute is not ConCmdAttribute && x.Attribute.Flags.HasFlag( ConVarFlags.GameSetting ) );

		var list = new List<GameSetting>();

		foreach ( var cvar in vars )
		{
			var e = new GameSetting( cvar.Attribute.Name, cvar.Member.Title, cvar.Member.Group );

			if ( e.Min != 0f )
			{
				e.Min = cvar.Attribute.Min;
			}

			if ( e.Max != 0f )
			{
				e.Max = cvar.Attribute.Max;
			}

			// Ranged values (min, max, step size)
			var clampAttribute = cvar.Member.Attributes.OfType<RangeAttribute>().FirstOrDefault();
			if ( clampAttribute is not null )
			{
				e.Min = clampAttribute.Min;
				e.Max = clampAttribute.Max;
			}

			var stepAttribute = cvar.Member.Attributes.OfType<StepAttribute>().FirstOrDefault();
			if ( stepAttribute is not null )
			{
				e.Step = stepAttribute.Step;
			}

			// Default values
			if ( cvar.Member.GetCustomAttribute<DefaultValueAttribute>() is { } defaultAttribute )
			{
				e.Default = defaultAttribute.Value.ToString();
			}
			else
			{
				e.Default = "0";
			}

			// Enum values (for dropdown properties)
			if ( cvar.Member is PropertyDescription prop && prop.PropertyType.IsEnum )
			{
				e.Options = new();
				foreach ( var entry in library.GetEnumDescription( prop.PropertyType ) )
				{
					e.Options.Add( new GameSetting.Option( entry.Name, entry.Icon ) );
				}
			}

			list.Add( e );
		}

		return list;
	}

	ProjectPublisher( Project project )
	{
		Project = project;
		Manifest = new PackageManifest();

		//
		// Copy stored metadata over
		//
		project.Config.Metadata ??= new();
		foreach ( var e in project.Config.Metadata )
		{
			Meta[e.Key] = e.Value;
		}

		Meta["Resources"] = project.Config.Resources ?? "";
	}

	/// <summary>
	/// Build a list of files to upload for this version. Return false if errors that would prevent the upload.
	/// </summary>
	async Task GenerateAssetManifest( IProgress progress = null, CancellationToken cancel = default )
	{
		await Manifest.BuildFromAssets( Project, progress, cancel );
	}

	/// <summary>
	/// Build a list of files to upload for this version. Return false if errors that would prevent the upload.
	/// </summary>
	async Task GenerateSourceManifest( IProgress progress = null, CancellationToken cancel = default )
	{
		await Manifest.BuildFromSource( Project, progress, cancel );
	}

	void FinishAddingFiles()
	{
		Meta["CodePackageReferences"] = Manifest.CodePackageReferences.ToArray();
	}

	/// <summary>
	/// Publish a new revision
	/// </summary>
	public async Task Publish( IProgress progress = null, CancellationToken cancel = default )
	{
		if ( Project.Config.IsStandaloneOnly )
			return;

		await TryWorkshopUpload();

		await PostManifest( Manifest, cancel );
	}

	/// <summary>
	/// Check the intended manifest, ask the backend which files need to be uploaded.
	/// </summary>
	public async Task PrePublish( CancellationToken cancellationToken = default )
	{
		FinishAddingFiles();

		if ( Project.Config.IsStandaloneOnly )
			return;

		if ( Manifest.Assets.Count == 0 )
		{
			return;
		}

		//
		// lowercase all relative paths if we're not a source project
		//
		if ( !Project.IsSourcePublish() )
		{
			foreach ( var file in Manifest.Assets )
			{
				file.Name = file.Name.ToLowerInvariant();
			}
		}

		var publish = new PublishManifest
		{
			Assets = Manifest.Assets.Select( x => new ManifestFile( x.Name, x.Size, x.Hash ) ).ToArray(),
			Title = "",
			Description = "",
			Publish = false,
			Meta = Meta.Count > 0 ? JsonSerializer.Serialize( Meta ) : null,
			EngineApi = Sandbox.Engine.Protocol.Api
		};

		publish.Config = new ManifestConfig
		{
			Title = Project.Config.Title,
			Type = Project.Config.Type,
			Org = Project.Config.Org,
			Ident = Project.Config.Ident,
			Schema = Project.Config.Schema,
			PackageReferences = Project.Config.PackageReferences?.ToList() ?? new(),
			EditorReferences = Project.Config.EditorReferences?.ToList() ?? new(),
		};

		PublishManifestResult result = default;

		try
		{
			result = await Backend.Package.PublishManifest( publish );
		}
		catch ( Refit.ApiException e )
		{
			Log.Warning( $"PublishManifest: {e.StatusCode} - {e.Content}" );
			return;
		}
		catch ( InvalidOperationException e )
		{
			//await Progress.StatementAsync( "Manifest Problem", $"Backend reported: " + ex.Message );
			Log.Warning( e, $"Project publish preparation failed for {publish.Config.Type}" );
			throw;
		}

		if ( result.Status is null )
		{
			Log.Warning( "Addon upload preparation failed: API Error." );
			throw new System.Exception( "Api Error, no result" );
		}

		if ( result.Status == "OK" )
		{
			for ( int i = 0; i < Manifest.Assets.Count; i++ )
			{
				var e = Manifest.Assets[i];
				e.Skip = true;
				e.SizeUploaded = e.Size;
				Manifest.Assets[i] = e;
			}

			return;
		}

		//
		// It wants some files
		//
		if ( result.Status == "upload" )
		{
			List<ProjectFile> downloadList = new();

			for ( int i = 0; i < Manifest.Assets.Count; i++ )
			{
				Manifest.Assets[i].Skip = true;
				Manifest.Assets[i].SizeUploaded = Manifest.Assets[i].Size;
			}

			foreach ( var file in result.Files )
			{
				var projectFile = Manifest.Assets.Where( x => string.Equals( x.Name, file, StringComparison.OrdinalIgnoreCase ) ).FirstOrDefault();
				if ( projectFile is null )
				{
					Log.Warning( $"Upload failed - couldn't find local file '{file}'!" );
					continue;
				}

				// upload this file!
				projectFile.Skip = false;
				projectFile.SizeUploaded = 0;
			}

			return;
		}

		throw new System.Exception( $"Unhandled status: {result}" );
	}

	async Task<bool> PostManifest( PackageManifest manifest, CancellationToken cancellationToken )
	{
		if ( Project.Config.IsStandaloneOnly )
			return false;

		if ( manifest.Assets.Count == 0 )
			throw new System.ArgumentException( "No files" );

		var publish = new PublishManifest
		{
			Assets = Manifest.Assets.Select( x => new ManifestFile( x.Name, x.Size, x.Hash ) ).ToArray(),
			Title = string.IsNullOrWhiteSpace( manifest.Summary ) ? "Untitled Version" : manifest.Summary,
			Description = manifest.Description,
			Publish = true,
			Meta = Meta.Count > 0 ? Json.Serialize( Meta ) : null,
			EngineApi = Sandbox.Engine.Protocol.Api
		};

		publish.Config = new ManifestConfig
		{
			Title = Project.Config.Title,
			Type = Project.Config.Type,
			Org = Project.Config.Org,
			Ident = Project.Config.Ident,
			Schema = Project.Config.Schema,
			PackageReferences = Project.Config.PackageReferences?.ToList() ?? new(),
			EditorReferences = Project.Config.EditorReferences?.ToList() ?? new(),
		};

		PublishManifestResult result = default;

		try
		{
			result = await Backend.Package.PublishManifest( publish );
		}
		catch ( Refit.ApiException e )
		{
			Log.Warning( $"PublishManifest: {e.StatusCode} - {e.Content}" );
			return false;
		}
		catch ( InvalidOperationException )
		{
			//await Progress.StatementAsync( "Manifest Problem", $"Backend reported: " + ex.Message );
			return false;
		}

		if ( result.Status is null )
		{
			Log.Error( $"Api Error" );
			return false;
		}

		if ( result.Status == "OK" )
		{
			return true;
		}

		Log.Error( $"Unhandled status: {result.Status}" );

		if ( result.Files is not null )
		{
			Log.Warning( $"Need Files: {string.Join( ", ", result.Files )}" );
		}

		return false;

	}

	void SetMetaFromAsset( Asset asset )
	{
		SetMeta( "PrimaryAsset", asset.Path );

		if ( asset.AssetType == AssetType.Model )
		{
			var model = Model.Load( asset.Path );

			SetMeta( "ModelMetaVersion", 1 );

			SetMeta( "RenderMins", model.RenderBounds.Mins );
			SetMeta( "RenderMaxs", model.RenderBounds.Maxs );

			SetMeta( "PhysicsMins", model.PhysicsBounds.Mins );
			SetMeta( "PhysicsMaxs", model.PhysicsBounds.Maxs );

			SetMeta( "BoneCount", model.BoneCount );
			SetMeta( "MeshCount", model.MeshCount );
			SetMeta( "AttachmentCount", model.Attachments.Count );
			SetMeta( "BodyPartCount", model.Parts.Count );
		}

		SetMeta( "AssetType", asset.AssetType.FriendlyName );
		SetMeta( "AssetTypeExtension", asset.AssetType.FileExtension );
		SetMeta( "AssetTypeIsGameResource", asset.AssetType.IsGameResource );
		SetMeta( "AssetTypeCategory", asset.AssetType.Category );
		SetMeta( "AssetTypeTarget", asset.AssetType.ResourceType?.FullName );
		SetMeta( "AssetTypeIsSimple", asset.AssetType.IsSimpleAsset );
		SetMeta( "AssetTypeHasDependencies", asset.AssetType.HasDependencies );
	}

	public async Task UploadFiles()
	{
		// upload
		var uploads = Manifest.Assets.Where( x => !x.Skip ).ToArray();

		var tasks = new List<Task>();

		foreach ( var up in uploads )
		{
			var t = UploadFile( up );

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

		await Task.WhenAll( tasks.ToArray() );
	}


	async Task UploadFile( ProjectFile file )
	{
		file.SizeUploaded = 1;

		if ( file.Contents is not null )
		{
			var r = await Project.Package.UploadFile( file.Contents, file.Name, p => { file.SizeUploaded = p.ProgressBytes; TriggerProgessChanged(); } );
			if ( r ) file.Skip = true;
		}
		else if ( file.AbsolutePath is not null )
		{
			var r = await Project.Package.UploadFile( file.AbsolutePath, file.Name, p => { file.SizeUploaded = p.ProgressBytes; TriggerProgessChanged(); } );
			if ( r ) file.Skip = true;
		}
		else
		{
			Log.Warning( $"Unable to upload {file.Name} - has no content defined!" );
		}

		TriggerProgessChanged();
	}

	/// <summary>
	/// Manually add a file to the manifest
	/// </summary>
	public Task AddFile( byte[] contents, string relativePath )
	{
		return Manifest.AddFile( contents, relativePath );
	}

	/// <summary>
	/// Manually add a file to the manifest
	/// </summary>
	public Task AddFile( string contents, string relativePath )
	{
		return Manifest.AddTextFile( contents, relativePath );
	}

	/// <summary>
	/// If the code is referencing a package - we can add it to the manifest using this.
	/// </summary>
	public Task AddCodePackageReference( string package )
	{
		return Manifest.AddCodePackageReference( package );
	}

	RealTimeSince timeSinceTrigger;

	void TriggerProgessChanged()
	{
		if ( timeSinceTrigger <= (1.0f / 30.0f) )
			return;

		timeSinceTrigger = 0;
		MainThread.Queue( OnProgressChanged );
	}

	/// <summary>
	/// Get access to the files within the manifest
	/// </summary>
	public IEnumerable<ProjectFile> Files => Manifest.Assets;

	/// <summary>
	/// Allows to set information on the revision - for future reference
	/// </summary>
	public void SetChangeDetails( string change, string detail )
	{
		Manifest.Summary = change;
		Manifest.Description = detail;
	}

	/// <summary>
	/// Return true if we're not opposed to publishing this asset
	/// </summary>
	public static bool CanPublishFile( Asset a )
	{
		// Core/base shaders should never be uploaded
		// Ideally I'd just check against mod_base and mod_core but we have weird c# filesystem
		if ( a.AbsolutePath.Contains( "/addons/base/assets/shaders/", StringComparison.OrdinalIgnoreCase ) ) return false;
		if ( a.AbsolutePath.Contains( "/core/shaders/", StringComparison.OrdinalIgnoreCase ) ) return false;

		return true;
	}

	/// <summary>
	/// Try to upload this asset as a workshop file. We need to do this with some assets because we want
	/// to publish them as paid steam inventory items - so they need to have a connection.
	/// </summary>
	async Task TryWorkshopUpload()
	{
		var asset = EditorUtility.GetAssetFromProject( Project );
		if ( asset is null ) return;

		// only clothing uploads to workshop for now
		if ( !asset.TryLoadResource<Clothing>( out var clothing ) )
			return;

		await WorkshopUtils.UploadAsset( asset );

		// look up the workshop id
		if ( asset.MetaData.Get<ulong>( "WorkshopId" ) is var id )
		{
			// Publish this info with the asset
			SetMeta( "WorkshopId", id );

			if ( WorkshopUtils.NeedsLegalAgreement() )
			{
				PromptLegalAgreement( id );
			}
		}
	}

	void PromptLegalAgreement( ulong id )
	{
		var popup = new PopupDialogWidget( "paid" );

		popup.FixedWidth = 650;
		popup.WindowTitle = "Workshop Legal Agreement Needs Acceptance";

		popup.MessageLabel.Text = $"If you're interested in monetizing your workshop item, You will need to agree to the latest Legal Agreement on Steam before finalizing revenue sharing percentages.";

		popup.ButtonLayout.Spacing = 4;
		popup.ButtonLayout.AddStretchCell();

		popup.ButtonLayout.Add( new Button.Primary( "Open in Web", "open_in_new" )
		{
			Clicked = () =>
			{
				EditorUtility.OpenFolder( $"https://steamcommunity.com/sharedfiles/filedetails/?id={id}" );
				popup.Destroy();
			}
		} );

		popup.ButtonLayout.Add( new Button( "Ignore", "highlight_off" )
		{
			Clicked = () =>
			{
				popup.Destroy();
			}
		} );

		popup.SetModal( true, true );
		popup.Hide();
		popup.Show();
	}

}
