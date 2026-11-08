namespace Editor.AnimGraph;

/// <summary>
/// Turns the sparse weights a mask is authored with into a weight for every bone, the same way
/// the animation system does when it builds the mask - so what's drawn on the model is what the
/// graph will actually blend through.
/// </summary>
internal static class BoneMaskWeights
{
	private const float Unset = -1f;

	/// <summary>
	/// Bones with no weight of their own take one from the chain they're on: everything under a
	/// weighted bone inherits it, and a chain running between two weighted bones is feathered
	/// across so the blend doesn't snap from one to the other.
	/// </summary>
	public static (string Name, int ParentIndex)[] GetBones( Model model )
		=> model?.Bones.AllBones.Select( bone => (bone.Name, bone.Parent?.Index ?? -1) ).ToArray() ?? [];

	public static (string Name, int ParentIndex)[] GetBones( Skeleton skeleton )
		=> skeleton?.Bones.Select( bone => (bone.Name, bone.ParentIndex) ).ToArray() ?? [];

	public static Dictionary<string, float> Build( IReadOnlyList<(string Name, int ParentIndex)> bones, SkeletonDocument.BoneMask mask )
	{
		var result = new Dictionary<string, float>( StringComparer.OrdinalIgnoreCase );

		var count = bones.Count;

		var authored = new float[count];
		Array.Fill( authored, Unset );

		if ( mask is not null )
		{
			foreach ( var weight in mask.Weights )
			{
				var index = IndexOf( bones, weight.Bone );

				if ( index >= 0 )
					authored[index] = weight.Weight.Clamp( 0f, 1f );
			}
		}

		var weights = (float[])authored.Clone();
		var chain = new List<int>();

		for ( var i = count - 1; i > 0; i-- )
		{
			var parent = ParentOf( bones, i );

			if ( weights[i] == Unset )
				FillChain( bones, authored, weights, chain, i, parent );
			else if ( parent >= 0 && weights[parent] == Unset )
				FeatherChain( bones, authored, weights, chain, i, parent );
		}

		for ( var i = 0; i < count; i++ )
		{
			result[bones[i].Name] = weights[i] == Unset ? 0f : weights[i];
		}

		return result;
	}

	/// <summary>
	/// An unweighted bone takes the first weight above it, and the whole chain up to that bone
	/// takes it with them. A chain that reaches a root without finding one is left at zero.
	/// </summary>
	private static void FillChain( IReadOnlyList<(string Name, int ParentIndex)> bones, float[] authored, float[] weights, List<int> chain, int index, int parent )
	{
		chain.Clear();
		chain.Add( index );

		var weight = 0f;

		while ( parent >= 0 )
		{
			if ( authored[parent] != Unset )
			{
				weight = authored[parent];
				break;
			}

			chain.Add( parent );
			parent = ParentOf( bones, parent );
		}

		// A root is nobody's chain to set - it's left alone here and zeroed at the end
		if ( parent < 0 )
			chain.RemoveAt( chain.Count - 1 );

		foreach ( var bone in chain )
		{
			weights[bone] = weight;
		}
	}

	/// <summary>
	/// A weighted bone hanging off unweighted parents ramps up to it, so the mask fades in over
	/// the bones between it and the next weight above.
	/// </summary>
	private static void FeatherChain( IReadOnlyList<(string Name, int ParentIndex)> bones, float[] authored, float[] weights, List<int> chain, int index, int parent )
	{
		var end = weights[index];
		var start = Unset;

		chain.Clear();
		chain.Add( index );

		while ( parent >= 0 )
		{
			chain.Add( parent );

			if ( authored[parent] != Unset )
			{
				start = authored[parent];
				break;
			}

			parent = ParentOf( bones, parent );
		}

		for ( var i = chain.Count - 2; i > 0; i-- )
		{
			var through = (float)i / (chain.Count - 1);

			weights[chain[i]] = start == Unset ? 0f : end + (start - end) * through;
		}
	}

	private static int ParentOf( IReadOnlyList<(string Name, int ParentIndex)> bones, int index ) => bones[index].ParentIndex;

	private static int IndexOf( IReadOnlyList<(string Name, int ParentIndex)> bones, string name )
	{
		if ( string.IsNullOrEmpty( name ) )
			return -1;

		for ( var i = 0; i < bones.Count; i++ )
		{
			if ( string.Equals( bones[i].Name, name, StringComparison.OrdinalIgnoreCase ) )
				return i;
		}

		return -1;
	}
}
