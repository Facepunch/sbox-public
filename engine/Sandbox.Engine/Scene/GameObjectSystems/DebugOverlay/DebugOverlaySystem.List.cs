namespace Sandbox;

public partial class DebugOverlaySystem
{
	List<Entry> entries = new();

	class Entry : IDisposable
	{
		public bool CreatedDuringFixed;
		public bool Pooled;
		public bool SingleFrame = true;
		public float life;
		public SceneObject sceneObject;

		public Entry( float duration, bool fixedUpdate, SceneObject so )
		{
			CreatedDuringFixed = fixedUpdate;
			sceneObject = so;

			if ( duration > 0 )
			{
				life = duration;
				SingleFrame = false;
			}
		}

		public void Dispose()
		{
			sceneObject?.Delete();
			sceneObject = default;
		}
	}

	/// <summary>
	/// Add an entry manually
	/// </summary>
	void Add( float duration, SceneObject so, bool pooled = false )
	{
		// common flags for debug overlays
		so.Flags.IncludeInCubemap = false;
		so.Tags.Add( "debugoverlay" );

		var entry = new Entry( duration, inFixedUpdate, so ) { Pooled = pooled };
		entries.Add( entry );
	}

	// Single frame models from the last removal pass, hidden until a draw takes them or the next pass deletes them
	readonly Dictionary<Model, ModelBucket> _modelPool = new();
	int _poolPass;

	// Models not parked for this many passes lose their bucket, so the pool doesn't keep every model it saw
	const int BucketPasses = 60;

	sealed class ModelBucket
	{
		public readonly Stack<SceneModel> Models = new();
		public int LastParked;
	}

	SceneModel TakePooledModel( Model model )
	{
		while ( _modelPool.TryGetValue( model, out var bucket ) && bucket.Models.TryPop( out var so ) )
		{
			if ( so.IsValid() ) return so;
		}

		return null;
	}

	void ParkModel( SceneModel so )
	{
		so.RenderingEnabled = false;

		if ( !_modelPool.TryGetValue( so.Model, out var bucket ) )
			_modelPool[so.Model] = bucket = new();

		bucket.LastParked = _poolPass;
		bucket.Models.Push( so );
	}

	void DeletePooledModels()
	{
		_poolPass++;

		foreach ( var (model, bucket) in _modelPool )
		{
			while ( bucket.Models.TryPop( out var so ) )
				so.Delete();

			if ( _poolPass - bucket.LastParked > BucketPasses )
				_modelPool.Remove( model );
		}
	}
}
