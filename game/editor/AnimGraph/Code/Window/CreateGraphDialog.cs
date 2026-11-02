using System.IO;

namespace Editor.AnimGraph;

internal sealed class CreateGraphDialog : Dialog, AssetSystem.IEventListener
{
	private readonly Session _session = Session.Create();
	private readonly Nodes.AnimationClipNode _clip = new();
	private readonly Nodes.ReferencePoseNode _referencePose = new();
	private readonly string _directory;
	private readonly ClipList _clips;
	private readonly PropertiesPreview _preview;
	private readonly Label _status;
	private readonly Button _create;
	private readonly Button _clearAnimation;
	private readonly WarningBox _error;
	private readonly SerializedProperty _skeletonProperty;
	private readonly Button _skeletonMatches;
	private string[] _matchingSkeletons = [];

	[Event( "asset.createoptions" )]
	private static void AddCreateOption( List<CreateAsset.Entry> entries )
	{
		entries.Add( new CreateAsset.Entry
		{
			Name = "Animation Graph",
			Icon = "account_tree",
			Category = "Animation",
			Pinned = true,
			Action = folder => new CreateGraphDialog( folder.FullName ).Show()
		} );
	}

	public CreateGraphDialog( string directory ) : base( EditorWindow )
	{
		_directory = directory;
		_session.Graph.AddNode( _clip );
		_session.Graph.Nodes.OfType<Nodes.PoseResultNode>().Single().Pose = new NodeInput
		{
			Identifier = _clip.Identifier,
			Output = nameof( Nodes.AnimationClipNode.Result )
		};

		Window.WindowTitle = "New Animation Graph";
		Window.Size = new Vector2( 960, 640 );
		Window.MinimumSize = new Vector2( 760, 500 );
		Window.SetWindowIcon( "account_tree" );

		Layout = Layout.Column();
		Layout.Margin = 12;
		Layout.Spacing = 8;
		Layout.Add( new Label( "Choose a model and its skeleton. You can start with an animation or add one later.", this ) { WordWrap = true } );

		var content = Layout.AddRow( 1 );
		content.Spacing = 12;
		var settings = content.AddColumn( 1 );
		settings.Spacing = 8;

		var serialized = EditorTypeLibrary.GetSerializedObject( _session.Graph );
		var targets = settings.AddColumn();
		targets.Spacing = 0;
		var sheet = new ControlSheet();
		sheet.AddRow( serialized.GetProperty( nameof( Data.TargetModel ) ) );
		targets.Add( sheet );

		var skeletonRow = targets.AddRow();
		skeletonRow.Spacing = 4;
		var skeletonSheet = new ControlSheet();
		_skeletonProperty = serialized.GetProperty( nameof( Data.TargetSkeleton ) );
		skeletonSheet.AddRow( _skeletonProperty );
		skeletonRow.Add( skeletonSheet, 1 );
		_skeletonMatches = skeletonRow.Add( new Button( "Matches", "arrow_drop_down", this )
		{
			Clicked = ShowMatchingSkeletons,
			ToolTip = "Choose a skeleton compatible with this model"
		} );

		_clips = new ClipList( _session.Graph, editing: false );
		_clips.OnClipSelected = SelectClip;
		var status = settings.AddRow();
		status.Spacing = 8;
		_status = status.Add( new Label( this ) { WordWrap = true }, 1 );
		_clearAnimation = status.Add( new Button( "Clear", "close", this ) { Clicked = _clips.ClearSelection, ToolTip = "Create without a starting animation" } );
		settings.Add( _clips, 1 );
		_preview = content.Add( new PropertiesPreview( this, _session.Graph ), 1 );
		_error = Layout.Add( new WarningBox( this ) { BackgroundColor = Theme.Red, Icon = "error_outline", Visible = false } );

		var buttons = Layout.AddRow();
		buttons.Spacing = 8;
		buttons.AddStretchCell();
		buttons.Add( new Button( "Cancel", this ) { Clicked = Close } );
		_create = buttons.Add( new Button.Primary( "Create Graph...", "add", this ) { Clicked = Create } );

		serialized.OnPropertyChanged += property =>
		{
			_error.Visible = false;
			RefreshTargets( property.Name == nameof( Data.TargetModel ) );
		};
		RefreshTargets( true );
	}

	private void RefreshTargets( bool modelChanged = false, bool refreshSkeletons = false )
	{
		if ( modelChanged )
		{
			_session.Graph.TargetSkeleton = _session.Graph.TargetModel?.SkeletonName ?? "";
		}

		if ( modelChanged || refreshSkeletons )
		{
			_matchingSkeletons = FindMatchingSkeletons( _session.Graph.TargetModel );
			if ( modelChanged && string.IsNullOrWhiteSpace( _session.Graph.TargetSkeleton ) && _matchingSkeletons.Length == 1 )
				_session.Graph.TargetSkeleton = _matchingSkeletons[0];
		}
		_skeletonMatches.Visible = _matchingSkeletons.Length > 1
			|| (_matchingSkeletons.Length == 1 && string.IsNullOrWhiteSpace( _session.Graph.TargetSkeleton ));

		if ( !_session.Graph.SelectedVariation.CompatibleClips.Contains( _clip.AnimationName, StringComparer.OrdinalIgnoreCase ) )
			_clip.AnimationName = "";
		_clips.Rebuild();
		UpdateSelection();
	}

