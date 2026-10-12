namespace MauiApp1Login3;

public partial class Entrada : ContentPage
{
    // Variável para armazenar a seleção do usuário
    private string opcaoSelecionada;

    public Entrada()
    {
        InitializeComponent();
    }

    private void OnAtuacaoChanged(object sender, EventArgs e)
    {
        var picker = (Picker)sender;
        int selectedIndex = picker.SelectedIndex;

        if (selectedIndex != -1)
        {
            // Guarda a opção escolhida na variável
            opcaoSelecionada = (string)picker.ItemsSource[selectedIndex];
            ResultadoLabel.Text = $"Opção selecionada: {opcaoSelecionada}";
        }
    }

    private async void OnAvancarClicked(object sender, EventArgs e)
    {
        // Valida se o usuário escolheu alguma opção
        if (string.IsNullOrEmpty(opcaoSelecionada))
        {
            await DisplayAlert("Atenção", "Por favor, selecione uma opção antes de continuar.", "OK");
            return;
        }

        // Navega para a página correspondente
        switch (opcaoSelecionada)
        {
            case "Telecomunicações":
                // Abre a página de Telecom (se você usou NavigationPage)
                await Navigation.PushAsync(new AprTelecom1());
                break;

           // case "Elétrica Predial":
              //  await Navigation.PushAsync(new AprTelecom1(());
             //   break;

           // case "Análise de Frota":
              //  await Navigation.PushAsync(new AprTelecom1(());
              //  break;
        }
    }
}