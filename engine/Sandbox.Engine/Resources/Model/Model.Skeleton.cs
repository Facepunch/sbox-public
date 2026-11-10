namespace Sandbox;

internal class ModelBones : BoneCollection
{
	public Model Model { get; }

	Bone _root;

	public override Bone Root => _root;

	List<Bone> _allBones;

	public override IReadOnlyList<Bone> AllBones => _allBones;

	public ModelBones( Model model )
	{
		Model = model;

		_allBones = new();

		for ( int i = 0; i < Model.BoneCount; i++ )
		{
			var b = new ModelBone( Model, i );
			_allBones.Add( b );
		}

		for ( int i = 0; i < Model.BoneCount; i++ )
		{
			var parent = Model.GetBoneParent( i );
			if ( parent < 0 ) continue;
			if ( parent >= _allBones.Count() ) continue;
			_allBones[i].SetParent( _allBones[parent] );
		}

		_root = _allBones.Where( x => x.Parent == null ).FirstOrDefault();
	}

	class ModelBone : Bone
	{
		public ModelBone( Model model, int i )
		{
			Index = i;
			Name = model.GetBoneName( i );
			LocalTransform = model.GetBoneTransform( i );
		}

		public override int Index { get; }
		public override string Name { get; }
		public override Transform LocalTransform { get; }
	}

	internal void Dispose()
	{
		_allBones.Clear();
	}
}

public partial class Model
{
	BoneCollection _bones;

	/// <summary>
	/// Access to bones of this model.
	/// </summary>
	public BoneCollection Bones
	{
		get
		{
			_bones ??= new ModelBones( this );
			return _bones;
		}
	}

	/// <summary>
	/// Number of bones this model has.
	/// </summary>
	public int BoneCount => native.NumBones();

	/// <summary>
	/// Returns name of a bone at given bone index.
	/// </summary>
	/// <param name="boneIndex">Bone index to get name of, starting at 0.</param>
	/// <returns>Name of the bone.</returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when given index exceeds range of [0,BoneCount-1]</exception>
	public string GetBoneName( int boneIndex )
	{
		OOBChecks.ThrowIfBoneOutOfBounds( boneIndex, BoneCount, nameof( boneIndex ) );

		return native.boneName( boneIndex );
	}

	/// <summary>
	/// Returns the id of given bone's parent bone.
	/// </summary>
	/// <param name="boneIndex">The bone to look up parent of.</param>
	/// <returns>The id of the parent bone, or -1 if given bone has no parent.</returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when given index exceeds range of [0,BoneCount-1]</exception>
	public int GetBoneParent( int boneIndex )
	{
		OOBChecks.ThrowIfBoneOutOfBounds( boneIndex, BoneCount, nameof( boneIndex ) );

		return native.boneParent( boneIndex );
	}

	/// <summary>
	/// Returns transform of given bone at bind position.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when given index exceeds range of [0,BoneCount-1]</exception>
	public Transform GetBoneTransform( int boneIndex )
	{
		OOBChecks.ThrowIfBoneOutOfBounds( boneIndex, BoneCount, nameof( boneIndex ) );

		return native.GetBoneTransform( boneIndex );
	}

	/// <summary>
	/// Returns transform of given bone at bind position.
	/// </summary>
	public Transform GetBoneTransform( string bone )
	{
		var idx = native.FindBoneIndex( bone );
		if ( idx < 0 ) return Transform.Zero;

		return GetBoneTransform( idx );
	}

	/// <summary>
	/// Creates a dictionary of bone names to game objects, where each game object is a bone object in the scene.
	/// </summary>
	public Dictionary<BoneCollection.Bone, GameObject> CreateBoneObjects( GameObject root )
	{
		Dictionary<BoneCollection.Bone, GameObject> boneObjects = new();
		if ( !root.IsValid() ) return boneObjects;

		foreach ( var b in Bones.AllBones.Where( x => x is { Parent: null } ) )
		{
			CreateBoneObjects( root, b, boneObjects );
		}

		return boneObjects;
	}

	/// <summary>
	/// Recursively creates game objects for each bone in the hierarchy, starting from the given root bone.
	/// </summary>
	private void CreateBoneObjects( GameObject root, BoneCollection.Bone thisBone, Dictionary<BoneCollection.Bone, GameObject> boneObjects )
	{
		var scene = root.Scene;
		if ( !scene.IsValid() ) return;

		var go = root.Children.FirstOrDefault( x => x.Name == thisBone.Name && !x.Flags.Contains( GameObjectFlags.Attachment ) );

		go ??= new GameObject( root, true, thisBone.Name );

		go.Flags |= GameObjectFlags.Bone;

		boneObjects[thisBone] = go;

		foreach ( var childBone in thisBone.Children )
		{
			CreateBoneObjects( go, childBone, boneObjects );
		}
	}
}
