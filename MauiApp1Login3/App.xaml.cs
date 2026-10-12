using Microsoft.Extensions.DependencyInjection;

namespace MauiApp1Login3
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // Substitua 'LoginPage' pela classe da página que deseja iniciar
            return new MyWindow(new MainPage());
        }
    }

    public class MyWindow : Window
    {
        public MyWindow(Page page) : base(page)
        {
            Height = 600.0;
            Width = 400.0;
        }
    }
}