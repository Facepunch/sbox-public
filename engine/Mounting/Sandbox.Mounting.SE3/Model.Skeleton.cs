namespace SeriousEngine3;

public struct BoneInfo
{
	public string Name;
	public int Parent;
	public Vector3 Position;
	public Rotation Rotation;
}

public partial class ModelLoader
{
	static List<BoneInfo> ReadBones( MetaValue skeleton )
	{
		var result = new List<BoneInfo>();

		var lods = skeleton["1"]; // CStaticArray<CSkeletonLOD>
		if ( lods.Count == 0 ) return result;

		var bones = lods[0]["2"]; // CStaticStackArray<SkeletonBone>
		for ( int i = 0; i < bones.Count; i++ )
		{
			var bone = bones[i];
			string boneName = bone["1"].AsIdentName(); // IDENT
			int parent = bone["11"].AsInt(); // INDEX (parent)

			var quatVect = bone["4"]; // QuatVect
			var q = quatVect["1"].AsQuaternion(); // Quaternion4f
			var v = quatVect["2"].AsVector3(); // Vector3f

			result.Add( new BoneInfo
			{
				Name = boneName,
				Parent = parent,
				Position = new Vector3( -v.z, -v.x, v.y ),
				Rotation = new Rotation( -q.z, -q.x, q.y, q.w )
			} );
		}

		return result;
	}

	static void AddBones( ModelBuilder builder, List<BoneInfo> bones )
	{
		var transforms = new Transform[bones.Count];

		for ( int i = 0; i < bones.Count; i++ )
		{
			var b = bones[i];
			var t = new Transform( b.Position * Scale, b.Rotation );
			transforms[i] = b.Parent < 0 ? t : transforms[b.Parent].ToWorld( t );
		}

		for ( int i = 0; i < bones.Count; i++ )
		{
			var b = bones[i];
			var t = transforms[i];
			builder.AddBone( b.Name, t.Position, t.Rotation, b.Parent < 0 ? null : bones[b.Parent].Name );
		}
	}

	static Transform[] BuildBoneTransforms( List<BoneInfo> bones )
	{
		var transforms = new Transform[bones.Count];

		for ( int i = 0; i < bones.Count; i++ )
		{
			var b = bones[i];
			var t = new Transform( b.Position * Scale, b.Rotation );
			transforms[i] = b.Parent < 0 ? t : transforms[b.Parent].ToWorld( t );
		}

		return transforms;
	}
}
