namespace MauiApp1Login3;

public partial class Login : ContentPage
{
    public Login()
    {
        InitializeComponent();
    }

    private async void OnEsqueciSenhaTapped(object? sender, TappedEventArgs e)
    {
        // Não há backend de autenticação: o acesso é liberado pelo responsável da equipe.
        await Dialogo.AvisarAsync(
            "Recuperar acesso",
            "A redefinição de senha é feita pelo responsável pela equipe. Procure seu supervisor para liberar um novo acesso.");
    }

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
                await Shell.Current.GoToAsync(nameof(Entrada));
            }
            else
            {
                await Dialogo.AvisarAsync(
                    "Acesso negado",
                    "Usuário ou senha inválidos. Confira os dados e tente de novo.",
                    "Fechar");
            }
        }
        catch (Exception ex)
        {
            await Dialogo.AvisarAsync("Algo deu errado", ex.Message, "Fechar");
        }
    }
}
