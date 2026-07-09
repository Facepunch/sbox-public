using System.Data;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Sandbox.Hashing;

namespace Sandbox;

public static partial class Json
{
	/// <summary>
	/// Uniquely identifies a tracked object by its type and identifier value.
	/// </summary>
	public record struct ObjectIdentifier
	{
		[JsonInclude]
		public string Type;

		[JsonInclude]
		public string IdValue;
	}

	/// <summary>
	/// Represents a property change to apply during patching.
	/// </summary>
	public record struct PropertyOverride
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
	public record struct AddedObject
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
	public record struct RemovedObject
	{
		/// <summary>The identifier of the object to remove</summary>
		[JsonInclude]
		public ObjectIdentifier Id;
	}

	/// <summary>
	/// Represents an object that should be moved to a new location during patching.
	/// </summary>
	public record struct MovedObject
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
	public class TrackedObjectDefinition
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

		/// <summary>The JSON property key used to identify objects of this type (e.g. "__guid").</summary>
		public string IdProperty;

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
			var fields = requiredFields?.ToArray() ?? Array.Empty<string>();
			var fieldCount = fields.Length;

			return new TrackedObjectDefinition
			{
				Type = type,
				// Return the count of required fields if all required fields are present
				MatchScore = ( jsonObject ) =>
				{
					if ( fieldCount == 0 ) return 0f;

					var matched = 0;
					foreach ( var f in fields )
						if ( jsonObject.ContainsKey( f ) ) matched++;

					// Only match if all required fields are present
					return matched == fieldCount ? fieldCount : 0f;
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
				IdProperty = idProperty,
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

		/// <summary>Hash of the path to this object in the JSON structure</summary>
		public ulong PathHash;

		// Null for leaves — lazy-init avoids one LinkedList allocation per component.
		private LinkedList<TrackedObject> _children;
		public LinkedList<TrackedObject> Children => _children ??= new LinkedList<TrackedObject>();

		/// <summary>Reference to this object's node in parent's Children list (for O(1) removal)</summary>
		public LinkedListNode<TrackedObject> ChildNode;

		/// <summary>
		/// Reconstructs a complete JSON tree from this object and all its children.
		/// </summary>
		public JsonNode ToJson()
		{
			// Data.Parent is null after CopyStrippedData (normal case).
			// Exception: a TrackedObject overwritten in IdToTrackedObject by a later duplicate
			// (contract violation — IDs must be unique). Its Data still points to the source
			// tree; clone it on the spot so we can safely reparent.
			var root = Data.Parent == null ? Data : Data.DeepClone().AsObject();

			if ( _children == null )
				return root;

			foreach ( var child in _children )
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
		public HashSet<ulong> TrackedPaths = new( 128 );
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

			// Skip MatchScore dispatch entirely if the required ID key is absent.
			if ( definition.IdProperty != null && !jsonObject.ContainsKey( definition.IdProperty ) )
				continue;

			var defintionScore = definition.MatchScore( jsonObject );

			if ( defintionScore == 0f )
				continue;

			if ( defintionScore > bestCandidateScore )
			{
				// Fast path: read the ID directly from the known key; validate it's a scalar so
				// null-valued or non-scalar properties don't throw (same error path as ToId).
				string id;
				if ( definition.IdProperty != null )
				{
					if ( jsonObject[definition.IdProperty] is not JsonValue idNode )
					{
						Log.Error( $"Object of type '{definition.Type}' does not have a valid id property '{definition.IdProperty}'" );
						continue;
					}
					id = idNode.GetValue<object>()?.ToString();
				}
				else
				{
					id = definition.ToId != null ? definition.ToId( jsonObject ) : null;
				}

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

		// Pass 1: traverse without cloning — Data references point into the original tree.
		TraverseNode( root, 0UL, definitions, result, null, null, false );

		// Pass 2: replace Data with a fresh stripped copy that owns its own nodes
		// (Parent == null), so ToJson() can reparent them freely.
		foreach ( var (_, trackedObj) in result.IdToTrackedObject )
		{
			trackedObj.Data = CopyStrippedData( trackedObj.Data, trackedObj.PathHash, result.TrackedPaths );
		}

		return result;
	}

	/// <summary>
	/// Builds a new JsonObject containing only the non-tracked own properties of
	/// <paramref name="source"/>. Tracked child paths are omitted; nested non-tracked
	/// containers are copied recursively. The returned object has no parent.
	/// </summary>
	private static JsonObject CopyStrippedData( JsonObject source, ulong pathHash, HashSet<ulong> trackedPaths )
	{
		var copy = new JsonObject();
		foreach ( var (key, value) in source )
		{
			var propHash = HashAppend( pathHash, key );
			if ( trackedPaths.Contains( propHash ) )
			{
				// Preserve tracked arrays as empty so they survive when all children are removed.
				// Tracked direct-object properties are omitted; ToJson() reassigns them.
				if ( value is JsonArray )
					copy[key] = new JsonArray();
			}
			else
			{
				copy[key] = CopyStrippedNode( value, propHash, trackedPaths );
			}
		}
		return copy;
	}

	private static JsonNode CopyStrippedNode( JsonNode node, ulong pathHash, HashSet<ulong> trackedPaths )
	{
		if ( node is JsonObject obj )
			return CopyStrippedData( obj, pathHash, trackedPaths );

		if ( node is JsonArray arr )
		{
			var copy = new JsonArray();
			for ( var i = 0; i < arr.Count; i++ )
				copy.Add( CopyStrippedNode( arr[i], HashAppend( pathHash, i ), trackedPaths ) );
			return copy;
		}

		return node?.DeepClone();
	}

	private static TrackedObject TraverseNode(
		JsonNode node,
		ulong pathHash,
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

			TrackedObject currentTrackedObj = null;
			if ( currentIdentifier.HasValue )
			{
				currentTrackedObj = new TrackedObject
				{
					Id = currentIdentifier.Value,
					Definition = matchedDefintion,
					Data = jsonObject,
					Parent = parent,
					ContainerProperty = containerProperty,
					IsContainedInArray = containerIsArray,
					PathHash = pathHash,
				};
				result.IdToTrackedObject[currentIdentifier.Value] = currentTrackedObj;
				if ( parent != null )
				{
					currentTrackedObj.ChildNode = parent.Children.AddLast( currentTrackedObj );
				}

				// If parent is null set our root
				if ( parent == null )
				{
					result.Root = currentTrackedObj;
				}

				result.TrackedPaths.Add( pathHash );

				if ( matchedDefintion.Atomic )
				{
					// If the object is self contained we don't need to traverse its children
					return currentTrackedObj;
				}
			}

			// Traverse child properties
			foreach ( var (propName, propValue) in jsonObject )
			{
				// Simple values (strings, numbers, bools, null) can't contain tracked
				// objects — skip them entirely.
				if ( propValue is not (JsonObject or JsonArray) )
					continue;

				var newPathHash = HashAppend( pathHash, propName );
				var newParent = currentTrackedObj ?? parent;
				// When descending through a non-tracked container carry the path prefix forward.
				// Guard against a null containerProperty (happens at the tree root) to avoid "null.propName".
				var newContainerProperty = currentTrackedObj != null ? propName
					: containerProperty != null ? $"{containerProperty}.{propName}" : propName;
				TraverseNode(
					propValue,
					newPathHash,
					definitions,
					result,
					newParent,
					newContainerProperty,
					false );
			}

			return currentTrackedObj;
		}
		else if ( node is JsonArray jsonArray )
		{
			TrackedObject previousElement = null;

			for ( int i = 0; i < jsonArray.Count; i++ )
			{
				var item = jsonArray[i];
				var childPathHash = HashAppend( pathHash, i );

				if ( item is JsonObject )
				{
					// Process this object — TraverseNode returns the TrackedObject it created (if any).
					var trackedObj = TraverseNode( item, childPathHash, definitions, result, parent, containerProperty, true );

					// If we found a valid identifier, update its node with previous element info
					if ( trackedObj != null )
					{
						result.TrackedPaths.Add( pathHash );

						// Set the previous element reference
						trackedObj.PreviousElement = previousElement;

						// Current becomes previous for next iteration
						previousElement = trackedObj;
					}
				}
				// We only support objects and value arrays
				// so don't do anything if array contains values or other arrays
			}
		}

		return null;
	}

	/// <summary>
	/// Represents a complete set of changes to be applied to a JSON structure.
	/// </summary>
	/// <remarks>
	/// A patch contains all the operations needed to transform one JSON structure into another
	/// while preserving object identity and relationships.
	/// </remarks>
	public class Patch
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
	public static Patch CalculateDifferences(
		JsonObject oldRoot,
		JsonObject newRoot,
		HashSet<TrackedObjectDefinition> definitions )
	{
		var patch = new Patch();

		// Find objects in old and new JSON structures
		var oldObjects = FindTrackedObjectsInJson( oldRoot, definitions );
		var newObjects = FindTrackedObjectsInJson( newRoot, definitions );

		// Find removed objects
		foreach ( var oldObj in oldObjects.IdToTrackedObject )
		{
			if ( oldObj.Value.Parent == null ) continue;
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
						Data = newObj.Value.Data,
						PreviousElement = newObj.Value.PreviousElement?.Id
					} );
				}
			}
			else
			{
				var oldTrackedObj = oldObjects.IdToTrackedObject[newObj.Key];
				var oldObjValue = oldTrackedObj.Data;

				// Check for new or modified properties
				foreach ( var property in newObj.Value.Data )
				{
					var propName = property.Key;
					var newValue = property.Value;

					var propPathHash = HashAppend( newObj.Value.PathHash, propName );
					if ( oldObjects.TrackedPaths.Contains( propPathHash ) || newObjects.TrackedPaths.Contains( propPathHash ) )
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
				if ( newObj.Value.PreviousElement?.Id != oldTrackedObj.PreviousElement?.Id ||
					newObj.Value.Parent?.Id != oldTrackedObj.Parent?.Id )
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
	public static JsonObject ApplyPatch(
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
				Data = addition.Data.DeepClone().AsObject(),
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
		var addedObjects = new List<TrackedObject>( patch.AddedObjects.Count + patch.MovedObjects.Count );
		foreach ( var a in patch.AddedObjects )
		{
			if ( sourceObjects.IdToTrackedObject.TryGetValue( a.Id, out var o ) && o.Parent != null )
				addedObjects.Add( o );
		}
		foreach ( var m in patch.MovedObjects )
		{
			if ( sourceObjects.IdToTrackedObject.TryGetValue( m.Id, out var o ) && o?.Parent != null )
				addedObjects.Add( o );
		}

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
	/// Combine a parent path hash with a property name segment using XxHash3.
	/// Produces a deterministic 64-bit hash without allocating any strings.
	/// </summary>
	private static ulong HashAppend( ulong parentHash, string segment )
	{
		var bytes = MemoryMarshal.AsBytes( segment.AsSpan() );
		var segmentHash = XxHash3.HashToUInt64( bytes );

		// Mix parent and segment hashes to make order-dependent
		return parentHash * 6364136223846793005UL + segmentHash;
	}

	/// <summary>
	/// Combine a parent path hash with an array index segment.
	/// </summary>
	private static ulong HashAppend( ulong parentHash, int index )
	{
		// Hash the index value directly as bytes — no int.ToString() allocation
		var bytes = MemoryMarshal.AsBytes( new ReadOnlySpan<int>( in index ) );
		var indexHash = XxHash3.HashToUInt64( bytes );

		return parentHash * 6364136223846793005UL + indexHash;
	}
}
