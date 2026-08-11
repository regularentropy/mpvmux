using Avalonia.Controls;

using Microsoft.Extensions.DependencyInjection;
using mpvmux.ViewModels;
using System;

namespace mpvmux.Services;

internal interface IWindowService
{
    void ShowDialog<TWindow, TViewModel>()
        where TWindow : Window, new()
        where TViewModel : ViewModelBase;

    TWindow GetDialog<TWindow, TViewModel>(Func<TViewModel> viewModelFactory)
        where TWindow : Window, new()
        where TViewModel : ViewModelBase;

    TWindow ShowDialog<TWindow>(Func<TWindow> windowFactory)
        where TWindow : Window;
}

internal class WindowService(Window window, IServiceProvider serviceProvider) : IWindowService
{
    public TWindow ShowDialog<TWindow>(Func<TWindow> windowFactory)
        where TWindow : Window
    {
        TWindow window1 = windowFactory();
        window1.ShowDialog(window);
        return window1;
    }

    public void ShowDialog<TWindow, TViewModel>()
        where TWindow : Window, new()
        where TViewModel : ViewModelBase
    {
        TWindow window1 = new TWindow();
        TViewModel viewModel = serviceProvider.GetService<TViewModel>()!;
        window1.DataContext = viewModel;
        window1.ShowDialog(window);
    }

    public TWindow GetDialog<TWindow, TViewModel>(Func<TViewModel> viewModelFactory)
        where TWindow : Window, new()
        where TViewModel : ViewModelBase
    {
        TWindow window = new TWindow();
        TViewModel viewModel = viewModelFactory();
        window.DataContext = viewModel;
        return window;
    }

}
