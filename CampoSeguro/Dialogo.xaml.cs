namespace CampoSeguro;

// Substitui DisplayAlert / DisplayActionSheet, que sao caixas do sistema e ignoram
// o design system. Fica como ultima camada da pagina e aparece sob demanda.
public partial class Dialogo : ContentView
{
    private TaskCompletionSource<string?>? resposta;
    private string? opcaoCancelar;

    public Dialogo()
    {
        InitializeComponent();
    }

    // Mostra uma mensagem com um unico botao de confirmacao.
    public Task AvisarAsync(string titulo, string mensagem, string botao = "Entendi")
        => PerguntarAsync(titulo, mensagem, new[] { botao });

    // Mostra as opcoes e devolve a escolhida, ou null se o usuario dispensar.
    public Task<string?> PerguntarAsync(
        string titulo,
        string mensagem,
        string[] opcoes,
        string? cancelar = null)
    {
        TituloLabel.Text = titulo.ToUpperInvariant();

        MensagemLabel.Text = mensagem;
        MensagemLabel.IsVisible = !string.IsNullOrWhiteSpace(mensagem);

        opcaoCancelar = cancelar;
        Opcoes.Clear();

        for (int i = 0; i < opcoes.Length; i++)
        {
            string texto = opcoes[i];
            var botao = new Button
            {
                Text = texto,
                // A primeira opcao e a acao sugerida; as demais ficam discretas.
                Style = i == 0
                    ? ObterEstilo("PrimaryButton")
                    : ObterEstilo("SecondaryButton")
            };
            botao.Clicked += (_, _) => Responder(texto);
            Opcoes.Add(botao);
        }

        if (!string.IsNullOrWhiteSpace(cancelar))
        {
            var botaoCancelar = new Button
            {
                Text = cancelar,
                Style = ObterEstilo("DangerButton")
            };
            botaoCancelar.Clicked += (_, _) => Responder(cancelar);
            Opcoes.Add(botaoCancelar);
        }

        IsVisible = true;
        resposta = new TaskCompletionSource<string?>();
        return resposta.Task;
    }

    private static Style ObterEstilo(string chave)
        => (Style)Application.Current!.Resources[chave];

    private void Responder(string? escolha)
    {
        IsVisible = false;
        resposta?.TrySetResult(escolha);
        resposta = null;
    }

    // Tocar fora do card equivale a cancelar.
    private void OnVeuTocado(object? sender, TappedEventArgs e) => Responder(opcaoCancelar);
}
