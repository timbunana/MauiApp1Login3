namespace MauiApp1Login3
{
    public partial class MainPage : ContentPage
    {
        int count = 0;

        public MainPage()
        {
            InitializeComponent();
        }

        private void EntrarClicked(object? sender, EventArgs e)
        {
            Application.Current.MainPage = new NavigationPage(new Login());
        }
    }
}
