namespace HotloadTests;

/// <summary>
/// Reflection objects referring to swapped types should be replaced with ones from the new assembly.
/// </summary>
[TestClass]
public class ReflectionTest : HotloadFixtureTestBase
{
	[TestMethod]
	public async Task ReflectionMembers()
	{
		await HotloadFixtureAsync(
			"""
			using System;
			using System.Reflection;

			public class ReflectionMembers : IHotloadTestFixture
			{
				public Type Type;
				public FieldInfo FieldInfo;
				public PropertyInfo PropertyInfo;
				public MethodInfo MethodInfo;

				public int Field;
				public int Property { get; set; }
				public int Method( int a ) => a * 2;

				void IHotloadTestFixture.Test()
				{
			-		Type = typeof( ReflectionMembers );
			-		FieldInfo = Type.GetField( nameof( Field ), BindingFlags.Instance | BindingFlags.Public );
			-		PropertyInfo = Type.GetProperty( nameof( Property ), BindingFlags.Instance | BindingFlags.Public );
			-		MethodInfo = Type.GetMethod( nameof( Method ), BindingFlags.Instance | BindingFlags.Public );
			-
					Assert.AreEqual( typeof( ReflectionMembers ), Type );
					Assert.AreEqual( typeof( ReflectionMembers ).GetField( nameof( Field ) ), FieldInfo );
					Assert.AreEqual( typeof( ReflectionMembers ).GetProperty( nameof( Property ) ), PropertyInfo );
					Assert.AreEqual( typeof( ReflectionMembers ).GetMethod( nameof( Method ) ), MethodInfo );
				}
			}
			""" );
	}

	/// <summary>
	/// Make sure that we don't lose type parameters from generic method MethodInfos.
	/// </summary>
	[TestMethod]
	public async Task ReflectionGenericMethodInfo()
	{
		await HotloadFixtureAsync(
			"""
			using System.Reflection;

			public class ReflectionGenericMethodInfo : IHotloadTestFixture
			{
				public static MethodInfo MethodInfo;

				public string GenericMethod<T>() => typeof( T ).Name;

				void IHotloadTestFixture.Test()
				{
			-		MethodInfo = typeof( ReflectionGenericMethodInfo )
			-			.GetMethod( nameof( GenericMethod ) )
			-			.MakeGenericMethod( typeof( string ) );
			-
					Assert.IsNotNull( MethodInfo );
					Assert.AreEqual( typeof( ReflectionGenericMethodInfo ), MethodInfo.DeclaringType );
					Assert.IsTrue( MethodInfo.IsConstructedGenericMethod );
					Assert.AreEqual( typeof( string ), MethodInfo.GetGenericArguments()[0] );
				}
			}
			""" );
	}

	/// <summary>
	/// Make sure that we don't lose type parameters from generic declaring type MethodInfos.
	/// </summary>
	[TestMethod]
	public async Task ReflectionGenericTypeMethodInfo()
	{
		await HotloadFixtureAsync(
			"""
			using System.Reflection;

			public class GenericTypeClass<T>
			{
				public string GenericMethod() => typeof( T ).Name;
			}

			public class ReflectionGenericTypeMethodInfo : IHotloadTestFixture
			{
				public static MethodInfo MethodInfo;

				void IHotloadTestFixture.Test()
				{
			-		MethodInfo = typeof( GenericTypeClass<string> ).GetMethod( nameof( GenericTypeClass<string>.GenericMethod ) );
			-
					Assert.IsNotNull( MethodInfo );
					Assert.AreEqual( typeof( GenericTypeClass<string> ), MethodInfo.DeclaringType );
				}
			}
			""" );
	}

	/// <summary>
	/// Special case for <see cref="System.Reflection.ParameterInfo"/>.
	/// </summary>
	[TestMethod]
	public async Task ReflectionParameterInfo()
	{
		await HotloadFixtureAsync(
			"""
			using System.Reflection;

			public class ReflectionParameterInfo : IHotloadTestFixture
			{
				public static ParameterInfo ParameterInfo;

				public void Method1( int parameter ) { }

				void IHotloadTestFixture.Test()
				{
			-		ParameterInfo = typeof( ReflectionParameterInfo ).GetMethod( nameof( Method1 ) ).GetParameters()[0];
			-
					Assert.AreEqual( typeof( ReflectionParameterInfo ).GetMethod( nameof( Method1 ) ).GetParameters()[0], ParameterInfo );
					Assert.AreEqual( typeof( int ), ParameterInfo.ParameterType );
				}
			}
			""" );
	}

	/// <summary>
	/// Test upgrading <see cref="System.Reflection.ParameterInfo"/> from a generic instance method.
	/// </summary>
	[TestMethod]
	public async Task ReflectionGenericMethodParameterInfo()
	{
		await HotloadFixtureAsync(
			"""
			using System.Reflection;

			public class ReflectionGenericMethodParameterInfo : IHotloadTestFixture
			{
				public static ParameterInfo ParameterInfo;

				public void Method2<T>( T parameter ) { }

				void IHotloadTestFixture.Test()
				{
			-		ParameterInfo = typeof( ReflectionGenericMethodParameterInfo ).GetMethod( nameof( Method2 ) )
			-			.MakeGenericMethod( typeof( string ) ).GetParameters()[0];
			-
					Assert.IsNotNull( ParameterInfo );
					Assert.AreEqual( typeof( ReflectionGenericMethodParameterInfo ), ParameterInfo.Member.DeclaringType );
					Assert.AreEqual( typeof( string ), ParameterInfo.ParameterType );
				}
			}
			""" );
	}

	/// <summary>
	/// Reproducing <see href="https://github.com/Facepunch/sbox/issues/1673"/>.
	/// </summary>
	[TestMethod]
	public async Task CompareGenericMethods()
	{
		await HotloadFixtureAsync(
			"""
			using System.Linq;
			using System.Reflection;

			public class CompareGenericMethods : IHotloadTestFixture
			{
				public MethodInfo MethodInfo;

				public void GenericMethod( ref int parameter ) { }

				public void GenericMethod<T>( ref T parameter ) { }

				void IHotloadTestFixture.Test()
				{
			-		MethodInfo = typeof( CompareGenericMethods )
			-			.GetMethods()
			-			.FirstOrDefault( x => x.Name == nameof( GenericMethod ) && x.IsGenericMethodDefinition );
			-
					Assert.IsNotNull( MethodInfo );
					Assert.AreEqual( nameof( GenericMethod ), MethodInfo.Name );
					Assert.AreEqual( typeof( CompareGenericMethods ), MethodInfo.DeclaringType );
			+		Assert.IsTrue( MethodInfo.IsGenericMethodDefinition );
				}
			}
			""" );
	}
}
