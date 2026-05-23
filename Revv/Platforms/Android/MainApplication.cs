using Android.App;
using Android.Runtime;

namespace Revv;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Android.Util.Log.Error("REVV_CRASH", args.ExceptionObject?.ToString());

        AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
        {
            Android.Util.Log.Error("REVV_CRASH", args.Exception?.ToString());
            args.Handled = false;
        };
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
