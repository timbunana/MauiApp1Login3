namespace MauiApp1Login3
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private async void EntrarClicked(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(Login));
        }

        private async void ComoFuncionaClicked(object? sender, EventArgs e)
        {
            await Dialogo.AvisarAsync(
                "Como funciona",
                "1. Escolha a área de atuação.\n" +
                "2. Informe se o trabalho é no mesmo nível ou em altura.\n" +
                "3. Marque os EPIs e condições conferidos.\n" +
                "4. Capture uma foto como evidência.\n" +
                "5. Gere o PDF e compartilhe com a equipe.");
        }
    }
}
