using Microsoft.Extensions.Logging;
using Revv.Shared;
using Revv.Shared.Networking;

namespace Revv;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<SteeringController>();
        builder.Services.AddSingleton<RevvBroadcaster>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
