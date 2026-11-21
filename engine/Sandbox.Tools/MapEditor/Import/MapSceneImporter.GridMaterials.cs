using System;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	// Hammer loads each pair of base and replacement handles when it first applies that override.
	sealed class GridMaterialSets
	{
		readonly Dictionary<Element, int> loaded = [];

		public int Load( Element set, int index )
		{
			int previous = loaded.GetValueOrDefault( set );
			var variants = set.Get<Element[]>( "materialSets", [] );
			if ( (uint)index < variants.Length )
			{
				int count = Math.Min( variants[0].GetArray<string>( "materialNames" ).Length, variants[index].GetArray<string>( "materialNames" ).Length );
				if ( count > previous ) loaded[set] = count;
			}
			return previous;
		}

		public ReadOnlySpan<int> ReloadLayer( ReadOnlySpan<int> materials, ReadOnlySpan<int> slots, bool initialLayer, Func<int, Element> getSet )
		{
			// Switching layers remaps saved indices by name. Duplicate names resolve to the first match.
			var names = new Dictionary<(int Slot, int Index), int>();
			int[] remapped = null;
			for ( int i = 0; i < materials.Length; i++ )
			{
				int index = materials[i];
				if ( index <= 0 ) continue;
				int slot = i < slots.Length ? slots[i] : 0;
				if ( !names.TryGetValue( (slot, index), out int resolved ) )
				{
					if ( getSet( slot ) is not { } set ) continue;
					// Layer zero has already been active once when Hammer begins baking the other layers.
					if ( initialLayer ) Load( set, index );
					var variants = set.Get<Element[]>( "materialSets", [] );
					resolved = (uint)index < variants.Length
						? Array.FindIndex( variants, m => string.Equals( m.Name, variants[index].Name, StringComparison.OrdinalIgnoreCase ) ) : 0;
					names[(slot, index)] = resolved;
				}
				if ( resolved != index ) (remapped ??= materials.ToArray())[i] = resolved;
			}
			return remapped is null ? materials : remapped;
		}
	}
}
