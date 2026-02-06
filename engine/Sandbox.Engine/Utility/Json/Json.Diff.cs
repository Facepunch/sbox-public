using System.Data;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static Sandbox.Json;

namespace Sandbox;

public static partial class Json
{
	/// <summary>
	/// Uniquely identifies a tracked object by its type and identifier value.
	/// </summary>
	internal record struct ObjectIdentifier
	{
		[JsonInclude]
		public string Type;

		[JsonInclude]
		public string IdValue;
	}

	/// <summary>
	/// Represents a property change to apply during patching.
	/// </summary>
	internal record struct PropertyOverride
	{
		/// <summary>The object whose property should be modified</summary>
		[JsonInclude]
		public ObjectIdentifier Target;

		/// <summary>The name of the property to modify</summary>
		[JsonInclude]
		public string Property;

		/// <summary>The new value to assign to the property</summary>
		[JsonInclude]
		public JsonNode Value;
	}

	/// <summary>
	/// Represents an object that needs to be added during patching.
	/// </summary>
	internal record struct AddedObject
	{
		/// <summary>The identifier for the new object</summary>
		[JsonInclude]
		public ObjectIdentifier Id;

		/// <summary>The parent object that will contain this object</summary>
		[JsonInclude]
		public ObjectIdentifier Parent;

		/// <summary>The previous sibling when adding to an array (null if first or not in array)</summary>
		[JsonInclude]
		public ObjectIdentifier? PreviousElement;

		/// <summary>The property name in the parent that will contain this object</summary>
		[JsonInclude]
		public string ContainerProperty;

		/// <summary>Whether this object is being added to an array (true) or as a direct property (false)</summary>
		[JsonInclude]
		public bool IsContainerArray;

		/// <summary>The data for the new object</summary>
		[JsonInclude]
		public JsonObject Data;
	}

	/// <summary>
	/// Represents an object that should be removed during patching.
	/// </summary>
	internal record struct RemovedObject
	{
		/// <summary>The identifier of the object to remove</summary>
		[JsonInclude]
		public ObjectIdentifier Id;
	}

	/// <summary>
	/// Represents an object that should be moved to a new location during patching.
	/// </summary>
	internal record struct MovedObject
	{
		/// <summary>The identifier of the object to move</summary>
		[JsonInclude]
		public ObjectIdentifier Id;

		/// <summary>The new parent object</summary>
		[JsonInclude]
		public ObjectIdentifier NewParent;

		/// <summary>The property name in the new parent that will contain this object</summary>
		[JsonInclude]
		public string NewContainerProperty;

		/// <summary>Whether the object is being moved to an array (true) or as a direct property (false)</summary>
		[JsonInclude]
		public bool IsNewContainerArray;

		/// <summary>The previous sibling in the new location (null if first or not in array)</summary>
		[JsonInclude]
		public ObjectIdentifier? NewPreviousElement;
	}

	/// <summary>
	/// Defines characteristics of an object type that should be tracked within a JSON tree structure.
	/// These definitions are used to identify, track, and manage specific types of objects during JSON diffing and patching operations.
	/// </summary>
	internal class TrackedObjectDefinition
	{
		/// <summary>
		/// A unique identifier for this object type. This is used to categorize objects.
		/// </summary>
		public string Type;

		/// <summary>
		/// Determines whether a JSON object should be considered an instance of this tracked object type.
		/// </summary>
		/// <remarks>
		/// The function returns a float value indicating how well the JSON object matches this definition.
		/// A return value of 0 indicates no match, while higher values indicate stronger matches.
		/// This allows for heuristic-based matching when exact matches aren't possible.
		/// </remarks>
		public Func<JsonObject, float> MatchScore;

		/// <summary>
		/// Maps a JSON object to a unique identifier string.
		/// </summary>
		/// <remarks>
		/// The identifier could be derived from a specific property, a combination of properties, or a computed hash.
		/// It's critical that this function:
		/// 1. Produces a truly unique value for each distinct object of this type
		/// 2. Never maps two different objects to the same ID
		/// 3. Is deterministic - always returns the same ID when applied to the same object
		/// 
		/// If you can just use a UUID or other guaranteed unique identifier.
		/// </remarks>
		public Func<JsonObject, string> ToId;

