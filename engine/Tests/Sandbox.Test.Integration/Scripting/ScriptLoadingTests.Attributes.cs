using System;
using System.Linq;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Script declarations accept enum constants and named arguments in attributes, and script enums
	/// carry display attributes on the enum and its members.
	/// </summary>
	[TestMethod]
	public void ScriptAttributesAcceptEnumAndNamedArguments()
	{
		var loaded = Game.Scripting.LoadFile( "attribute-arguments.csx", """
			/// <summary>
			/// How two values combine.
			/// </summary>
			[Title( "Blend" )]
			public enum ScriptBlend
			{
				[Title( "Add them" ), Icon( "add" )] Add,
				Subtract
			}

			public class AttributeProbe : Component
			{
				[Property( Title = "Blend mode" ), Sync] public ScriptBlend Mode { get; set; }
				public int Calls;
				[Rpc.Host( NetFlags.OwnerOnly | NetFlags.Unreliable )] public void Use() { Calls++; }
			}
			""" );
		Assert.IsTrue( loaded.Success, string.Join( "\n", loaded.Diagnostics ) );

		var blend = Game.TypeLibrary.GetType( "ScriptBlend" );
		Assert.IsNotNull( blend );
		Assert.AreEqual( "Blend", blend.Title );
		Assert.AreEqual( "How two values combine.", blend.Description );
		var add = blend.TargetType.GetField( "Add" );
		Assert.AreEqual( "Add them", add.GetCustomAttributes( typeof( TitleAttribute ), false ).Cast<TitleAttribute>().Single().Value );
		Assert.AreEqual( "add", add.GetCustomAttributes( typeof( IconAttribute ), false ).Cast<IconAttribute>().Single().Value );

		var probe = Game.TypeLibrary.GetType( "AttributeProbe" );
		Assert.AreEqual( "Blend mode", probe.GetProperty( "Mode" ).Title );
		var use = probe.TargetType.GetMethod( "Use" );
		var host = use.GetCustomAttributes( typeof( Rpc.HostAttribute ), false ).Cast<Rpc.HostAttribute>().Single();
		Assert.AreEqual( NetFlags.OwnerOnly | NetFlags.Unreliable, host.Flags );

		scene = new Scene();
		using var scope = scene.Push();
		var component = new GameObject( scene ).Components.Create( probe );
		use.CreateDelegate<Action>( component )();
		Assert.AreEqual( 1, probe.TargetType.GetField( "Calls" ).GetValue( component ) );
	}
}
