namespace MauiApp1Login3;

public partial class Login : ContentPage
{
    public Login()
    {
        InitializeComponent();
    }

    [Obsolete]
    private void Button_Clicked(object sender, EventArgs e)
    {

        try
        {

        }
        catch (Exception ex)
        {
            DisplayAlert("Ops", ex.Message, "Fechar");
        }

    }
}