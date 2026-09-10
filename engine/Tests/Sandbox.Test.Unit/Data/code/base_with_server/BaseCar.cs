public partial class BaseCar
{
    public virtual void HelloClientAndServer()
    {
#if SERVER
			// hello I'm the server
#endif

        // hello I'm both the server and client
    }
}

#if SERVER
public class ServerOnlyClass 
{
    public void ServerOnlyMethod() 
    {
    }
}
#else
public class ClientOnlyMethod
{
    public void HelloClientOnly()
    {
    }
}
#endif