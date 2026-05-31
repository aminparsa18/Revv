using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Activity;
using AndroidX.Core.View;

namespace Revv;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        EdgeToEdge.Enable(this);
        // Draw content behind the camera cutout in landscape.
        // ShortEdges only covers top/bottom (portrait notch); in landscape the camera is
        // on the long edge, so we need Always (API 30+) for full coverage.
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            Window!.Attributes!.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.Never;
        }
        else if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            Window!.Attributes!.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.Never;
        }

        // Edge-to-edge: let content fill behind system bars
        WindowCompat.SetDecorFitsSystemWindows(Window!, false);

        // Hide status bar and nav bar (swipe-to-peek, not sticky)
        WindowInsetsControllerCompat? controller = WindowCompat.GetInsetsController(Window!, Window!.DecorView);
        controller.Hide(WindowInsetsCompat.Type.StatusBars());
        controller.Hide(WindowInsetsCompat.Type.DisplayCutout());
        controller.Hide(WindowInsetsCompat.Type.NavigationBars());
        controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
    }
}