		/// <summary>
		/// Specifies the required type of the parent object. If null, AllowedAsRoot must be true.
		/// </summary>
		/// <remarks>
		/// This enforces type hierarchy constraints within the JSON structure.
		/// </remarks>
		public string ParentType;

		/// <summary>
		/// If true, objects of this type can be the root of the object tree.
		/// </summary>
		/// <remarks>
		/// Root objects don't require a parent, and they don't need an ID since there can only be one root.
		/// If AllowedAsRoot is false, ParentType must be specified.
		/// </remarks>
		public bool AllowedAsRoot;

		/// <summary>
		/// When true, treats this object as an atomic unit during tracking operations.
		/// </summary>
		/// <remarks>
		/// Objects with AtomicTracking enabled:
		/// 1. Have their children excluded from individual tracking
		/// 2. Skip property-level diffing (changes are handled as whole object replacements)
		/// 3. Are treated as "black boxes" where internal structure is ignored
		/// 
		/// This is useful for:
		/// - Objects containing data that shouldn't be tracked independently (like patches)
		/// - Preventing recursive tracking of complex nested structures
		/// </remarks>
		public bool Atomic;

		public HashSet<string> IgnoredProperties;

		/// <summary>
		/// Creates a TrackedObjectDefinition that identifies objects based on the presence of specific fields.
		/// </summary>
		internal static TrackedObjectDefinition CreatePresenceBasedDefinition(
			string type,
			IEnumerable<string> requiredFields,
			string idProperty = null,
			string parentType = null,
			bool allowedAsRoot = false,
			bool atomic = false,
			IEnumerable<string> ignoredProperties = null )
		{
			return new TrackedObjectDefinition
			{
				Type = type,
				// Return the count of required fields if all required fields are present
				MatchScore = ( jsonObject ) =>
				{
					if ( idProperty != null && !jsonObject.ContainsKey( idProperty ) ) return 0f;

					if ( requiredFields == null || requiredFields.Count() == 0 ) return 0f;

					var matchingRequiredFields = requiredFields.Count( jsonObject.ContainsKey );

					// Only match if all required fields are present
					return matchingRequiredFields == requiredFields.Count() ? requiredFields.Count() : 0f;
				},
				// Extract the ID from the specified property
				ToId = ( jsonObject ) =>
				{
					if ( idProperty == null ) return null;
					if ( !jsonObject.TryGetPropertyValue( idProperty, out var idValue ) )
					{
						Log.Error( $"Object of type '{type}' does not have a valid id property '{idProperty}'" );
						return null;
					}
					return idValue.AsValue().GetValue<object>().ToString();
				},
				ParentType = parentType,
				AllowedAsRoot = allowedAsRoot,
				Atomic = atomic,
				IgnoredProperties = ignoredProperties is null ? new HashSet<string>() : ignoredProperties.ToHashSet()
			};
		}
	}

	/// <summary>
	/// Represents a tracked object in a JSON tree with metadata for diffing and patching operations.
	/// </summary>
	private class TrackedObject
	{
		/// <summary>The unique identifier for this object</summary>
		public ObjectIdentifier Id;

		/// <summary>The defintion taht was used to track this object.</summary>
		public TrackedObjectDefinition Definition;

		/// <summary>The object's JSON data without its children</summary>
		public JsonObject Data;

		/// <summary>Reference to this object's parent (null for root objects)</summary>
		public TrackedObject Parent;

		/// <summary>The property name in parent that contains this object</summary>
		public string ContainerProperty;

		/// <summary>Whether this object is contained in an array (true) or as a direct property (false)</summary>
		public bool IsContainedInArray;

		/// <summary>The previous sibling element when contained in an array (null if first or not in array)</summary>
		public TrackedObject PreviousElement;

		/// <summary>The path to this object in the JSON structure</summary>
		public string Path;

		/// <summary>Child objects belonging to this object</summary>
		public LinkedList<TrackedObject> Children = new();

		/// <summary>Reference to this object's node in parent's Children list (for O(1) removal)</summary>
		public LinkedListNode<TrackedObject> ChildNode;

