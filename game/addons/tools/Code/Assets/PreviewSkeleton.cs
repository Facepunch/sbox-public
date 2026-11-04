using Sandbox.Animation;

namespace Editor.Assets;

[AssetPreview( "skel" )]
public class PreviewSkeleton( Asset asset ) : AssetPreview( asset )
{
	public override float PreviewWidgetCycleSpeed => 0.2f;
	public override bool IsAnimatedPreview => true;

	IReadOnlyList<Skeleton.Bone> _bones;
	private IReadOnlyDictionary<string, float> _weights;
	private bool _showWeights;
	private string _rootBone;
	private HashSet<string> _included;
	private bool _showNames = EditorCookie.Get( "skeleton.preview.names", false );

	public override Widget CreateToolbar()
	{
		var bar = new ToolBar( null );
		var names = bar.AddOption( "Bone Names", "label", () =>
		{
			_showNames = !_showNames;
			EditorCookie.Set( "skeleton.preview.names", _showNames );
		} );
		names.Checkable = true;
		names.FetchCheckedState = () => _showNames;
		return bar;
	}

	public void SetBoneWeights( IReadOnlyDictionary<string, float> weights, bool showWeights, string rootBone = null )
	{
		_bones ??= Skeleton.Load( Asset.Path )?.Bones;
		_weights = weights;
		_showWeights = showWeights;
		if ( _rootBone == rootBone )
			return;

		_rootBone = rootBone;
		RebuildIncludedBones();
	}

	public Color GetBoneColor( string name, bool hovered )
	{
		if ( _included is not null && !_included.Contains( name ) )
			return Theme.TextControl.WithAlpha( 0.15f );

		return _showWeights
			? Color.Lerp( Theme.Red, Theme.Green, _weights?.GetValueOrDefault( name ) ?? 0f )
			: Color.White.WithAlpha( hovered ? 1f : 0.25f );
	}

	private void RebuildIncludedBones()
	{
		_included = null;
		if ( _bones is null || string.IsNullOrWhiteSpace( _rootBone ) )
			return;

		var root = _bones.FirstOrDefault( bone => string.Equals( bone.Name, _rootBone, StringComparison.OrdinalIgnoreCase ) );
		if ( root is null )
			return;

		_included = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var bone in _bones )
		{
			for ( var ancestor = bone; ancestor is not null; ancestor = ancestor.ParentIndex >= 0 ? _bones[ancestor.ParentIndex] : null )
			{
				if ( ancestor != root )
					continue;

				_included.Add( bone.Name );
				break;
			}
		}
	}

	public override async Task InitializeAsset()
	{
		await Task.Yield();

		var skeleton = Skeleton.Load( Asset.Path );
		if ( skeleton is null || skeleton.BoneCount == 0 )
			return;

		_bones = skeleton.Bones;
		RebuildIncludedBones();

		var bounds = new BBox();
		foreach ( var bone in _bones )
			bounds = bounds.AddPoint( bone.Transform.Position );

		using ( Scene.Push() )
		{
			PrimaryObject = new GameObject( true, "preview skeleton" );
			PrimaryObject.WorldTransform = Transform.Zero;

			SceneSize = bounds.Size;
			SceneCenter = bounds.Center;
		}
	}

	public override void UpdateScene( float cycle, float timeStep )
	{
		base.UpdateScene( cycle, timeStep );

		if ( Gizmo.Settings is null )
			return;

		DrawBones();
	}

	void DrawBones()
	{
		if ( _bones is null || !PrimaryObject.IsValid() )
			return;

		var root = PrimaryObject.WorldTransform;

		var nameColor = Theme.Text;

		foreach ( var bone in _bones )
		{
			var tx = root.ToWorld( bone.Transform );
			var parentTx = bone.ParentIndex >= 0 ? root.ToWorld( _bones[bone.ParentIndex].Transform ) : root;

			using var scope = Gizmo.Scope( $"Bone{bone.Index}", tx );

			var radius = Math.Min( 1f, tx.Position.Distance( Gizmo.CameraTransform.Position ) / 64f );
			var excluded = _included is not null && !_included.Contains( bone.Name );
			var boneColor = _showWeights || excluded ? GetBoneColor( bone.Name, false ) : Theme.Blue;

			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.LineThickness = 1f;

			Gizmo.Draw.Color = boneColor.WithAlphaMultiplied( 0.6f );
			Gizmo.Draw.Line( 0f, tx.PointToLocal( parentTx.Position ) );

			Gizmo.Draw.Color = _showWeights || excluded ? boneColor : Theme.Yellow;
			Gizmo.Draw.LineCircle( 0f, radius );

			if ( _showNames )
			{
				Gizmo.Draw.Color = excluded ? nameColor.WithAlpha( 0.15f ) : nameColor;
				Gizmo.Draw.ScreenText( bone.Name, tx.Position, Vector2.Up * 8f, size: 10f, flags: TextFlag.CenterBottom );
			}
		}
	}
}
