using Microsoft.Extensions.Logging;
using MauiApp1.Presentation.ViewModels;
using MauiApp1.Presentation.Views;
using MauiApp1.Application.Storage;
using MauiApp1.Infrastructure.Persistence;

namespace MauiApp1
{
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

            builder.Services.AddSingleton<AppShell>();
            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<MainPageViewModel>();
            builder.Services.AddLocalPersistence(Path.Combine(FileSystem.AppDataDirectory,
                LocalPersistenceServiceCollectionExtensions.DatabaseFileName));

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            var app = builder.Build();
            app.Services.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync()
                .GetAwaiter()
                .GetResult();

            return app;
        }
    }
}
