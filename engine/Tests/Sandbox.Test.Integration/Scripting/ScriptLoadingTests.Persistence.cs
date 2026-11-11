using System.Linq;

namespace ScriptingTests;

/// <summary>
/// Native references participate in the same save and clone paths as script properties.
/// </summary>
[Expose, Hide]
public sealed class PersistenceReferenceHost : Component
{
	/// <summary>
	/// A native reference to a live script component.
	/// </summary>
	[Property]
	public Component Other { get; set; }
}

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Normal flat component persistence preserves references, state, defaults and independent clones after edits.
	/// </summary>
	[TestMethod]
	public void ReloadedComponentsUseCurrentMembersAndRemapReferences()
	{
		Game.TypeLibrary.AddAssembly( typeof( PersistenceReferenceHost ).Assembly, false );
		const string members = """
			[Property] public int Value { get; set; } = 5;
			[Property] public Component Other { get; set; }
			[Property] public Vector3 Offset { get; set; }
			""";
		LoadSource( "persistence.csx", "class PersistedScript : Component { " + members + " }" );
		Json.Initialize();
		scene = new Scene();
		using var scope = scene.Push();
		var type = Game.TypeLibrary.GetType( "PersistedScript" );
		var root = scene.CreateObject();
		var first = root.Components.Create( type );
		var child = scene.CreateObject();
		child.Parent = root;
		var second = child.Components.Create( type );
		var external = scene.CreateObject().Components.Create<PersistenceReferenceHost>();
		external.Other = first;
		type.GetProperty( "Other" ).SetValue( first, second );
		type.GetProperty( "Other" ).SetValue( second, external );
		type.GetProperty( "Value" ).SetValue( first, 321 );
		type.GetProperty( "Offset" ).SetValue( first, new Vector3( 2, 3, 4 ) );
		var firstId = first.Id;
		var secondId = second.Id;
		var saved = scene.Serialize().ToJsonString();
		_ = ReflectionQueryCache.ClonePlan( type.TargetType );
		reloadRoots = [first, external];

		LoadSource( "persistence.csx", "class PersistedScript : Component { " + members + " [Property] public int Added { get; set; } = 9; }" );
		first = (Component)reloadRoots[0];
		external = (PersistenceReferenceHost)reloadRoots[1];
		type = Game.TypeLibrary.GetType( "PersistedScript" );
		type.GetProperty( "Added" ).SetValue( first, 13 );

		var clone = root.Clone();
		var clonedFirst = clone.Components.Get( type.TargetType );
		var clonedSecond = clone.Children.Single().Components.Get( type.TargetType );
		Assert.AreSame( clonedSecond, type.GetProperty( "Other" ).GetValue( clonedFirst ) );
		Assert.AreSame( external, type.GetProperty( "Other" ).GetValue( clonedSecond ) );
		Assert.AreEqual( 13, type.GetProperty( "Added" ).GetValue( clonedFirst ) );
		type.GetProperty( "Value" ).SetValue( clonedFirst, 600 );
		Assert.AreEqual( 321, type.GetProperty( "Value" ).GetValue( first ) );
		clone.Destroy();
		scene.ProcessDeletes();

		var restored = new Scene();
		try
		{
			using var restoredScope = restored.Push();
			restored.Deserialize( Json.ParseToJsonObject( saved ) );
			var loadedFirst = restored.GetAllComponents( type.TargetType ).Single( component => component.Id == firstId );
			var loadedSecond = restored.GetAllComponents( type.TargetType ).Single( component => component.Id == secondId );
			var loadedExternal = restored.GetAllComponents<PersistenceReferenceHost>().Single();
			Assert.AreNotSame( first, loadedFirst );
			Assert.AreEqual( external.Id, loadedExternal.Id );
			Assert.AreSame( loadedFirst, loadedExternal.Other );
			Assert.AreSame( loadedSecond, type.GetProperty( "Other" ).GetValue( loadedFirst ) );
			Assert.AreSame( loadedExternal, type.GetProperty( "Other" ).GetValue( loadedSecond ) );
			Assert.AreEqual( 321, type.GetProperty( "Value" ).GetValue( loadedFirst ) );
			Assert.AreEqual( 9, type.GetProperty( "Added" ).GetValue( loadedFirst ) );
			Assert.AreEqual( new Vector3( 2, 3, 4 ), type.GetProperty( "Offset" ).GetValue( loadedFirst ) );
		}
		finally
		{
			restored.Destroy();
		}
	}

	/// <summary>
	/// Nested script objects use current CLR members and ordinary component references after hotload.
	/// </summary>
	[TestMethod]
	public void ReloadedNestedObjectsUseClrJsonMembers()
	{
		const string dataMembers = "public int Count { get; set; } = 4; public Component Other { get; set; }";
		LoadSource( "nested.csx", "class NestedScriptData { " + dataMembers + " } class NestedScriptOwner : Component { [Property] public NestedScriptData Data { get; set; } = new NestedScriptData(); }" );
		Json.Initialize();
		scene = new Scene();
		using var scope = scene.Push();
		var ownerType = Game.TypeLibrary.GetType( "NestedScriptOwner" );
		var dataType = Game.TypeLibrary.GetType( "NestedScriptData" );
		var component = scene.CreateObject().Components.Create( ownerType );
		var data = ownerType.GetProperty( "Data" ).GetValue( component );
		dataType.GetProperty( "Count" ).SetValue( data, 42 );
		dataType.GetProperty( "Other" ).SetValue( data, component );
		var saved = scene.Serialize().ToJsonString();
		Assert.AreEqual( 42, component.Serialize()["Data"]["Count"].GetValue<int>() );
		reloadRoots = [component, data];

		LoadSource( "nested.csx", "class NestedScriptData { " + dataMembers + " public int Added { get; set; } = 9; }" );
		component = (Component)reloadRoots[0];
		data = reloadRoots[1];
		ownerType = Game.TypeLibrary.GetType( "NestedScriptOwner" );
		dataType = Game.TypeLibrary.GetType( "NestedScriptData" );
		Assert.AreEqual( 9, component.Serialize()["Data"]["Added"].GetValue<int>() );
		var clone = component.GameObject.Clone();
		var clonedComponent = clone.Components.Get( ownerType.TargetType );
		var clonedData = ownerType.GetProperty( "Data" ).GetValue( clonedComponent );
		Assert.AreNotSame( data, clonedData );
		Assert.AreEqual( 42, dataType.GetProperty( "Count" ).GetValue( clonedData ) );
		Assert.AreEqual( 9, dataType.GetProperty( "Added" ).GetValue( clonedData ) );
		Assert.AreSame( clonedComponent, dataType.GetProperty( "Other" ).GetValue( clonedData ) );

		var restored = new Scene();
		try
		{
			using var restoredScope = restored.Push();
			restored.Deserialize( Json.ParseToJsonObject( saved ) );
			var loadedComponent = restored.GetAllComponents( ownerType.TargetType ).Single();
			var loadedData = ownerType.GetProperty( "Data" ).GetValue( loadedComponent );
			Assert.AreEqual( 42, dataType.GetProperty( "Count" ).GetValue( loadedData ) );
			Assert.AreEqual( 9, dataType.GetProperty( "Added" ).GetValue( loadedData ) );
			Assert.AreSame( loadedComponent, dataType.GetProperty( "Other" ).GetValue( loadedData ) );
		}
		finally
		{
			restored.Destroy();
		}
	}

}
