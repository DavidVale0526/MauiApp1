using Microsoft.Extensions.Logging;
using MauiApp1.Presentation.ViewModels;
using MauiApp1.Presentation.Views;
using MauiApp1.Application.Storage;
using MauiApp1.Application.Authentication;
using MauiApp1.Infrastructure.Persistence;
using Microsoft.Maui.Networking;
using ZXing.Net.Maui.Controls;

namespace MauiApp1
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseBarcodeReader()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            builder.Services.AddSingleton<AppShell>();
            builder.Services.AddSingleton<IConnectivity>(Connectivity.Current);
            builder.Services.AddSingleton<MauiApp1.Application.Sync.ICoordinatorRealtimeClient, MauiApp1.Infrastructure.Lan.CoordinatorRealtimeClient>();
            builder.Services.AddSingleton<MauiApp1.Infrastructure.Sync.LocalCoordinatorDatabaseChangeMonitor>();
            builder.Services.AddSingleton<MauiApp1.Application.Sync.ILocalCoordinatorChangeMonitor>(services =>
                services.GetRequiredService<MauiApp1.Infrastructure.Sync.LocalCoordinatorDatabaseChangeMonitor>());
            builder.Services.AddSingleton<MauiApp1.Application.Lan.ICoordinatorLinkStorage, MauiApp1.Services.SecureCoordinatorLinkStorage>();
            builder.Services.AddSingleton<MauiApp1.Application.Authentication.ICoordinatorUserSessionStorage, MauiApp1.Services.SecureCoordinatorUserSessionStorage>();
            builder.Services.AddSingleton<MauiApp1.Infrastructure.Lan.CoordinatorPairingClient>();
            builder.Services.AddSingleton<MauiApp1.Application.Lan.ICoordinatorPairingClient>(services =>
                services.GetRequiredService<MauiApp1.Infrastructure.Lan.CoordinatorPairingClient>());
            builder.Services.AddSingleton<MauiApp1.Application.Authentication.ICoordinatorAuthenticationClient>(services =>
                services.GetRequiredService<MauiApp1.Infrastructure.Lan.CoordinatorPairingClient>());
            builder.Services.AddSingleton<MauiApp1.Application.Authentication.ICoordinatorCashierAccountClient>(services =>
                services.GetRequiredService<MauiApp1.Infrastructure.Lan.CoordinatorPairingClient>());
            builder.Services.AddSingleton<MauiApp1.Application.Sync.ICoordinatorChangeClient>(services =>
                services.GetRequiredService<MauiApp1.Infrastructure.Lan.CoordinatorPairingClient>());
            builder.Services.AddSingleton<MauiApp1.Application.Authentication.ICoordinatedSignInService, MauiApp1.Infrastructure.Authentication.CoordinatedSignInService>();
            builder.Services.AddSingleton<MauiApp1.Application.Sync.ICoordinatorOperationSender, MauiApp1.Infrastructure.Lan.CoordinatorOperationSender>();
            builder.Services.AddTransient<DevicePairingViewModel>();
            builder.Services.AddTransient<DevicePairingPage>();
            builder.Services.AddTransient<MauiApp1.Presentation.Views.ConflictReviewPage>();
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
            builder.Services.AddTransient<CustomerManagementPage>();
            builder.Services.AddTransient<CustomerManagementViewModel>();
            builder.Services.AddTransient<PaymentManagementPage>();
            builder.Services.AddTransient<PaymentManagementViewModel>();
            builder.Services.AddTransient<ReportsPage>();
            builder.Services.AddTransient<ReportsViewModel>();
            builder.Services.AddTransient<OtherExitPage>();
            builder.Services.AddTransient<OtherExitViewModel>();
            var databasePath = Path.Combine(FileSystem.AppDataDirectory,
                LocalPersistenceServiceCollectionExtensions.DatabaseFileName);
            var migrationsManagedExternally = false;
#if WINDOWS
            var coordinatorDatabasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Miscelanea Ari", "Coordinator", "miscelanea-ari-central.db");
            if (File.Exists(coordinatorDatabasePath))
            {
                databasePath = coordinatorDatabasePath;
                migrationsManagedExternally = true;
            }
#endif
            builder.Services.AddLocalPersistence(databasePath, migrationsManagedExternally);

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            var app = builder.Build();
            app.Services.GetRequiredService<ILocalDatabaseInitializer>()
                .InitializeAsync()
                .GetAwaiter()
                .GetResult();
#if WINDOWS
            app.Services.GetRequiredService<MauiApp1.Application.Sync.ILocalCoordinatorChangeMonitor>().Start();
#endif

            return app;
        }
    }
}
