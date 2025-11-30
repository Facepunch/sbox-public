using Silk.NET.Windowing;
using System;

namespace Sandbox.Engine.Graphics
{
    public class SilkNetApp
    {
        private readonly IWindow _window;
        private readonly ISilkRenderContext _renderContext;

        public SilkNetApp(IWindow window, ISilkRenderContext renderContext)
        {
            _window = window;
            _renderContext = renderContext;

            _window.Load += OnLoad;
            _window.Update += OnUpdate;
            _window.Render += OnRender;
            _window.Closing += OnClosing;
        }

        public void RunLoop()
        {
            _window.Run();
        }

        private void OnLoad()
        {
            // Initialization logic for the app
            Console.WriteLine("SilkNetApp: OnLoad");
        }

        private void OnUpdate(double deltaTime)
        {
            // Update logic for the app
            // Console.WriteLine($"SilkNetApp: OnUpdate(deltaTime: {deltaTime})");
        }

        private void OnRender(double deltaTime)
        {
            // Rendering logic
            _renderContext.Clear(new System.Numerics.Vector4(0.2f, 0.2f, 0.2f, 1.0f), 1.0f, 0);
            
            // TODO: Add actual rendering calls
            
            _renderContext.Submit();

            // Console.WriteLine($"SilkNetApp: OnRender(deltaTime: {deltaTime})");
        }

        private void OnClosing()
        {
            // Cleanup logic
            Console.WriteLine("SilkNetApp: OnClosing");
            _renderContext.Dispose();
        }
    }
}