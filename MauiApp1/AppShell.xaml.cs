using MauiApp1.Presentation.Views;

namespace MauiApp1
{
    public partial class AppShell : Shell
    {
        public AppShell(MainPage mainPage)
        {
            InitializeComponent();
            Items.Add(new ShellContent
            {
                Title = "Inicio",
                Route = "MainPage",
                Content = mainPage
            });
        }
    }
}
