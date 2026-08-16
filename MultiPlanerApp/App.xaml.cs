using Microsoft.Maui.Controls;

namespace MultiPlanerApp;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        MainPage = new MainPage();
    }
}