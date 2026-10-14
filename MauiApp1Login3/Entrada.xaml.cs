namespace MauiApp1Login3;

public partial class Entrada : ContentPage
{
    public Entrada()
    {
        InitializeComponent();
    }

    private async void OnTelecomTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(AprTelecom1));
    }

    private async void OnEmBreveTapped(object? sender, TappedEventArgs e)
    {
        await Dialogo.AvisarAsync(
            "Em desenvolvimento",
            "Este módulo ainda não está disponível. Por enquanto, apenas Telecomunicações.");
    }
}
