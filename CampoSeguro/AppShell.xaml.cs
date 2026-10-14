namespace CampoSeguro
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute(nameof(Login), typeof(Login));
            Routing.RegisterRoute(nameof(Entrada), typeof(Entrada));
            Routing.RegisterRoute(nameof(AprTelecom1), typeof(AprTelecom1));
        }
    }
}
