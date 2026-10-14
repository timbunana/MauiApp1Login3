using Microsoft.Extensions.DependencyInjection;

namespace MauiApp1Login3
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            // O design system é só escuro (ver DESIGN.md): fixar evita que o tema claro
            // do sistema vaze nos controles nativos.
            UserAppTheme = AppTheme.Dark;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new MyWindow(new AppShell());
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