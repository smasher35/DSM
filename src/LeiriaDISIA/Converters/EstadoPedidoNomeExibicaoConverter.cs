using System.Globalization;
using System.Windows.Data;
using LeiriaDISIA.Models;

namespace LeiriaDISIA.Converters;

/// <summary>
/// Converte um valor do enum <see cref="EstadoPedido"/> no respetivo nome de exibição
/// configurado em Administração → Dados Fixos → "Estados dos Pedidos" (ou no nome do
/// próprio enum, se ainda não houver personalização gravada).
/// Usado para que renomear um estado ali se reflita de facto no formulário de pedidos
/// de intervenção e nas listagens — antes, estes controlos mostravam sempre o nome
/// técnico do enum (ex.: "EmAndamento"), sem qualquer ligação a Dados Fixos.
/// </summary>
public class EstadoPedidoNomeExibicaoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is EstadoPedido estado ? EstadoCores.NomeExibicaoEstadoPedido(estado) : value ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
