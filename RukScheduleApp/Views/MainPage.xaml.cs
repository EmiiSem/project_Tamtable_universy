using RukScheduleApp.ViewModels;

namespace RukScheduleApp.Views;

public partial class SchedulePage : ContentPage
{
    public SchedulePage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is MainViewModel vm)
            await vm.InitializeCommand.ExecuteAsync(null);
    }
}
