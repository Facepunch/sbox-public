namespace Sandbox;

public class HitboxSet
{
	List<Box> all;

	internal HitboxSet( ModelBones bones, CHitBoxSet set )
	{
		// Cache count to avoid Native Interop calls in loop condition
		// and initialize list with correct capacity to avoid resizing
		int count = (set.IsValid) ? set.numhitboxes() : 0;
		
		all = new( count );

		if ( count == 0 )
		{
			return;
		}

		for ( int i = 0; i < count; i++ )
		{
			var box = new Box( bones, set.pHitbox( i ) );
			all.Add( box );
		}
	}

	public IReadOnlyList<Box> All => all;

	public class Box
	{
		const int HITBOX_SHAPE_BOX = 0;
		const int HITBOX_SHAPE_SPHERE = 1;
		const int HITBOX_SHAPE_CAPSULE = 2;

		internal Box( ModelBones bones, CHitBox cHitBox )
		{
			Name = cHitBox.m_name;
			SurfaceName = cHitBox.m_sSurfaceProperty;

			Bone = bones.GetBone( cHitBox.m_sBoneName );

			Shape = cHitBox.m_nShapeType switch
			{
				HITBOX_SHAPE_SPHERE => new Sphere( cHitBox.m_vMinBounds, cHitBox.m_flShapeRadius ),
				HITBOX_SHAPE_CAPSULE => new Capsule( cHitBox.m_vMinBounds, cHitBox.m_vMaxBounds, cHitBox.m_flShapeRadius ),
				_ => new BBox( cHitBox.m_vMinBounds, cHitBox.m_vMaxBounds )
			};

			Tags = new TagSet();

			for ( int i = 0; i < 16; i++ )
			{
				var tagToken = cHitBox.GetTag( i );
				if ( string.IsNullOrEmpty( tagToken ) ) break;

				Tags.Add( tagToken );
			}
		}


		public string Name { get; init; }
		public string SurfaceName { get; init; }
		public BoneCollection.Bone Bone { get; init; }
		public ITagSet Tags { get; init; }

		/// <summary>
		/// Either a Sphere, Capsule or BBox
		/// </summary>
		public object Shape { get; init; }

		/// <summary>
		/// Get a random point inside this hitbox
		/// </summary>
		public Vector3 RandomPointInside
		{
			get
			{
				if ( Shape is Sphere sphere ) return sphere.RandomPointInside;
				if ( Shape is BBox bbox ) return bbox.RandomPointInside;
				if ( Shape is Capsule capsule ) return capsule.RandomPointInside;

				throw new NotImplementedException();
			}
		}

		/// <summary>
		/// Get a random point on the edge this hitbox
		/// </summary>
		public Vector3 RandomPointOnEdge
		{
			get
			{
				if ( Shape is Sphere sphere ) return sphere.RandomPointOnEdge;
				if ( Shape is BBox bbox ) return bbox.RandomPointOnEdge;
				if ( Shape is Capsule capsule ) return capsule.RandomPointOnEdge;

				throw new NotImplementedException();
			}
		}
	}

	internal void Dispose()
	{
		all.Clear();
	}
}

public partial class Model
{
	HitboxSet _hitboxset;

	/// <summary>
	/// Access to default hitbox set of this model
	/// </summary>
	public HitboxSet HitboxSet
	{
		get
		{
			if ( _hitboxset is null )
			{
				_hitboxset = new HitboxSet( (ModelBones)Bones, native.GetHitboxSetByIndex( 0, 0 ) );
			}

			return _hitboxset;
		}
	}

}
