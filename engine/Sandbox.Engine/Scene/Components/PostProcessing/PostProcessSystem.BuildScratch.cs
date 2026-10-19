using Sandbox.Volumes;

namespace Sandbox;

public sealed partial class PostProcessSystem
{
	// A nested build from an effect callback needs its own scratch storage.
	readonly Stack<BuildScratch> _buildScratch = new();

	BuildScratch RentBuildScratch() => _buildScratch.TryPop( out var scratch ) ? scratch : new();

	void ReturnBuildScratch( BuildScratch scratch )
	{
		scratch.Clear();
		_buildScratch.Push( scratch );
	}

	sealed class BuildScratch
	{
		public readonly List<(PostProcessVolume Volume, int Priority, int Index)> Volumes = new();
		readonly List<BasePostProcess> _components = new();
		readonly List<(BasePostProcess Effect, Component Owner)> _cameraEffects = new();
		readonly Dictionary<Type, int> _groupIndices = new();
		readonly List<List<WeightedEffect>> _groups = new();

		public void AddEffects( GameObject gameObject, float weight )
		{
			_components.Clear();
			gameObject.Components.GetAll( _components, FindMode.EnabledInSelfAndDescendants );
			foreach ( var effect in _components )
			{
				if ( !effect.IsCameraEffect )
					AddEffect( effect, weight );
			}
		}

		void AddEffect( BasePostProcess effect, float weight, Component owner = null )
		{
			var type = effect.GetType();
			if ( !_groupIndices.TryGetValue( type, out var index ) )
			{
				index = _groupIndices.Count;
				_groupIndices.Add( type, index );
				if ( index == _groups.Count ) _groups.Add( new() );
			}

			_groups[index].Add( new WeightedEffect { Effect = effect, Weight = weight, Owner = owner } );
		}

		/// <summary>
		/// Capture ownership before callbacks. Disabled owners still block their children from becoming global effects.
		/// </summary>
		public bool CollectCameraEffects( Scene scene )
		{
			foreach ( var effect in scene.Query<BasePostProcess>() )
			{
				if ( effect.IsCameraEffect )
					_cameraEffects.Add( (effect, GetOwner( effect.GameObject )) );
			}

			return _cameraEffects.Count > 0;
		}

		/// <summary>
		/// Append one owner's camera effects to the ordinary type groups, preserving their scene order.
		/// </summary>
		public void AddCameraEffects( Component owner, float weight )
		{
			if ( weight <= 0 )
				return;

			foreach ( var entry in _cameraEffects )
			{
				if ( entry.Owner == owner )
					AddEffect( entry.Effect, weight, owner );
			}
		}

		/// <summary>
		/// Collect containing volumes in stable priority order for either kind of effect build.
		/// </summary>
		public void CollectVolumes( Scene scene, Vector3 position )
		{
			foreach ( var volume in scene.Query<PostProcessVolume>() )
			{
				if ( ((VolumeSystem.IVolume)volume).Test( position ) )
					Volumes.Add( (volume, volume.Priority, Volumes.Count) );
			}

			// Match OrderBy's stable ordering when volumes have equal priority.
			Volumes.Sort( static ( a, b ) =>
			{
				var order = a.Priority.CompareTo( b.Priority );
				return order != 0 ? order : a.Index.CompareTo( b.Index );
			} );
		}

		public void Build( CameraComponent camera, SceneCamera target = null )
		{
			// Group order is first occurrence, just like GroupBy. All collection is finished
			// before invoking effects, whose callbacks may change the scene.
			for ( int i = 0; i < _groupIndices.Count; i++ )
			{
				var group = _groups[i];
				group[0].Effect.Build( new PostProcessContext { Camera = camera, TargetCamera = target, Components = group } );
			}
		}

		public void Clear()
		{
			Volumes.Clear();
			_components.Clear();
			_cameraEffects.Clear();
			for ( int i = 0; i < _groupIndices.Count; i++ ) _groups[i].Clear();
			_groupIndices.Clear();
		}
	}
}