		/// <summary>
		/// Reconstructs a complete JSON tree from this object and all its children.
		/// </summary>
		public JsonNode ToJson()
		{
			var root = Data.DeepClone().AsObject();

			foreach ( var child in Children )
			{
				var pathSegments = child.ContainerProperty.Split( '.' );
				var currentObject = root;  // Start from the root for each child

				// Navigate to the correct container, creating objects as needed
				for ( var i = 0; i < pathSegments.Length - 1; i++ )
				{
					var pathSegment = pathSegments[i];
					if ( !currentObject.ContainsKey( pathSegment ) )
					{
						currentObject[pathSegment] = new JsonObject();
					}
					currentObject = currentObject[pathSegment].AsObject();
				}

				// Handle the final path segment
				var finalSegment = pathSegments[pathSegments.Length - 1];
				if ( child.IsContainedInArray )
				{
					if ( !currentObject.ContainsKey( finalSegment ) )
					{
						currentObject[finalSegment] = new JsonArray();
					}
					var parentArray = currentObject[finalSegment].AsArray();
					parentArray.Add( child.ToJson() );
				}
				else
				{
					currentObject[finalSegment] = child.ToJson();
				}
			}

			return root;
		}
	}

	private class TrackedObjects
	{
		public TrackedObject Root;
		public Dictionary<ObjectIdentifier, TrackedObject> IdToTrackedObject = new( 128 );
		public HashSet<string> TrackedPaths = new( 128 );
	}

	private static (ObjectIdentifier?, TrackedObjectDefinition) TryGetObjectIdentifier(
		JsonObject jsonObject,
		string parentType,
		IEnumerable<TrackedObjectDefinition> definitions )
	{
		ObjectIdentifier? bestCandidate = null;
		TrackedObjectDefinition bestDefinition = null;
		var bestCandidateScore = 0f;

		foreach ( var definition in definitions )
		{
			if ( !definition.AllowedAsRoot && parentType == null )
				continue;

			if ( !definition.AllowedAsRoot && string.IsNullOrEmpty( definition.ParentType ) )
			{
				Log.Warning( $"Object definition '{definition.Type}' is not allowed as root, but has no owner type" );
			}

			if ( parentType != null && string.IsNullOrEmpty( definition.ParentType ) )
				continue;

			if ( !string.IsNullOrEmpty( definition.ParentType ) && parentType == null && !definition.AllowedAsRoot )
				continue;

			if ( !string.IsNullOrEmpty( definition.ParentType ) && !definition.ParentType.Equals( parentType, StringComparison.OrdinalIgnoreCase ) && !definition.AllowedAsRoot )
				continue;

			var defintionScore = definition.MatchScore( jsonObject );

			if ( defintionScore == 0f )
				continue;

			if ( defintionScore > bestCandidateScore )
			{
				var id = definition.ToId( jsonObject );

				// We allow an empty ids only root level objects
				if ( id == null && !definition.AllowedAsRoot )
				{
					Log.Error( $"Object of type '{definition.Type}' does not have a valid id" );
					continue;
				}

				bestCandidate = new ObjectIdentifier
				{
					Type = definition.Type,
					IdValue = id,
				};
				bestCandidateScore = defintionScore;
				bestDefinition = definition;
			}
		}

		return (bestCandidate, bestDefinition);
	}

	private static TrackedObjects FindTrackedObjectsInJson(
		JsonObject root,
		HashSet<TrackedObjectDefinition> definitions )
	{
		var result = new TrackedObjects();

		if ( root is null )
		{
			return result;
		}

		var clonedRoot = root.DeepClone().AsObject();

		TraverseNode( clonedRoot, "", definitions, result, null, null, false );

		// Sanitize objects to remove tracked objects
		foreach ( var (objId, trackedObj) in result.IdToTrackedObject )
		{
			if ( trackedObj.Definition.Atomic ) continue;
			trackedObj.Data = StripNestedObjects( trackedObj, result.TrackedPaths );
		}

		return result;
	}

