namespace CampoSeguro;

// O Entry desenha a propria borda/sublinhado da plataforma, que aparece por dentro do
// Border arredondado do design system. Aqui ela e removida para sobrar so a nossa.
public static class EstiloNativo
{
    public static void RemoverBordaDosCampos()
    {
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping(
            "SemBordaNativa",
            (handler, view) =>
            {
#if WINDOWS
                var campo = handler.PlatformView;
                var transparente = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Transparent);
                var semBorda = new Microsoft.UI.Xaml.Thickness(0);

                campo.BorderThickness = semBorda;
                campo.Background = transparente;
                campo.Padding = new Microsoft.UI.Xaml.Thickness(0);

                // O WinUI redesenha a borda nos estados de foco/hover: zerar por recurso.
                campo.Resources["TextControlBorderThemeThickness"] = semBorda;
                campo.Resources["TextControlBorderThemeThicknessFocused"] = semBorda;
                campo.Resources["TextControlBackground"] = transparente;
                campo.Resources["TextControlBackgroundPointerOver"] = transparente;
                campo.Resources["TextControlBackgroundFocused"] = transparente;
                campo.Resources["TextControlBackgroundDisabled"] = transparente;
#elif ANDROID
                handler.PlatformView.BackgroundTintList =
                    Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif IOS || MACCATALYST
                handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
            });
    }
}
