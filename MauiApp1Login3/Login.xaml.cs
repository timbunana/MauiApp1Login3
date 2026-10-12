namespace MauiApp1Login3;

public partial class Login : ContentPage
{
    public Login()
    {
        InitializeComponent();
    }

    [Obsolete]
    private async void Button_Clicked(object sender, EventArgs e)
    {

        try
        {
            List<DadosUsuario> lista_usuarios = new List<DadosUsuario>()
            {
                new DadosUsuario() { Usuario = "admin", Senha = "123" },
                new DadosUsuario() { Usuario = "user", Senha = "321" },
                new DadosUsuario() { Usuario = "jonas", Senha = "1234" }

            };

            DadosUsuario dados_digitados = new DadosUsuario()
            {
                Usuario = txtUsuario.Text,
                Senha = txtSenha.Text
            };

            //LINQ
            if (lista_usuarios.Any(

                i => i.Usuario == dados_digitados.Usuario && i.Senha == dados_digitados.Senha

                ))
            {
                await SecureStorage.SetAsync("usuario_logado", dados_digitados.Usuario);
                Application.Current.MainPage = new NavigationPage(new Entrada());

            }
            else
            {
                await DisplayAlert("Ops", "Usuário ou senha inválidos", "Fechar");
            }
        }
        catch (Exception ex)
        {
            DisplayAlert("Ops", ex.Message, "Fechar");
        }

    }
}