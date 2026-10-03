using MauiApp1.Presentation.ViewModels;
#if ANDROID
using Android.Views;
#endif
#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
#endif

namespace MauiApp1.Presentation.Views;

public partial class AccessPage : ContentPage
{
    private readonly AccessPageViewModel _viewModel;

    public AccessPage(AccessPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        ApplyResponsiveCardWidth();
        SetLeftTextAlignment(UserNameEntry);
        SetLeftTextAlignment(PrimaryPasswordEntry);
        SetLeftTextAlignment(ConfirmationPasswordEntry);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }

    private void OnTogglePasswordVisibility(object? sender, EventArgs eventArgs)
    {
        PrimaryPasswordEntry.IsPassword = !PrimaryPasswordEntry.IsPassword;
        PasswordVisibilityButton.ImageSource = PrimaryPasswordEntry.IsPassword
            ? "eye_off.svg"
            : "eye.svg";
    }

    private void ApplyResponsiveCardWidth()
    {
        CardContainer.HorizontalOptions = DeviceInfo.Idiom == DeviceIdiom.Phone
            ? LayoutOptions.Fill
            : LayoutOptions.Center;
        LoginCard.HorizontalOptions = LayoutOptions.Fill;
    }

    private static void SetLeftTextAlignment(Entry entry)
    {
        entry.HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Start;
        entry.HorizontalOptions = LayoutOptions.Fill;
        entry.HandlerChanged += (_, _) => ApplyNativeLeftAlignment(entry);
        ApplyNativeLeftAlignment(entry);
    }

    private static void ApplyNativeLeftAlignment(Entry entry)
    {
#if ANDROID
        if (entry.Handler?.PlatformView is Android.Widget.EditText nativeEntry)
        {
            nativeEntry.Gravity = GravityFlags.Start | GravityFlags.CenterVertical;
        }
#elif WINDOWS
        if (entry.Handler?.PlatformView is TextBox nativeEntry)
        {
            nativeEntry.TextAlignment = Microsoft.UI.Xaml.TextAlignment.Left;
            nativeEntry.HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left;
        }
#endif
    }
}
