using MauiApp1.Presentation.ViewModels;
using MauiApp1.Application.Sync;

namespace MauiApp1.Presentation.Views;

public partial class ReportsPage : CoordinatorRefreshPage
{
    private readonly ReportsViewModel _viewModel;
#if WINDOWS
    private bool _isDatePickerOpen;
#endif

    public ReportsPage(ReportsViewModel viewModel, ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
#if WINDOWS
        MoveWindowsFiltersOutsideResults(SalesResultsCollection, nameof(ReportsViewModel.IsSalesSection));
        MoveWindowsFiltersOutsideResults(InventoryResultsCollection, nameof(ReportsViewModel.IsInventorySection));
        MoveWindowsFiltersOutsideResults(MovementsResultsCollection, nameof(ReportsViewModel.IsMovementsSection));
#endif
    }

#if WINDOWS
    private void MoveWindowsFiltersOutsideResults(CollectionView results, string sectionVisibilityProperty)
    {
        if (results.Header is not View filters || results.Parent is not Grid host)
            throw new InvalidOperationException("No se encontró el contenedor de filtros de Reportes.");

        // Keep interactive filters outside the Windows CollectionView header/empty-view layer.
        var index = host.Children.IndexOf(results);
        results.Header = null;
        host.Remove(results);
        results.RemoveBinding(IsVisibleProperty);
        results.IsVisible = true;

        var filterScroll = new ScrollView
        {
            Content = filters,
            Orientation = ScrollOrientation.Vertical,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            MaximumHeightRequest = 480
        };
        var section = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };
        section.SetBinding(IsVisibleProperty, sectionVisibilityProperty);
        section.Add(filterScroll);
        section.Add(results, 0, 1);
        section.SizeChanged += (_, _) =>
        {
            if (section.Height > 0)
                filterScroll.MaximumHeightRequest = section.Height - Math.Min(160, section.Height * 0.35);
        };
        host.Children.Insert(index, section);
    }
#endif

    protected override Task RefreshAfterCoordinatorChangesAsync() => _viewModel.LoadAsync();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ReportsViewModel viewModel)
            await viewModel.LoadAsync();
    }

    private async void OnSalesClicked(object? sender, EventArgs eventArgs)
    {
        if (BindingContext is ReportsViewModel viewModel) await viewModel.ShowSalesAsync();
    }

    private async void OnInventoryClicked(object? sender, EventArgs eventArgs)
    {
        if (BindingContext is ReportsViewModel viewModel) await viewModel.ShowInventoryAsync();
    }

    private async void OnMovementsClicked(object? sender, EventArgs eventArgs)
    {
        if (BindingContext is ReportsViewModel viewModel) await viewModel.ShowMovementsAsync();
    }

    private async void OnRefreshClicked(object? sender, EventArgs eventArgs)
    {
        if (BindingContext is ReportsViewModel viewModel) await viewModel.RefreshAsync();
    }

    private async void OnSalesFromDateClicked(object? sender, EventArgs eventArgs) =>
        await OpenDatePickerAsync(_viewModel.DateFrom, date => _viewModel.DateFrom = date);

    private async void OnSalesToDateClicked(object? sender, EventArgs eventArgs) =>
        await OpenDatePickerAsync(_viewModel.DateTo, date => _viewModel.DateTo = date);

    private async void OnMovementsFromDateClicked(object? sender, EventArgs eventArgs) =>
        await OpenDatePickerAsync(_viewModel.DateFrom, date => _viewModel.DateFrom = date);

    private async void OnMovementsToDateClicked(object? sender, EventArgs eventArgs) =>
        await OpenDatePickerAsync(_viewModel.DateTo, date => _viewModel.DateTo = date);

    private async Task OpenDatePickerAsync(DateTime currentDate, Action<DateTime> applyDate)
    {
#if WINDOWS
        if (_isDatePickerOpen)
            return;

        _isDatePickerOpen = true;
        try
        {
            // Host the dialog in the page window, independently of the header's child handlers.
            var nativeWindow = Window?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            var xamlRoot = nativeWindow?.Content?.XamlRoot
                ?? throw new InvalidOperationException("La ventana de Reportes no tiene un XamlRoot activo.");

            var calendar = new Microsoft.UI.Xaml.Controls.CalendarView
            {
                SelectionMode = Microsoft.UI.Xaml.Controls.CalendarViewSelectionMode.Single,
                IsTodayHighlighted = true,
                Width = 340,
                Height = 350
            };
            calendar.SelectedDates.Add(new DateTimeOffset(currentDate.Date));

            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = "Seleccionar fecha",
                Content = calendar,
                PrimaryButtonText = "Aceptar",
                CloseButtonText = "Cancelar",
                DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary
            };
            calendar.SelectedDatesChanged += (_, _) =>
                dialog.IsPrimaryButtonEnabled = calendar.SelectedDates.Count > 0;

            var result = await dialog.ShowAsync();
            if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary && calendar.SelectedDates.Count > 0)
                applyDate(calendar.SelectedDates[0].Date);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"[ReportsDatePicker] {exception}");
            await DisplayAlertAsync("No se pudo abrir el calendario", "Vuelve a intentarlo en esta ventana.", "Aceptar");
        }
        finally
        {
            _isDatePickerOpen = false;
        }
#elif ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
            return;

        var datePicker = new Android.App.DatePickerDialog(
            activity,
            (_, args) => applyDate(new DateTime(args.Year, args.Month + 1, args.DayOfMonth)),
            currentDate.Year,
            currentDate.Month - 1,
            currentDate.Day);
        datePicker.Show();
        await Task.CompletedTask;
#endif
    }

    private async void OnBackClicked(object? sender, EventArgs eventArgs)
    {
        if (Navigation.ModalStack.Count > 0) await Navigation.PopModalAsync();
        else if (Navigation.NavigationStack.Count > 1) await Navigation.PopAsync();
    }

    private void OnPageSizeChanged(object? sender, EventArgs eventArgs)
    {
        if (PageLayout.Width <= 0) return;
        var isDesktop = PageLayout.Width >= 900;
        PageLayout.Padding = isDesktop ? new Thickness(32, 28) : new Thickness(16, 20);
        ContentHost.WidthRequest = Math.Min(1260, Math.Max(0, PageLayout.Width - PageLayout.Padding.HorizontalThickness));
        ContentHost.RowSpacing = isDesktop ? 16 : 12;
        PageTitle.FontSize = isDesktop ? 30 : 26;

        ArrangeFilters(SalesFiltersGrid,
            [SalesFromField, SalesToField, SalesModeField, SalesCustomerField], isDesktop, true);
        ArrangeFilters(InventoryFiltersGrid,
            [InventoryCategoryField, InventoryStatusField], isDesktop, false);
        ArrangeFilters(MovementsFiltersGrid,
            [MovementsFromField, MovementsToField, MovementsProductField, MovementsTypeField], isDesktop, true);
    }

    private static void ArrangeFilters(Grid grid, View[] fields, bool isDesktop, bool hasDateRange)
    {
        var columns = isDesktop ? fields.Length : hasDateRange ? 2 : 1;
        grid.ColumnDefinitions.Clear();
        for (var index = 0; index < columns; index++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            if (isDesktop || (hasDateRange && index < 2))
            {
                Grid.SetRow(field, 0);
                Grid.SetColumn(field, index);
                Grid.SetColumnSpan(field, 1);
            }
            else
            {
                Grid.SetRow(field, hasDateRange ? index - 1 : index);
                Grid.SetColumn(field, 0);
                Grid.SetColumnSpan(field, columns);
            }
        }
    }
}
