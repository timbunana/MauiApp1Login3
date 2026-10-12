using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

// Define um apelido para evitar o conflito de classes 'Colors' entre MAUI e QuestPDF
using QColors = QuestPDF.Helpers.Colors;

namespace MauiApp1Login3;

public partial class AprTelecom1 : ContentPage
{
    private FileResult fotoCapturada;
    private string tipoAtividadeSelecionada = string.Empty;

    public AprTelecom1()
    {
        InitializeComponent();

        // Configuração obrigatória da licença comunitária do QuestPDF
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // Executado assim que a página aparece na tela, antes de exibir os EPIs para marcação
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Garante que a pergunta só será feita se ainda não tiver sido respondida nesta navegação
        if (string.IsNullOrEmpty(tipoAtividadeSelecionada))
        {
            string acao = await DisplayActionSheet(
                "Confirmação de Segurança",
                "Cancelar",
                null,
                "Trabalho no Mesmo Nível",
                "Trabalho em Altura");

            if (acao == "Cancelar" || string.IsNullOrEmpty(acao))
            {
                // Se cancelar, volta para a tela anterior
                await Navigation.PopAsync();
                return;
            }

            tipoAtividadeSelecionada = acao;
            AtualizarVisibilidadeEpis();
        }
    }

    // Controla quais switches aparecem na tela de acordo com a escolha prévia
    private void AtualizarVisibilidadeEpis()
    {
        bool mesmoNivel = (tipoAtividadeSelecionada == "Trabalho no Mesmo Nível");

        // Os 4 primeiros ficam sempre visíveis (Capacete, Luvas, Bota, Óculos)
        if (Opcao1Switch != null) Opcao1Switch.IsVisible = true;
        if (Opcao2Switch != null) Opcao2Switch.IsVisible = true;
        if (Opcao3Switch != null) Opcao3Switch.IsVisible = true;
        if (Opcao4Switch != null) Opcao4Switch.IsVisible = true;

        // Do 5 ao 10 aparecem apenas se NÃO for no mesmo nível (Trabalho em Altura)
        if (Opcao5Switch != null) Opcao5Switch.IsVisible = !mesmoNivel;
        if (Opcao6Switch != null) Opcao6Switch.IsVisible = !mesmoNivel;
        if (Opcao7Switch != null) Opcao7Switch.IsVisible = !mesmoNivel;
        if (Opcao8Switch != null) Opcao8Switch.IsVisible = !mesmoNivel;
        if (Opcao9Switch != null) Opcao9Switch.IsVisible = !mesmoNivel;
        if (Opcao10Switch != null) Opcao10Switch.IsVisible = !mesmoNivel;
    }

    // Método acionado ao clicar no botão da câmera
    private async void OnTirarFotoClicked(object sender, EventArgs e)
    {
        string acao = await DisplayActionSheet(
            "Capturar Evidência",
            "Cancelar",
            null,
            "Câmera Traseira",
            "Câmera Frontal (Selfie)",
            "Escolher da Galeria");

        if (acao == "Cancelar" || string.IsNullOrEmpty(acao))
            return;

        try
        {
            FileResult photo = null;

            if (acao == "Câmera Traseira" && MediaPicker.Default.IsCaptureSupported)
            {
                photo = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions { Title = "Foto da Evidência" });
            }
            else if (acao == "Câmera Frontal (Selfie)")
            {
                photo = await MediaPicker.Default.CapturePhotoAsync();
            }
            else if (acao == "Escolher da Galeria")
            {
                photo = await MediaPicker.Default.PickPhotoAsync();
            }

            if (photo != null)
            {
                fotoCapturada = photo;
                using (Stream stream = await photo.OpenReadAsync())
                {
                    FotoEvidenciaImage.Source = ImageSource.FromStream(() => stream);
                    FotoEvidenciaImage.IsVisible = true;
                }
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", $"Não foi possível obter a imagem: {ex.Message}", "OK");
        }
    }

