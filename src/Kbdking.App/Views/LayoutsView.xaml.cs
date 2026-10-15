using Kbdking.Presentation;
using Microsoft.UI.Xaml.Controls;

namespace Kbdking.App.Views;

public sealed partial class LayoutsView : UserControl
{
    public LayoutsView(LayoutManagerViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public LayoutManagerViewModel ViewModel { get; }
}