	private static void TraverseNode(
		JsonNode node,
		string path,
		HashSet<TrackedObjectDefinition> definitions,
		TrackedObjects result,
		TrackedObject parent,
		string containerProperty,
		bool containerIsArray )
	{
		if ( node is JsonObject jsonObject )
		{
			// Get the parent type if available
			string parentType = parent?.Id.Type;

			// Try to get an object identifier
			var (currentIdentifier, matchedDefintion) = TryGetObjectIdentifier( jsonObject, parentType, definitions );

			if ( currentIdentifier.HasValue )
			{
				var trackedObj = new TrackedObject
				{
					Id = currentIdentifier.Value,
					Definition = matchedDefintion,
					Data = jsonObject,
					Parent = parent,
					ContainerProperty = containerProperty,
					IsContainedInArray = containerIsArray,
					Path = path,
				};
				result.IdToTrackedObject[currentIdentifier.Value] = trackedObj;
				if ( parent != null )
				{
					trackedObj.ChildNode = parent.Children.AddLast( trackedObj );
				}

				// If parent is null set our root
				if ( parent == null )
				{
					result.Root = result.IdToTrackedObject[currentIdentifier.Value];
				}

				result.TrackedPaths.Add( path );

				if ( matchedDefintion.Atomic )
				{
					// If the object is self contained we don't need to traverse its children
					return;
				}
			}

			// Traverse child properties
			foreach ( var (propName, propValue) in jsonObject )
			{
				var newPath = AppendToPath( path, propName );
				var newParent = currentIdentifier.HasValue && result.IdToTrackedObject.ContainsKey( currentIdentifier.Value ) ? result.IdToTrackedObject[currentIdentifier.Value] : parent;
				// Reset containerproperty name if we found a tracked object
				var newContainerProperty = currentIdentifier.HasValue ? propName : $"{containerProperty}.{propName}";
				TraverseNode(
					propValue,
					newPath,
					definitions,
					result,
					newParent,
					newContainerProperty,
					false );
			}
		}
		else if ( node is JsonArray jsonArray )
		{
			TrackedObject previousElement = null;

			for ( int i = 0; i < jsonArray.Count; i++ )
			{
				var item = jsonArray[i];
				var childPath = AppendToPath( path, i );

				if ( item is JsonObject jsonArrayObject )
				{
					// Try to get identifier for this object
					var (elementId, _) = TryGetObjectIdentifier( jsonArrayObject, parent?.Id.Type, definitions );

					// Process this object
					TraverseNode( item, childPath, definitions, result, parent, containerProperty, true );

					// If we found a valid identifier, update its node with previous element info
					if ( elementId.HasValue && result.IdToTrackedObject.TryGetValue( elementId.Value, out var trackedObj ) )
					{
						result.TrackedPaths.Add( path );

						// Set the previous element reference
						trackedObj.PreviousElement = previousElement;

						// Current becomes previous for next iteration
						previousElement = result.IdToTrackedObject[elementId.Value];
					}
				}
				// We only support objects and value arrays
				// so don't do anything if array contains values or other arrays
			}
		}
	}

	/// <summary>
	/// Represents a complete set of changes to be applied to a JSON structure.
	/// </summary>
	/// <remarks>
	/// A patch contains all the operations needed to transform one JSON structure into another
	/// while preserving object identity and relationships.
	/// </remarks>
	internal class Patch
	{
		/// <summary>
		/// Objects that need to be added to the target structure.
		/// </summary>
		[JsonInclude]
		public List<AddedObject> AddedObjects { get; set; } = new List<AddedObject>( 16 );

		/// <summary>
		/// Objects that need to be removed from the target structure.
		/// </summary>
		[JsonInclude]
		public List<RemovedObject> RemovedObjects { get; set; } = new List<RemovedObject>( 16 );

		/// <summary>
		/// Property values that need to be changed on existing objects.
		/// </summary>
		[JsonInclude]
		public List<PropertyOverride> PropertyOverrides { get; set; } = new List<PropertyOverride>( 32 );

		/// <summary>
		/// Objects that need to be moved to a different location in the structure.
		/// </summary>
		[JsonInclude]
		public List<MovedObject> MovedObjects { get; set; } = new List<MovedObject>( 16 );
	}