    // Método para Salvar os dados e Gerar o PDF automaticamente na pasta Downloads
    private async void OnSalvarClicked(object sender, EventArgs e)
    {
        // 1. Pede o nome do usuário para compor o nome do arquivo
        string nomeUsuario = await DisplayPromptAsync("Identificação", "Digite o seu nome para o registro da APR:");
        if (string.IsNullOrWhiteSpace(nomeUsuario))
        {
            await DisplayAlert("Aviso", "O nome do usuário é obrigatório para gerar o PDF.", "OK");
            return;
        }

        // Limpa caracteres especiais do nome para evitar erros ao salvar o arquivo
        string nomeArquivoSeguro = string.Concat(nomeUsuario.Where(c => char.IsLetterOrDigit(c) || c == ' ')).Trim().Replace(" ", "_");
        string dataHoraStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"APR_{nomeArquivoSeguro}_{dataHoraStr}.pdf";

        // 2. Coleta o estado dos switches com base na regra de negócio escolhida no início
        bool[] opcoes;
        string[] nomesOpcoes;

        if (tipoAtividadeSelecionada == "Trabalho no Mesmo Nível")
        {
            opcoes = new bool[] {
                Opcao1Switch.IsToggled,
                Opcao2Switch.IsToggled,
                Opcao3Switch.IsToggled,
                Opcao4Switch.IsToggled
            };
            nomesOpcoes = new string[] { "Capacete", "Luvas", "Bota", "Óculos" };
        }
        else
        {
            opcoes = new bool[] {
                Opcao1Switch.IsToggled, Opcao2Switch.IsToggled, Opcao3Switch.IsToggled,
                Opcao4Switch.IsToggled, Opcao5Switch.IsToggled, Opcao6Switch.IsToggled,
                Opcao7Switch.IsToggled, Opcao8Switch.IsToggled, Opcao9Switch.IsToggled,
                Opcao10Switch.IsToggled
            };
            nomesOpcoes = new string[] {
                "Item 1", "Item 2", "Item 3", "Item 4", "Item 5",
                "Item 6", "Item 7", "Item 8", "Item 9", "Item 10"
            };
        }

        bool todosOk = opcoes.All(o => o);

        try
        {
            // 3. Converte a foto capturada em bytes (se existir) para inserir no PDF
            byte[] fotoBytes = null;
            if (fotoCapturada != null)
            {
                using (var stream = await fotoCapturada.OpenReadAsync())
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        await stream.CopyToAsync(memoryStream);
                        fotoBytes = memoryStream.ToArray();
                    }
                }
            }

            // 4. Criação do documento PDF estruturado via QuestPDF em memória
            using var pdfMemoryStream = new MemoryStream();

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(QColors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontColor(QColors.Grey.Darken3));

                    // Cabeçalho do PDF
                    page.Header().Text("Relatório de APR - Telecomunicações")
                        .SemiBold().FontSize(18).FontColor(QColors.Blue.Medium);

                    // Conteúdo principal
                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        col.Spacing(10);

                        // Metadados do Registro
                        col.Item().Text($"Colaborador: {nomeUsuario}");
                        col.Item().Text($"Tipo de Atividade: {tipoAtividadeSelecionada}");
                        col.Item().Text($"Data e Hora: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
                        col.Item().Text($"Status Geral: {(todosOk ? "Todos os itens OK" : "Pendências encontradas")}");

                        col.Item().LineHorizontal(1).LineColor(QColors.Grey.Lighten1);

                        // Lista dos itens de verificação filtrados
                        col.Item().Text("Itens de Verificação:").Bold();
                        for (int i = 0; i < opcoes.Length; i++)
                        {
                            string statusTexto = opcoes[i] ? "Conforme (OK)" : "Não Conforme / Desligado";
                            col.Item().Text($"• {nomesOpcoes[i]}: {statusTexto}");
                        }

                        // Adiciona a evidência fotográfica se houver
                        if (fotoBytes != null)
                        {
                            col.Item().PaddingTop(10).Text("Evidência Fotográfica:").Bold();
                            col.Item().MaxWidth(250).MaxHeight(250).Image(fotoBytes);
                        }
                    });

                    // Rodapé do PDF
                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Gerado por aplicativo MAUI - Página ");
                        x.CurrentPageNumber();
                    });
                });
            })
            .GeneratePdf(pdfMemoryStream);

            pdfMemoryStream.Position = 0;

            // 5. Define automaticamente o caminho para a pasta Downloads dependendo da plataforma
            string pastaDownloads = string.Empty;

#if ANDROID
            pastaDownloads = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath
                             ?? FileSystem.Current.AppDataDirectory;
#else
            pastaDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(pastaDownloads))
            {
                pastaDownloads = FileSystem.Current.AppDataDirectory;
            }
#endif

            string filePath = Path.Combine(pastaDownloads, fileName);

            // Grava o arquivo físico diretamente na pasta de Downloads
            using (var fileStream = File.Create(filePath))
            {
                await pdfMemoryStream.CopyToAsync(fileStream);
            }

            // 6. Alerta de sucesso informando onde o arquivo foi salvo automaticamente
            await DisplayAlert("Sucesso", $"PDF salvo automaticamente em:\n{filePath}", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", $"Não foi possível gerar o PDF: {ex.Message}", "OK");
        }
    }

    // Método para o botão Sair
    private async void OnSairClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }
}