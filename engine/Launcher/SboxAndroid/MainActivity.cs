using Android.App;
using Android.Content.PM;
using Android.Opengl;
using Android.OS;
using Android.Util;
using Javax.Microedition.Khronos.Egl;
using Javax.Microedition.Khronos.Opengles;
using Java.Lang;
using EGLConfig = Javax.Microedition.Khronos.Egl.EGLConfig;

namespace Sandbox.Launcher.SboxAndroid;

[Activity(
    Label = "SboxAndroid",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public class MainActivity : Activity, GLSurfaceView.IRenderer
{
    private GLSurfaceView? _glView;
    private bool _libLoaded;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        TryLoadLib();

        _glView = new GLSurfaceView(this);
        _glView.SetEGLContextClientVersion(3);
        _glView.SetRenderer(this);
        SetContentView(_glView);
    }

    protected override void OnPause()
    {
        base.OnPause();
        _glView?.OnPause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        _glView?.OnResume();
    }

    public void OnSurfaceCreated(IGL10? gl, EGLConfig? config)
    {
        GLES30.GlClearColor(0.1f, 0.1f, 0.2f, 1.0f);
        // TODO: appeler l'init native du moteur lorsque l'API sera exposée côté libengine2.so
    }

    public void OnSurfaceChanged(IGL10? gl, int width, int height)
    {
        GLES30.GlViewport(0, 0, width, height);
    }

    public void OnDrawFrame(IGL10? gl)
    {
        GLES30.GlClear((int)GLES30.GlColorBufferBit | (int)GLES30.GlDepthBufferBit);
        // TODO: forward rendu au moteur une fois l'API dispo
    }

    private void TryLoadLib()
    {
        if (_libLoaded) return;

        try
        {
            JavaSystem.LoadLibrary("engine2");
            _libLoaded = true;
            Log.Info("SboxAndroid", "libengine2.so chargée (LoadLibrary engine2)");
        }
        catch (System.Exception ex)
        {
            Log.Warn("SboxAndroid", $"Echec LoadLibrary(engine2): {ex}");
        }
    }
}

