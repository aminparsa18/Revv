using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;

namespace Revv;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ScreenOrientation = ScreenOrientation.SensorLandscape, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Draw content behind the camera cutout in landscape
        if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
            Window!.Attributes!.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;

        // Edge-to-edge: let content fill behind system bars
        WindowCompat.SetDecorFitsSystemWindows(Window!, false);

        // Hide status bar and nav bar (swipe-to-peek, not sticky)
        var controller = WindowCompat.GetInsetsController(Window!, Window!.DecorView);
        controller.Hide(WindowInsetsCompat.Type.StatusBars());
        controller.Hide(WindowInsetsCompat.Type.NavigationBars());
        controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
    }
}
