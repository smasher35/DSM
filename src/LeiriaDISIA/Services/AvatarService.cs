using System.IO;
using System.Windows.Media.Imaging;
using LeiriaDISIA.Data;

namespace LeiriaDISIA.Services;

/// <summary>
/// Gerencia o armazenamento e carregamento de avatares de utilizadores.
/// Armazena as imagens na pasta "avatares" dentro do diretório de dados.
/// </summary>
public static class AvatarService
{
    private static readonly string AvataresPasta = Path.Combine(
        Path.GetDirectoryName(AppDbContext.DbPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "avatares"
    );

    public static void InicializarPasta()
    {
        Directory.CreateDirectory(AvataresPasta);
    }

    /// <summary>
    /// Guarda um arquivo de imagem como avatar de um utilizador, redimensionando para 256x256px.
    /// </summary>
    public static void GuardarAvatar(int usuarioId, string caminhoImagemOrigem)
    {
        InicializarPasta();

        var caminhoDestino = Path.Combine(AvataresPasta, $"{usuarioId}.png");

        try
        {
            BitmapSource imagemRedimensionada;

            // Carregar e redimensionar a imagem
            using (var stream = File.OpenRead(caminhoImagemOrigem))
            {
                var imagemOrigem = new BitmapImage();
                imagemOrigem.BeginInit();
                imagemOrigem.StreamSource = stream;
                imagemOrigem.CacheOption = BitmapCacheOption.OnLoad;
                imagemOrigem.EndInit();
                imagemOrigem.Freeze();

                imagemRedimensionada = RedimensionarImagem(imagemOrigem, 256, 256);
            }

            // Guardar como PNG
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(imagemRedimensionada));

            // Se o arquivo existe, remover primeiro para evitar locks
            if (File.Exists(caminhoDestino))
            {
                try
                {
                    File.Delete(caminhoDestino);
                }
                catch { }
            }

            using var streamDestino = File.Create(caminhoDestino);
            encoder.Save(streamDestino);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Erro ao guardar avatar: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Redimensiona uma imagem mantendo aspecto, recortando se necessário.
    /// </summary>
    private static BitmapSource RedimensionarImagem(BitmapSource origem, int largura, int altura)
    {
        // Calcular proporções
        double aspectRatio = (double)origem.PixelWidth / origem.PixelHeight;
        double targetAspect = (double)largura / altura;

        int destWidth, destHeight, startX, startY;

        // Crop se necessário para manter proporção
        if (aspectRatio > targetAspect)
        {
            destHeight = origem.PixelHeight;
            destWidth = (int)(destHeight * targetAspect);
            startX = (origem.PixelWidth - destWidth) / 2;
            startY = 0;
        }
        else
        {
            destWidth = origem.PixelWidth;
            destHeight = (int)(destWidth / targetAspect);
            startX = 0;
            startY = (origem.PixelHeight - destHeight) / 2;
        }

        // Fazer crop
        var croppedBitmap = new CroppedBitmap(origem, new System.Windows.Int32Rect(startX, startY, destWidth, destHeight));

        // Redimensionar
        var scaleTransform = new System.Windows.Media.ScaleTransform(
            (double)largura / croppedBitmap.PixelWidth,
            (double)altura / croppedBitmap.PixelHeight
        );

        var drawingVisual = new System.Windows.Media.DrawingVisual();
        using (var context = drawingVisual.RenderOpen())
        {
            context.DrawImage(croppedBitmap, new System.Windows.Rect(0, 0, largura, altura));
        }

        var renderBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(largura, altura, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        renderBitmap.Render(drawingVisual);
        renderBitmap.Freeze();

        return renderBitmap;
    }

    /// <summary>
    /// Carrega o avatar de um utilizador, retornando null se não existir.
    /// </summary>
    public static BitmapImage? CarregarAvatar(int usuarioId)
    {
        var caminhoAvatar = Path.Combine(AvataresPasta, $"{usuarioId}.png");

        if (!File.Exists(caminhoAvatar))
            return null;

        try
        {
            // Esperar um pouco se o arquivo foi recém-escrito para evitar locks
            System.Threading.Thread.Sleep(100);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(caminhoAvatar, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 200;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Remove o avatar de um utilizador.
    /// </summary>
    public static void RemoverAvatar(int usuarioId)
    {
        var caminhoAvatar = Path.Combine(AvataresPasta, $"{usuarioId}.png");
        if (File.Exists(caminhoAvatar))
        {
            try
            {
                File.Delete(caminhoAvatar);
            }
            catch { /* Ignorar erros ao remover */ }
        }
    }

    /// <summary>
    /// Gera as iniciais do utilizador (ex: "JP" para "João Paulo").
    /// </summary>
    public static string ObterIniciaisNome(string nome)
    {
        var partes = nome.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (partes.Length == 0)
            return "?";

        if (partes.Length == 1)
            return partes[0][0].ToString().ToUpper();

        return $"{partes[0][0]}{partes[^1][0]}".ToUpper();
    }
}
