using Microsoft.Extensions.Logging;
using MauiApp1.Presentation.ViewModels;
using MauiApp1.Presentation.Views;
using MauiApp1.Application.Storage;
using MauiApp1.Application.Authentication;
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
            builder.Services.AddTransient<AccessPage>();
            builder.Services.AddTransient<AccessPageViewModel>();
            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<MainPageViewModel>();
            builder.Services.AddTransient<CashierSetupPage>();
            builder.Services.AddTransient<CashierSetupViewModel>();
            builder.Services.AddTransient<CategoryManagementPage>();
            builder.Services.AddTransient<CategoryManagementViewModel>();
            builder.Services.AddTransient<ProductManagementPage>();
            builder.Services.AddTransient<ProductManagementViewModel>();
            builder.Services.AddTransient<StockConsultationPage>();
            builder.Services.AddTransient<StockConsultationViewModel>();
            builder.Services.AddTransient<PurchaseRegistrationPage>();
            builder.Services.AddTransient<PurchaseRegistrationViewModel>();
            builder.Services.AddTransient<PurchaseHistoryPage>();
            builder.Services.AddTransient<PurchaseHistoryViewModel>();
            builder.Services.AddTransient<InventoryMovementHistoryPage>();
            builder.Services.AddTransient<InventoryMovementHistoryViewModel>();
            builder.Services.AddTransient<SaleRegistrationPage>();
            builder.Services.AddTransient<SaleRegistrationViewModel>();
            builder.Services.AddTransient<SaleHistoryPage>();
            builder.Services.AddTransient<SaleHistoryViewModel>();
            builder.Services.AddTransient<OtherExitPage>();
            builder.Services.AddTransient<OtherExitViewModel>();
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