	private static string[] FindMatchingSkeletons( Model model )
	{
		if ( model is not { IsValid: true, IsError: false, BoneCount: > 0 } || !string.IsNullOrWhiteSpace( model.SkeletonName ) )
			return [];

		var bones = model.Bones.AllBones.ToDictionary( bone => bone.Name, StringComparer.OrdinalIgnoreCase );
		return AssetSystem.All
			.Where( asset => !asset.IsDeleted && asset.AssetType.FileExtension == "skel" )
			.Select( asset => asset.Path )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.Where( path => MatchesSkeleton( bones, Skeleton.Load( path ) ) )
			.Order( StringComparer.OrdinalIgnoreCase )
			.ToArray();
	}

	private static bool MatchesSkeleton( IReadOnlyDictionary<string, BoneCollection.Bone> modelBones, Skeleton skeleton )
	{
		if ( skeleton is not { IsValid: true, IsError: false, BoneCount: > 0 } || skeleton.BoneCount != modelBones.Count )
			return false;

		foreach ( var bone in skeleton.Bones )
		{
			if ( !modelBones.TryGetValue( bone.Name, out var modelBone ) )
				return false;

			var parentName = bone.ParentIndex < 0 ? null : skeleton.Bones[bone.ParentIndex].Name;
			if ( !string.Equals( parentName, modelBone.Parent?.Name, StringComparison.OrdinalIgnoreCase ) )
				return false;

			if ( !(Vector3.DistanceBetween( bone.Transform.Position, modelBone.LocalTransform.Position ) <= 0.1f
				&& bone.Transform.Rotation.Distance( modelBone.LocalTransform.Rotation ) <= 0.1f
				&& bone.Transform.Scale.AlmostEqual( modelBone.LocalTransform.Scale, 0.001f )) )
				return false;
		}

		return true;
	}

	private void ShowMatchingSkeletons()
	{
		var menu = new Menu( this );
		foreach ( var path in _matchingSkeletons )
		{
			menu.AddOption( path, "account_tree", () =>
			{
				_skeletonProperty.Parent.NoteStartEdit( _skeletonProperty );
				_skeletonProperty.SetValue( path );
				_skeletonProperty.Parent.NoteFinishEdit( _skeletonProperty );
			} );
		}
		menu.OpenAtCursor();
	}

	private void SelectClip( string animation )
	{
		_error.Visible = false;
		_clip.AnimationName = animation;
		UpdateSelection();
	}

	private string ValidateSelection()
	{
		var graph = _session.Graph;
		if ( graph.TargetModel is not { IsValid: true, IsError: false, BoneCount: > 0 } model )
			return "Choose a skeletal model to preview your animation.";

		if ( string.IsNullOrWhiteSpace( graph.TargetSkeleton ) )
			return _matchingSkeletons.Length > 1
				? "Several skeletons match this model. Choose a match or select another skeleton."
				: "Choose the skeleton that belongs to this model.";

		var skeleton = Skeleton.Load( graph.TargetSkeleton );
		if ( skeleton is not { IsValid: true, IsError: false, BoneCount: > 0 } )
			return "The selected skeleton could not be loaded. Check its compilation errors or choose another skeleton.";

		var bones = model.Bones.AllBones.ToDictionary( bone => bone.Name, StringComparer.OrdinalIgnoreCase );
		var sharedBones = skeleton.Bones.Where( bone => bones.ContainsKey( bone.Name ) ).ToArray();
		if ( sharedBones.Length == 0 )
			return "The selected skeleton has no bones in common with this model. Choose a matching model and skeleton.";

		if ( sharedBones.Any( bone => Vector3.DistanceBetween( bone.Transform.Position, bones[bone.Name].LocalTransform.Position ) > 0.1f ) )
			return "This skeleton doesn't match the model's proportions or pose. Choose the skeleton made for this model.";

		if ( string.IsNullOrWhiteSpace( _clip.AnimationName ) )
			return null;

		var clips = graph.SelectedVariation.CompatibleClips;
		if ( !clips.Contains( _clip.AnimationName, StringComparer.OrdinalIgnoreCase ) )
			return "The selected animation is no longer available. Choose another animation or clear it to add one later.";

		var clip = Clip.Load( _clip.AnimationName );
		if ( clip is not { IsValid: true, IsError: false } )
			return "The selected clip could not be loaded. Check its compilation errors or choose another clip.";

		if ( clip.FrameCount == 0 )
			return "The selected clip has no animation frames. Choose another clip.";

		if ( !string.Equals( clip.SkeletonName, graph.TargetSkeleton, StringComparison.OrdinalIgnoreCase ) )
			return "The selected clip uses a different skeleton. Choose a clip compiled against the selected skeleton.";

		if ( clip.IsAdditive )
			return "This is an additive animation, which needs a base pose. Choose a non-additive clip to start your graph.";

		return null;
	}

