using Editor.MapDoc;
using Editor.MapEditor;
using Sandbox.DataModel;
using Sandbox.Internal;
using System;
using System.Collections.Generic;
using System.Text.Json;

[TestClass]
public class HammerDependencyTests
{
	static readonly Lazy<TypeLibrary> SharedLibrary = new( () =>
	{
		var library = new TypeLibrary();
		library.AddAssembly( typeof( Decal ).Assembly, false );
		return library;
	} );

	// Mock the serialized component properties passed to Hammer's dependency collector.
	// No native map, loaded assets or backend are needed to reproduce the missing references.
	sealed class MockProperty( Type type, object value ) : SerializedProperty
	{
		public override Type PropertyType => type;
		public override T GetValue<T>( T defaultValue = default ) => value is T result ? result : defaultValue;
		public override void SetValue<T>( T value ) => throw new NotSupportedException();
	}

	sealed class MockResource : Resource
	{
		public override bool IsValid => true;
		public MockResource( string path )
		{
			ResourcePath = path;
			GC.SuppressFinalize( this );
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DecalComponentListIncludesEveryDefinition( bool useTypeLibrary )
	{
		var first = new DecalDefinition();
		var second = new DecalDefinition();
		GC.SuppressFinalize( first );
		GC.SuppressFinalize( second );
		var definitions = new List<DecalDefinition> { first, null, second };
		// Call the TypeLibrary implementation used by GetSerialized directly, without its reflection fallback.
		IEnumerable<SerializedProperty> properties = useTypeLibrary
			? SharedLibrary.Value.GetSerializedObject( new Decal { Decals = definitions } )
			: [new MockProperty( typeof( Decal ).GetProperty( nameof( Decal.Decals ) ).PropertyType, definitions )];

		var references = MapWorld.GetReferencedResources( properties ).ToArray();

		CollectionAssert.AreEqual( new Resource[] { first, second }, references );
	}

	[TestMethod]
	public void ResourceArraysIncludeTheirAssetPaths()
	{
		var first = new MockResource( "decals/local.decal" );
		var second = new MockResource( "models/3t/clev/dark_rp_graffiti/thumb/thumb_decal.decal" );
		var property = new MockProperty( typeof( MockResource[] ), new[] { first, null, second } );

		var paths = MapWorld.GetReferencedResources( [property] ).Select( x => x.ResourcePath ).ToArray();

		CollectionAssert.AreEqual( new[] { first.ResourcePath, second.ResourcePath }, paths );
	}

	[TestMethod]
	public void DirectResourcesStillWorkAndEmptyPropertiesAreIgnored()
	{
		var resource = new MockResource( "models/example.vmdl" );
		SerializedProperty[] properties =
		[
			new MockProperty( typeof( MockResource ), resource ),
			new MockProperty( typeof( MockResource ), null ),
			new MockProperty( typeof( List<DecalDefinition> ), null ),
			new MockProperty( typeof( List<DecalDefinition> ), new List<DecalDefinition>() ),
			new MockProperty( typeof( string ), "not-an-asset" ),
			new MockProperty( typeof( int[] ), new[] { 1, 2 } )
		];

		CollectionAssert.AreEqual( new Resource[] { resource }, MapWorld.GetReferencedResources( properties ).ToArray() );
	}

	[TestMethod]
	public void SavingAndReopeningPreservesManualPackageReferences()
	{
		var settings = new Editor.Asset.PublishSettings
		{
			ProjectConfig = new ProjectConfig { PackageReferences = ["Wilson.NewDecalDefinition", "example.addon"] }
		};

		Hammer.UpdatePackageReferences( settings, ["EXAMPLE.ADDON", "example.other_addon"] );
		CollectionAssert.Contains( settings.ProjectConfig.PackageReferences, "wilson.newdecaldefinition" );
		settings = JsonSerializer.Deserialize<Editor.Asset.PublishSettings>( JsonSerializer.Serialize( settings ) );
		Hammer.UpdatePackageReferences( settings, [] );

		// An explicitly configured addon stays even when no entities use it; the automatic one goes.
		CollectionAssert.AreEqual( new[] { "example.addon", "wilson.newdecaldefinition" }, settings.ProjectConfig.PackageReferences );
	}

	[TestMethod]
	public void NewMapsCollectAndDeduplicateAddonReferences()
	{
		var settings = new Editor.Asset.PublishSettings { ProjectConfig = new ProjectConfig() };

		Hammer.UpdatePackageReferences( settings, ["example.addon", "EXAMPLE.ADDON"] );

		CollectionAssert.AreEqual( new[] { "example.addon" }, settings.ProjectConfig.PackageReferences );
	}

	[TestMethod]
	public void RemovedAddonEntitiesDropTheirAutomaticReferencesAfterReopening()
	{
		var settings = new Editor.Asset.PublishSettings { ProjectConfig = new ProjectConfig() };
		Hammer.UpdatePackageReferences( settings, ["example.removed", "example.shared"] );
		settings = JsonSerializer.Deserialize<Editor.Asset.PublishSettings>( JsonSerializer.Serialize( settings ) );

		Hammer.UpdatePackageReferences( settings, ["EXAMPLE.SHARED", "example.added"] );
		CollectionAssert.AreEqual( new[] { "example.added", "example.shared" }, settings.ProjectConfig.PackageReferences );

		Hammer.UpdatePackageReferences( settings, [] );
		Assert.AreEqual( 0, settings.ProjectConfig.PackageReferences.Count );
	}

	[TestMethod]
	public void LegacyReferencesWithUnknownOriginArePreserved()
	{
		var settings = JsonSerializer.Deserialize<Editor.Asset.PublishSettings>( """
			{ "ProjectConfig": { "PackageReferences": ["example.legacy"] } }
			""" );

		Hammer.UpdatePackageReferences( settings, [] );

		CollectionAssert.AreEqual( new[] { "example.legacy" }, settings.ProjectConfig.PackageReferences );
	}

	[TestMethod]
	public void RemovingAManualReferenceDoesNotRestoreItOnSave()
	{
		var settings = new Editor.Asset.PublishSettings
		{
			ProjectConfig = new ProjectConfig { PackageReferences = ["example.manual"] }
		};
		Hammer.UpdatePackageReferences( settings, ["example.automatic"] );
		settings.ProjectConfig.PackageReferences.Remove( "example.manual" );

		Hammer.UpdatePackageReferences( settings, ["example.automatic"] );

		CollectionAssert.AreEqual( new[] { "example.automatic" }, settings.ProjectConfig.PackageReferences );
	}
}