	/// <summary>
	/// Compares two JSON object trees and calculates the differences between them.
	/// </summary>
	/// <param name="oldRoot">The original JSON object tree</param>
	/// <param name="newRoot">The updated JSON object tree</param>
	/// <param name="definitions">Set of definitions for tracked object types in the JSON structure</param>
	/// <returns>A Patch object containing all changes needed to transform oldRoot into newRoot</returns>
	internal static Patch CalculateDifferences(
		JsonObject oldRoot,
		JsonObject newRoot,
		HashSet<TrackedObjectDefinition> definitions )
	{
		var patch = new Patch();

		// Find objects in old and new JSON structures
		var oldObjects = FindTrackedObjectsInJson( oldRoot, definitions );
		var newObjects = FindTrackedObjectsInJson( newRoot, definitions );

		// Find removed objects
		foreach ( var oldObj in oldObjects.IdToTrackedObject.Where( o => o.Value.Parent != null ) )
		{
			if ( !newObjects.IdToTrackedObject.ContainsKey( oldObj.Key ) )
			{
				patch.RemovedObjects.Add( new RemovedObject
				{
					Id = oldObj.Key
				} );
			}
		}

		// Find added objects and property overrides
		foreach ( var newObj in newObjects.IdToTrackedObject )
		{
			if ( !oldObjects.IdToTrackedObject.ContainsKey( newObj.Key ) )
			{
				// Object is in new but not in old
				if ( newObj.Value.Parent != null )
				{
					patch.AddedObjects.Add( new AddedObject
					{
						Id = newObj.Key,
						Parent = newObj.Value.Parent.Id,
						ContainerProperty = newObj.Value.ContainerProperty,
						IsContainerArray = newObj.Value.IsContainedInArray,
						Data = newObj.Value.Data.DeepClone().AsObject(),
						PreviousElement = newObj.Value.PreviousElement?.Id
					} );
				}
			}
			else
			{
				// Check for new or modified properties
				foreach ( var property in newObj.Value.Data )
				{
					var oldObjValue = oldObjects.IdToTrackedObject[newObj.Key].Data;

					var propName = property.Key;
					var newValue = property.Value;

					if ( oldObjects.TrackedPaths.Contains( AppendToPath( newObj.Value.Path, propName ) ) || newObjects.TrackedPaths.Contains( AppendToPath( newObj.Value.Path, propName ) ) )
					{
						// Skip tracked properties
						continue;
					}

					if ( newObj.Value.Definition.Atomic )
					{
						// Skip property overrides self contained objects
						continue;
					}

					if ( newObj.Value.Definition.IgnoredProperties.Contains( propName ) )
					{
						continue;
					}

					if ( oldObjValue.TryGetPropertyValue( propName, out var oldValue ) )
					{
						// Property exists in both - check for differences
						if ( !JsonNode.DeepEquals( oldValue, newValue ) )
						{
							patch.PropertyOverrides.Add( new PropertyOverride
							{
								Target = newObj.Key,
								Property = propName,
								Value = newValue?.DeepClone()
							} );
						}
					}
					else if ( newValue != null || oldObjValue != null )
					{
						// Property is new and not null
						patch.PropertyOverrides.Add( new PropertyOverride
						{
							Target = newObj.Key,
							Property = propName,
							Value = newValue?.DeepClone()
						} );
					}
				}

				// Check if object has moved (different parent or different position in array)
				if ( newObj.Value.PreviousElement?.Id != oldObjects.IdToTrackedObject[newObj.Key].PreviousElement?.Id ||
					newObj.Value.Parent?.Id != oldObjects.IdToTrackedObject[newObj.Key].Parent?.Id )
				{
					patch.MovedObjects.Add( new MovedObject
					{
						Id = newObj.Key,
						NewParent = newObj.Value.Parent.Id,
						NewContainerProperty = newObj.Value.ContainerProperty,
						IsNewContainerArray = newObj.Value.IsContainedInArray,
						NewPreviousElement = newObj.Value.PreviousElement?.Id
					} );
				}
			}
		}

		return patch;
	}

	private static JsonObject StripNestedObjects(
		TrackedObject original,
		HashSet<string> trackedPaths )
	{
		var sanitized = original.Data;
		RemoveTrackedObjects( sanitized, original.Path, trackedPaths );
		return sanitized;
	}

