using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AirGlance.App.Views;

public partial class MessageWindow : Window
{
    public MessageWindow(string title, string body)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        BodyText.Text = body;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
