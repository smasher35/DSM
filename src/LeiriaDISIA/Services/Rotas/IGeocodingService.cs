namespace LeiriaDISIA.Services.Rotas;

public record CoordenadaGeografica(double Latitude, double Longitude);

public record ResultadoGeocodificacao(bool Sucesso, CoordenadaGeografica? Coordenada, string? MensagemErro)
{
    public static ResultadoGeocodificacao Ok(CoordenadaGeografica coordenada) => new(true, coordenada, null);
    public static ResultadoGeocodificacao Falha(string mensagem) => new(false, null, mensagem);
}

public record ResultadoDistancia(bool Sucesso, double? DistanciaKm, int? DuracaoMinutos, string? MensagemErro)
{
    public static ResultadoDistancia Ok(double distanciaKm, int duracaoMinutos) => new(true, distanciaKm, duracaoMinutos, null);
    public static ResultadoDistancia Falha(string mensagem) => new(false, null, null, mensagem);
}

/// <summary>Uma paragem de uma rota já otimizada — <see cref="IndiceOriginal"/> refere-se à posição
/// da paragem na lista pedida a <see cref="IRoutingService.OtimizarRotaAsync"/>, para o chamador
/// conseguir voltar a associá-la ao Pedido/Escola correto depois de reordenada.</summary>
public record ParagemRotaOtimizada(int IndiceOriginal, double DistanciaDesdeAnteriorKm, int DuracaoDesdeAnteriorMinutos);

/// <param name="DistanciaRegressoKm">Distância/duração do troço final de regresso à sede, quando
/// pedido (ver <see cref="IRoutingService.OtimizarRotaAsync"/>) — já estão somadas em
/// <see cref="DistanciaTotalKm"/>/<see cref="DuracaoTotalMinutos"/>, mas ficam também disponíveis em
/// separado para a UI poder mostrar esse troço como uma linha própria, em vez de o total "aparecer"
/// maior do que a soma das paragens visíveis sem explicação.</param>
public record ResultadoOtimizacaoRota(
    bool Sucesso, List<ParagemRotaOtimizada> Paragens, double DistanciaTotalKm, int DuracaoTotalMinutos, string? MensagemErro,
    double? DistanciaRegressoKm = null, int? DuracaoRegressoMinutos = null)
{
    public static ResultadoOtimizacaoRota Ok(
        List<ParagemRotaOtimizada> paragens, double distanciaTotalKm, int duracaoTotalMinutos,
        double? distanciaRegressoKm = null, int? duracaoRegressoMinutos = null) =>
        new(true, paragens, distanciaTotalKm, duracaoTotalMinutos, null, distanciaRegressoKm, duracaoRegressoMinutos);

    public static ResultadoOtimizacaoRota Falha(string mensagem) => new(false, new List<ParagemRotaOtimizada>(), 0, 0, mensagem);
}

/// <summary>
/// Geocodificação de uma morada (texto) para coordenadas (latitude/longitude). Interface desacoplada
/// do fornecedor concreto (ver <see cref="OpenRouteServiceClient"/>) — trocar de fornecedor no
/// futuro (ex.: Google Maps Platform) implica só criar uma nova implementação desta interface,
/// sem tocar em mais nenhum sítio da aplicação (Escola, Planeamento de Rota, PDF).
/// </summary>
public interface IGeocodingService
{
    Task<ResultadoGeocodificacao> GeocodificarAsync(string moradaCompleta, CancellationToken ct = default);
}

/// <summary>
/// Cálculo de distâncias/rotas rodoviárias reais (nunca em linha reta) entre coordenadas já
/// geocodificadas. Ver <see cref="IGeocodingService"/> para o desacoplamento do fornecedor.
/// </summary>
public interface IRoutingService
{
    /// <summary>Distância/duração rodoviária direta entre dois pontos — usado para a distância
    /// Escola ↔ sede do Município (ver Services/Rotas/EscolaGeocodingService.cs).</summary>
    Task<ResultadoDistancia> CalcularDistanciaAsync(CoordenadaGeografica origem, CoordenadaGeografica destino, CancellationToken ct = default);

    /// <summary>Rota com múltiplas paragens: parte de <paramref name="origem"/>, começa sempre pela
    /// paragem mais longe da origem e, a partir daí, vai sempre para a paragem não visitada mais
    /// perto da anterior (vizinho mais próximo) — nunca passa pela origem entre paragens. Regressa a
    /// <paramref name="regresso"/> no fim, se indicado (<c>null</c> = não regressa).</summary>
    Task<ResultadoOtimizacaoRota> OtimizarRotaAsync(
        CoordenadaGeografica origem, List<CoordenadaGeografica> paragens, CoordenadaGeografica? regresso, CancellationToken ct = default);
}