	private void UpdateSelection()
	{
		var hasAnimation = !string.IsNullOrWhiteSpace( _clip.AnimationName );
		_clearAnimation.Enabled = hasAnimation;
		var message = ValidateSelection();
		var valid = message is null;
		_create.Enabled = valid;
		_create.ToolTip = message ?? "Choose where to save the new animation graph";
		_status.Color = Theme.TextLight;
		if ( message is null && hasAnimation )
		{
			var clip = Clip.Load( _clip.AnimationName );
			message = clip.FrameCount == 1
				? $"{Path.GetFileNameWithoutExtension( _clip.AnimationName )} - single-frame pose"
				: $"{Path.GetFileNameWithoutExtension( _clip.AnimationName )} - {clip.Duration:0.##}s, looping";
		}
		_status.Text = message ?? "";
		_preview.SetAnimation( valid && hasAnimation ? _clip.AnimationName : null );
	}

	private void Create()
	{
		UpdateSelection();
		if ( !_create.Enabled )
			return;

		var dialog = new FileDialog( Window )
		{
			Title = "Create Animation Graph",
			Directory = _directory,
			DefaultSuffix = ".ag"
		};
		dialog.SetFindFile();
		dialog.SetModeSave();
		dialog.SetNameFilter( "Animation Graph (*.ag)" );
		dialog.SelectFile( $"{Path.GetFileNameWithoutExtension( _session.Graph.TargetModel.ResourcePath )}.ag" );

		if ( !dialog.Execute() || string.IsNullOrWhiteSpace( dialog.SelectedFile ) )
			return;

		Save( dialog.SelectedFile );
	}

	private void Save( string path )
	{
		UpdateSelection();
		if ( !_create.Enabled )
			return;

		try
		{
			_error.Visible = false;
			path = Path.GetFullPath( path );
			var relative = Path.GetRelativePath( Project.Current.GetAssetsPath(), path );
			if ( Path.IsPathRooted( relative ) || relative == ".." || relative.StartsWith( $"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal )
				|| !Path.GetExtension( path ).Equals( ".ag", StringComparison.OrdinalIgnoreCase ) )
			{
				ShowError( "Save the .ag graph inside this project's Assets folder." );
				return;
			}

			var sameAsset = _session.SourceFile is { Length: > 0 } source
				&& string.Equals( Path.GetFullPath( source ), path, StringComparison.OrdinalIgnoreCase );
			var graph = _session.Graph;
			var hasAnimation = !string.IsNullOrWhiteSpace( _clip.AnimationName );
			Node starter = hasAnimation ? _clip : _referencePose;
			graph.RemoveNode( hasAnimation ? _referencePose : _clip );
			if ( !graph.Nodes.Contains( starter ) )
				graph.AddNode( starter );
			graph.Nodes.OfType<Nodes.PoseResultNode>().Single().Pose = new NodeInput
			{
				Identifier = starter.Identifier,
				Output = nameof( Nodes.ReferencePoseNode.Result )
			};
			var compiled = _session.CompileAll();
			if ( compiled.Failed )
			{
				ShowError( string.Join( "\n", compiled.Errors.Select( error => error.Message ) ) );
				return;
			}

			_session.Save( path, sameAsset ? _session.Revision : null );
			if ( _session.AssetCompileSucceeded != true )
			{
				ShowError( "The graph source was saved, but the asset could not be compiled. Check the console, fix the animation assets and try again." );
				return;
			}
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException )
		{
			Log.Warning( exception, "AnimGraph: could not create animation graph" );
			ShowError( exception.Message );
			return;
		}

		MainAssetBrowser.Instance?.Local.OnAssetCreated( _session.Asset, path );
		_session.Asset.OpenInEditor();
		Close();
	}

	private void ShowError( string message )
	{
		_error.Label.Text = message;
		_error.Visible = true;
	}

	void AssetSystem.IEventListener.OnAssetSystemChanges() => RefreshTargets( refreshSkeletons: true );

	void AssetSystem.IEventListener.OnAssetChanged( Asset asset )
	{
		if ( asset.AssetType.FileExtension is "anim" or "skel" )
			RefreshTargets( refreshSkeletons: asset.AssetType.FileExtension == "skel" );
		else if ( string.Equals( asset.Path, _session.Graph.TargetModel?.ResourcePath, StringComparison.OrdinalIgnoreCase ) )
			RefreshTargets( refreshSkeletons: true );
	}
}
