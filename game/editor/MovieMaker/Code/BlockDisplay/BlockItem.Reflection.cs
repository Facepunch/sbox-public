using System.Diagnostics.CodeAnalysis;
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Properties;

namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

partial class BlockItem
{
	public static BlockItem Create( TimelineTrack parent, ITrackBlock block, MovieTime offset )
	{
		var blockType = block.GetType();
		var propertyType = (block as IPropertyBlock)?.PropertyType;

		var inst = (BlockItem)Activator.CreateInstance( GetBlockItemType( blockType, propertyType ) )!;

		try
		{
			inst.Initialize( parent, block, offset );
		}
		catch ( Exception ex )
		{
			Log.Error( ex );

			BlockItemTypeCache[blockType] = typeof(DefaultBlockItem);

			inst = new DefaultBlockItem();
			inst.Initialize( parent, block, offset );
		}

		return inst;
	}

	[SkipHotload] private static Dictionary<Type, Type> BlockItemTypeCache { get; } = new();

	[EditorEvent.Hotload]
	private static void OnHotload()
	{
		BlockItemTypeCache.Clear();
	}

	private static Type GetBlockItemType( Type targetBlockType, Type? propertyType )
	{
		if ( BlockItemTypeCache.TryGetValue( targetBlockType, out var blockItemType ) ) return blockItemType;

		if ( propertyType is not null && BindingReference.GetUnderlyingType( propertyType ) is { } underlyingType )
		{
			return BlockItemTypeCache[targetBlockType] = typeof(ReferenceBlockItem<>).MakeGenericType( underlyingType );
		}

		var bestBlockItemType = typeof(DefaultBlockItem);
		var bestScore = int.MaxValue;

		foreach ( var typeDesc in EditorTypeLibrary.GetTypes<BlockItem>() )
		{
			if ( typeDesc.TargetType == typeof(ReferenceBlockItem<>) ) continue;

			var type = typeDesc.TargetType;
			var baseDistance = 0;

			if ( type.IsAbstract ) continue;
			if ( type.IsGenericType )
			{
				if ( propertyType is null ) continue;

				if ( !TryMakeGenericType( type, propertyType, out var newType ) )
				{
					continue;
				}

				type = newType;
				baseDistance = 1;
			}

			var score = baseDistance + GetScore( type, targetBlockType, propertyType );

			if ( score > bestScore ) continue;

			bestBlockItemType = type;
			bestScore = score;
		}

		return BlockItemTypeCache[targetBlockType] = bestBlockItemType;
	}

	private static bool TryMakeGenericType( Type trackPreviewType, Type propertyType,
		[NotNullWhen( true )] out Type? newTrackPreviewType )
	{
		newTrackPreviewType = null;

		if ( trackPreviewType.GetGenericArguments().Length != 1 )
		{
			return false;
		}

		try
		{
			newTrackPreviewType = trackPreviewType.MakeGenericType( propertyType );
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static int GetScore( Type blockItemType, Type targetBlockType, Type? propertyType )
	{
		var score = int.MaxValue;

		foreach ( var iFace in blockItemType.GetInterfaces() )
		{
			if ( !iFace.IsConstructedGenericType ) continue;

			if ( iFace.GetGenericTypeDefinition() == typeof(IBlockItem<>) )
			{
				var iFaceTargetType = iFace.GetGenericArguments()[0];

				score = Math.Min( score, GetDistance( iFaceTargetType, targetBlockType ) );
			}

			if ( iFace.GetGenericTypeDefinition() == typeof(IPropertyBlockItem<>) && propertyType != null )
			{
				var iFaceTargetType = iFace.GetGenericArguments()[0];

				score = Math.Min( score, GetDistance( iFaceTargetType, propertyType ) );
			}
		}
		return score;
	}

	private static int GetDistance( Type baseType, Type? derivedType )
	{
		if ( baseType == typeof(object) ) return int.MaxValue - 1;
		if ( !baseType.IsAssignableFrom( derivedType ) ) return int.MaxValue;
		if ( baseType.IsInterface && !derivedType.IsInterface ) return 1;

		var distance = 0;

		while ( baseType != derivedType && derivedType != null )
		{
			derivedType = derivedType.BaseType;
			distance++;
		}

		return distance;
	}
}
