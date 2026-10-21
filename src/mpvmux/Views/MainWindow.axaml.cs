using Avalonia.Controls;
using Avalonia.Input;
using mpvmux.ViewModels;
using System;

namespace mpvmux.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel? _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_viewModel is not null)
                _viewModel.ShortcutsChanged -= OnShortcutsChanged;

            _viewModel = DataContext as MainWindowViewModel;

            if (_viewModel is not null)
            {
                _viewModel.ShortcutsChanged += OnShortcutsChanged;
                RebuildKeyBindings();
            }
        }

        private void OnShortcutsChanged(object? sender, EventArgs e) => RebuildKeyBindings();

        private void RebuildKeyBindings()
        {
            if (_viewModel is null)
                return;

            // gestures are user-configurable, so the bindings cannot live in XAML
            KeyBindings.Clear();
            foreach (var (gesture, command) in _viewModel.CreateShortcutBindings())
                KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command });
        }
    }
}
