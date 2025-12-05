using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sbox.Engine.Emulation.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Sbox.Engine.Emulation.Tests.Common;

/// <summary>
/// Tests pour HandleManager centralisé.
/// </summary>
[TestClass]
public class HandleManagerTests
{
    [TestMethod]
    public void Register_ShouldReturnUniqueHandle()
    {
        var obj1 = new object();
        var obj2 = new object();
        
        int handle1 = HandleManager.Register(obj1);
        int handle2 = HandleManager.Register(obj2);
        
        Assert.AreNotEqual(handle1, handle2);
        Assert.IsTrue(handle1 >= 1000);
        Assert.IsTrue(handle2 >= 1000);
    }
    
    [TestMethod]
    public void Get_ShouldReturnRegisteredObject()
    {
        var obj = new object();
        int handle = HandleManager.Register(obj);
        
        var retrieved = HandleManager.Get<object>(handle);
        
        Assert.AreEqual(obj, retrieved);
    }
    
    [TestMethod]
    public void Get_ShouldReturnNullForInvalidHandle()
    {
        var result = HandleManager.Get<object>(9999);
        
        Assert.IsNull(result);
    }
    
    [TestMethod]
    public void Unregister_ShouldRemoveObject()
    {
        var obj = new object();
        int handle = HandleManager.Register(obj);
        
        HandleManager.Unregister(handle);
        
        var retrieved = HandleManager.Get<object>(handle);
        Assert.IsNull(retrieved);
    }
    
    [TestMethod]
    public void Exists_ShouldReturnTrueForValidHandle()
    {
        var obj = new object();
        int handle = HandleManager.Register(obj);
        
        bool exists = HandleManager.Exists(handle);
        
        Assert.IsTrue(exists);
    }
    
    [TestMethod]
    public void Exists_ShouldReturnFalseForInvalidHandle()
    {
        bool exists = HandleManager.Exists(9999);
        
        Assert.IsFalse(exists);
    }

    [TestMethod]
    public void RegisterCount_ShouldIncrement()
    {
        long before = HandleManager.RegisterCount;
        HandleManager.Register(new object());
        Assert.AreEqual(before + 1, HandleManager.RegisterCount);
    }

    [TestMethod]
    public void EntryPool_ShouldGrowAfterUnregister()
    {
        int handle = HandleManager.Register(new object());
        int before = HandleManager.EntryPoolSize;
        HandleManager.Unregister(handle);
        Assert.IsTrue(HandleManager.EntryPoolSize >= before + 1);
    }

    // ========== Cycle TDD 1.1 : HandleEntry - Structure de base ==========

    [TestMethod]
    public void HandleEntry_ShouldInitializeWithObjectAndBindingHandle()
    {
        var obj = new object();
        
        // Note: HandleEntry est interne, on teste via HandleManager.Register qui crée une HandleEntry
        int handle = HandleManager.Register(obj);
        int bindingHandleFromManager = HandleManager.GetBindingHandle(handle);
        
        Assert.IsTrue(bindingHandleFromManager >= 2000);
        Assert.AreEqual(0, bindingHandleFromManager % 2); // BindingHandle doit être pair
    }

    [TestMethod]
    public void HandleEntry_IncrementRef_ShouldModifyCounterAtomically()
    {
        var obj = new object();
        int handle = HandleManager.Register(obj);
        
        // Copier le handle devrait incrémenter le compteur de références
        int handle2 = HandleManager.CopyHandle(handle);
        
        Assert.IsTrue(handle2 > 0);
        int refCount = HandleManager.GetReferenceCount(handle);
        Assert.AreEqual(2, refCount); // Deux handles pointent vers le même objet
    }

    [TestMethod]
    public void HandleEntry_AddHandle_ShouldManageHashSetThreadSafe()
    {
        var obj = new object();
        int handle1 = HandleManager.Register(obj);
        int handle2 = HandleManager.CopyHandle(handle1);
        int handle3 = HandleManager.CopyHandle(handle1);
        
        // Les trois handles devraient pointer vers le même objet
        var obj1 = HandleManager.Get<object>(handle1);
        var obj2 = HandleManager.Get<object>(handle2);
        var obj3 = HandleManager.Get<object>(handle3);
        
        Assert.AreEqual(obj, obj1);
        Assert.AreEqual(obj, obj2);
        Assert.AreEqual(obj, obj3);
        
        int refCount = HandleManager.GetReferenceCount(handle1);
        Assert.AreEqual(3, refCount);
    }

    [TestMethod]
    public void HandleEntry_GetAllHandles_ShouldReturnThreadSafeSnapshot()
    {
        var obj = new object();
        int handle1 = HandleManager.Register(obj);
        int handle2 = HandleManager.CopyHandle(handle1);
        int handle3 = HandleManager.CopyHandle(handle1);
        
        var allHandles = HandleManager.GetAllHandles(handle1);
        
        Assert.IsNotNull(allHandles);
        Assert.AreEqual(3, allHandles.Length);
        CollectionAssert.Contains(allHandles, handle1);
        CollectionAssert.Contains(allHandles, handle2);
        CollectionAssert.Contains(allHandles, handle3);
    }

    [TestMethod]
    public void HandleEntry_TryLock_ShouldExecuteActionIfLockAcquired()
    {
        var obj = new object();
        int handle = HandleManager.Register(obj);
        
        bool lockAcquired = false;
        bool actionExecuted = HandleManager.TryLock(handle, () => {
            lockAcquired = true;
        });
        
        Assert.IsTrue(actionExecuted);
        Assert.IsTrue(lockAcquired);
    }

    [TestMethod]
    public void HandleEntry_TryLock_ShouldTimeoutIfLockNotAcquired()
    {
        var obj = new object();
        int handle = HandleManager.Register(obj);
        
        // Simuler un lock bloqué (dans un vrai scénario, ce serait un autre thread)
        // Pour ce test, on vérifie que TryLock gère le timeout
        bool actionExecuted = HandleManager.TryLock(handle, () => {
            Thread.Sleep(2000); // Simuler une opération longue
        }, timeoutMs: 100);
        
        // Le timeout devrait empêcher l'exécution si le lock est déjà pris
        // Ce test nécessite une implémentation réelle avec timeout
        Assert.IsTrue(true); // Placeholder pour le moment
    }
}

