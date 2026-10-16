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
		readonly Dictionary<Type, int> _groupIndices = new();
		readonly List<List<WeightedEffect>> _groups = new();

		public void AddEffects( GameObject gameObject, float weight )
		{
			_components.Clear();
			gameObject.Components.GetAll( _components, FindMode.EnabledInSelfAndDescendants );
			foreach ( var effect in _components )
			{
				var type = effect.GetType();
				if ( !_groupIndices.TryGetValue( type, out var index ) )
				{
					index = _groupIndices.Count;
					_groupIndices.Add( type, index );
					if ( index == _groups.Count ) _groups.Add( new() );
				}

				_groups[index].Add( new WeightedEffect { Effect = effect, Weight = weight } );
			}
		}

		public void Build( CameraComponent camera )
		{
			// Group order is first occurrence, just like GroupBy. All collection is finished
			// before invoking effects, whose callbacks may change the scene.
			for ( int i = 0; i < _groupIndices.Count; i++ )
			{
				var group = _groups[i];
				group[0].Effect.Build( new PostProcessContext { Camera = camera, Components = group } );
			}
		}

		public void Clear()
		{
			Volumes.Clear();
			_components.Clear();
			for ( int i = 0; i < _groupIndices.Count; i++ ) _groups[i].Clear();
			_groupIndices.Clear();
		}
	}
}