	private static void RemoveTrackedObjects(
		JsonNode node,
		string path,
		HashSet<string> trackedPaths )
	{
		if ( node is JsonObject jsonObject )
		{
			// Process all properties of the object
			foreach ( var property in jsonObject.ToList() )
			{
				var propName = property.Key;
				var propValue = property.Value;
				var propPath = AppendToPath( path, propName );

				if ( propValue is JsonObject propObject )
				{
					// Check if the object is tracked
					if ( trackedPaths.Contains( propPath ) )
					{
						jsonObject.Remove( propName );
						continue;
					}

					// Recursively process this object if it's not tracked itself
					RemoveTrackedObjects( propObject, propPath, trackedPaths );
				}
				else if ( propValue is JsonArray propArray )
				{
					// Check if the array itself is tracked
					if ( trackedPaths.Contains( propPath ) )
					{
						propArray.Clear();
						continue;
					}

					// Check array items (only if containing objects)
					for ( int i = propArray.Count - 1; i >= 0; i-- )
					{
						if ( propArray[i] is JsonObject arrayObj )
						{
							var itemPath = AppendToPath( propPath, i );
							if ( trackedPaths.Contains( itemPath ) )
							{
								// Remove tracked array items
								propArray.RemoveAt( i );
							}
							else
							{
								// Recursively process untracked objects in the array
								RemoveTrackedObjects( arrayObj, itemPath, trackedPaths );
							}
						}
					}
				}
			}
		}
	}

	/// <summary>
	/// Applies a patch to transform a JSON object tree, with support for partial patch application
	/// when the source tree has been modified after the patch was created.
	/// </summary>
	/// <param name="sourceRoot">The JSON object tree to modify</param>
	/// <param name="patch">The patch containing all changes to apply</param>
	/// <param name="definitions">Set of definitions for tracked object types</param>
	/// <returns>A new JSON object tree with all applicable changes applied</returns>
	/// <remarks>
	/// Partial patch application semantics:
	/// 
	/// Object Removal:
	/// - Skipped if object doesn't exist in source
	/// - Proceeds if object exists even if parent has changed
	/// 
	/// Object Addition:
	/// - Only added if parent exists in source
	/// - Skipped if parent is missing
	/// 
	/// Object Moves:
	/// - Requires both object and target parent to exist
	/// - Object is removed if target parent doesn't exist
	/// 
	/// Property Overrides:
	/// - Only applied if target object exists
	/// 
	/// Array Ordering:
	/// - Best effort based on neighbourhood information (previous element)
	/// - Objects without previous elements are placed at start
	/// 
	/// Operations are processed in this order: removals, additions, moves,
	/// reordering, and finally property overrides.
	/// </remarks>
	internal static JsonObject ApplyPatch(
		JsonObject sourceRoot,
		Patch patch,
		HashSet<TrackedObjectDefinition> definitions )
	{
		var sourceTrackedObjects = FindTrackedObjectsInJson( sourceRoot, definitions );

		// Removals are easy just nuke them from our object tree
		foreach ( var removal in patch.RemovedObjects )
		{
			var removedObject = sourceTrackedObjects.IdToTrackedObject.GetValueOrDefault( removal.Id );

			if ( removedObject == null ) continue;

			// check if parent still exists
			if ( removedObject.Parent != null && removedObject.ChildNode != null )
			{
				removedObject.Parent.Children.Remove( removedObject.ChildNode );
				removedObject.ChildNode = null;
			}
			sourceTrackedObjects.IdToTrackedObject.Remove( removedObject.Id );
		}

		// Register all objects that will be added to our tree later
		// We need their references to be avialable early
		// As we might need to move obejcts into their children

		// add objects to the source objects
		foreach ( var addition in patch.AddedObjects )
		{
			sourceTrackedObjects.IdToTrackedObject[addition.Id] = new TrackedObject
			{
				Id = addition.Id,
				Data = addition.Data,
				ContainerProperty = addition.ContainerProperty,
				IsContainedInArray = addition.IsContainerArray,
			};

		}

		// Second pass to set parents and prev and handle additions
		// need a second pass, because we can only start doing this once all references are available
		foreach ( var added in patch.AddedObjects )
		{
			var addedObject = sourceTrackedObjects.IdToTrackedObject[added.Id];
			addedObject.Parent = sourceTrackedObjects.IdToTrackedObject.GetValueOrDefault( added.Parent );
			addedObject.PreviousElement = added.PreviousElement.HasValue ? sourceTrackedObjects.IdToTrackedObject.GetValueOrDefault( added.PreviousElement.Value ) : null;
			// Add to parent if it still exists
			if ( addedObject.Parent != null )
			{
				addedObject.ChildNode = addedObject.Parent.Children.AddLast( addedObject );
			}
		}

		// Next handle moves
		foreach ( var move in patch.MovedObjects )
		{
			var movedObject = sourceTrackedObjects.IdToTrackedObject.GetValueOrDefault( move.Id );

			if ( movedObject == null ) continue;

			// If the parent is null, we can't move it
			if ( movedObject.Parent == null ) continue;

			var newParentObject = sourceTrackedObjects.IdToTrackedObject.GetValueOrDefault( move.NewParent );

			if ( newParentObject != null )
			{
				// We can perform the move - use ChildNode for O(1) removal
				if ( movedObject.ChildNode != null )
				{
					movedObject.Parent.Children.Remove( movedObject.ChildNode );
				}
				movedObject.Parent = newParentObject;
				movedObject.ContainerProperty = move.NewContainerProperty;
				movedObject.IsContainedInArray = move.IsNewContainerArray;
				movedObject.PreviousElement = move.NewPreviousElement.HasValue ? sourceTrackedObjects.IdToTrackedObject.GetValueOrDefault( move.NewPreviousElement.Value ) : null;
				movedObject.ChildNode = movedObject.Parent.Children.AddLast( movedObject );
			}
			else
			{
				// Target parent doesn't exist, remove the object entirely
				if ( movedObject.ChildNode != null )
				{
					movedObject.Parent.Children.Remove( movedObject.ChildNode );
					movedObject.ChildNode = null;
				}
				sourceTrackedObjects.IdToTrackedObject.Remove( movedObject.Id );
			}
		}

		ReorderAddedObjects( patch, sourceTrackedObjects );

		// Last apply property overrides
		foreach ( var propertyOverride in patch.PropertyOverrides )
		{
			if ( sourceTrackedObjects.IdToTrackedObject.TryGetValue( propertyOverride.Target, out var trackedObj ) )
			{
				trackedObj.Data[propertyOverride.Property] = propertyOverride.Value?.DeepClone();
			}
		}

		return sourceTrackedObjects.Root.ToJson().AsObject();
	}

