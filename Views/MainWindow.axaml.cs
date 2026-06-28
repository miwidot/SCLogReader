using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SCLogReader.Services;

namespace SCLogReader.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        UiServices.TopLevel = this;   // für Datei-Dialoge im ViewModel
    }

    void OnGridDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm && vm.LookupCommand.CanExecute(null))
            vm.LookupCommand.Execute(null);
    }
}
