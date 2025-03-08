using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CronPlusUI.Views;

public partial class TaskEditorView : UserControl
{
    public TaskEditorView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