	private static void ReorderAddedObjects( Patch patch, TrackedObjects sourceObjects )
	{
		// Get objects that need reordering (added + moved, with valid parents)
		// Materialize to avoid re-evaluating LINQ on each iteration
		var addedObjects = patch.AddedObjects
			.Select( a => sourceObjects.IdToTrackedObject[a.Id] )
			.Concat( patch.MovedObjects.Select( m => sourceObjects.IdToTrackedObject.GetValueOrDefault( m.Id ) ) )
			.Where( o => o?.Parent != null )
			.ToList();

		// Keep reordering until stable - objects may depend on each other's positions
		// Limit iterations to prevent infinite loops from unresolvable conflicts
		int maxIterations = addedObjects.Count + 1;
		for ( var iteration = 0; iteration < maxIterations; iteration++ )
		{
			var changed = false;

			foreach ( var obj in addedObjects )
			{
				var parent = obj.Parent;
				if ( parent == null )
					continue;

				var prevNode = obj.PreviousElement?.ChildNode;

				// Already in correct position?
				if ( prevNode != null && obj.ChildNode?.Previous == prevNode )
					continue;

				if ( prevNode == null && obj.ChildNode == parent.Children.First )
					continue;

				// Remove from current position
				if ( obj.ChildNode != null )
					parent.Children.Remove( obj.ChildNode );

				// Insert at correct position
				if ( prevNode != null )
					obj.ChildNode = parent.Children.AddAfter( prevNode, obj );
				else
					obj.ChildNode = parent.Children.AddFirst( obj );

				changed = true;
			}

			if ( !changed )
				break;
		}
	}

	/// <summary>
	/// Helper method to append a property name to a path string
	/// </summary>
	private static string AppendToPath( string path, string property )
	{
		if ( string.IsNullOrEmpty( path ) )
			return property;

		return string.Concat( path, ".", property );
	}

	/// <summary>
	/// Helper method to append an array index to a path string
	/// </summary>
	private static string AppendToPath( string path, int index )
	{
		if ( string.IsNullOrEmpty( path ) )
			return index.ToString();

		return string.Concat( path, ".", index.ToString() );
	}
}
