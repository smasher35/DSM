using System;
using System.IO;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Data;

public class AppDbContext : DbContext
{
    public static string DbPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LeiriaDISIA", "disia.db");

    public DbSet<Agrupamento> Agrupamentos => Set<Agrupamento>();
    public DbSet<Escola> Escolas => Set<Escola>();
    public DbSet<Contacto> Contactos => Set<Contacto>();
    public DbSet<PedidoIntervencao> PedidosIntervencao => Set<PedidoIntervencao>();
    public DbSet<Intervencao> Intervencoes => Set<Intervencao>();
    public DbSet<CategoriaIntervencao> CategoriasIntervencao => Set<CategoriaIntervencao>();
    public DbSet<SubCategoriaIntervencao> SubCategoriasIntervencao => Set<SubCategoriaIntervencao>();
    public DbSet<IntervencaoCategoria> IntervencaoCategorias => Set<IntervencaoCategoria>();
    public DbSet<Equipamento> Equipamentos => Set<Equipamento>();
    public DbSet<EquipamentoAbatido> EquipamentosAbatidos => Set<EquipamentoAbatido>();
    public DbSet<EquipamentoRecolhido> EquipamentosRecolhidos => Set<EquipamentoRecolhido>();
    public DbSet<IntervencaoEquipamento> IntervencaoEquipamentos => Set<IntervencaoEquipamento>();
    public DbSet<Comunicacao> Comunicacoes => Set<Comunicacao>();
    public DbSet<CategoriaDisia> CategoriasDisia => Set<CategoriaDisia>();
    public DbSet<AtividadeDisia> AtividadesDisia => Set<AtividadeDisia>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<ValorFixo> ValoresFixos => Set<ValorFixo>();
    public DbSet<EstadoCorPersonalizada> EstadosCorPersonalizados => Set<EstadoCorPersonalizada>();
    public DbSet<CaracteristicaEquipamento> CaracteristicasEquipamento => Set<CaracteristicaEquipamento>();
    public DbSet<EquipamentoCaracteristicaValor> EquipamentoCaracteristicaValores => Set<EquipamentoCaracteristicaValor>();
    public DbSet<CaracteristicaEquipamentoOpcao> CaracteristicaEquipamentoOpcoes => Set<CaracteristicaEquipamentoOpcao>();
    public DbSet<RelatorioMensalDados> RelatoriosMensaisDados => Set<RelatorioMensalDados>();
    public DbSet<PlanoRota> PlanosRota => Set<PlanoRota>();
    public DbSet<PlanoRotaParagem> PlanoRotaParagens => Set<PlanoRotaParagem>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        options.UseSqlite($"Data Source={DbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Agrupamento>()
            .HasIndex(a => a.CodAgrupamento).IsUnique();

        modelBuilder.Entity<Escola>()
            .HasIndex(e => e.CodEscola).IsUnique();

        modelBuilder.Entity<Escola>()
            .HasOne(e => e.Agrupamento)
            .WithMany(a => a.Escolas)
            .HasForeignKey(e => e.AgrupamentoId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Contacto>()
            .HasOne(c => c.Escola)
            .WithMany(e => e.Contactos)
            .HasForeignKey(c => c.EscolaId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<PedidoIntervencao>()
            .HasOne(p => p.Escola)
            .WithMany(e => e.Pedidos)
            .HasForeignKey(p => p.EscolaId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PedidoIntervencao>()
            .HasOne(p => p.Agrupamento)
            .WithMany()
            .HasForeignKey(p => p.AgrupamentoId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PedidoIntervencao>()
            .HasOne(p => p.Intervencao)
            .WithOne(i => i.PedidoOrigem)
            .HasForeignKey<PedidoIntervencao>(p => p.IntervencaoId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Intervencao>()
            .HasOne(i => i.Escola)
            .WithMany(e => e.Intervencoes)
            .HasForeignKey(i => i.EscolaId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Intervencao>()
            .HasOne(i => i.Agrupamento)
            .WithMany()
            .HasForeignKey(i => i.AgrupamentoId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IntervencaoCategoria>()
            .HasOne(ic => ic.Intervencao)
            .WithMany(i => i.Categorias)
            .HasForeignKey(ic => ic.IntervencaoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<IntervencaoCategoria>()
            .HasOne(ic => ic.Categoria)
            .WithMany()
            .HasForeignKey(ic => ic.CategoriaIntervencaoId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SubCategoriaIntervencao>()
            .HasOne(s => s.Categoria)
            .WithMany(c => c.SubCategorias)
            .HasForeignKey(s => s.CategoriaIntervencaoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Equipamento>()
            .HasIndex(e => e.NumeroSerie).IsUnique();
        modelBuilder.Entity<Equipamento>()
            .HasIndex(e => e.NumeroInventario).IsUnique();

        modelBuilder.Entity<Equipamento>()
            .HasOne(e => e.Escola)
            .WithMany(e => e.Equipamentos)
            .HasForeignKey(e => e.EscolaId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<EquipamentoAbatido>()
            .HasOne(a => a.Equipamento)
            .WithOne(e => e.Abate)
            .HasForeignKey<EquipamentoAbatido>(a => a.EquipamentoId)
            .OnDelete(DeleteBehavior.SetNull);

        // (1.3) Valores das características adicionais definidas pelo administrador em Dados
        // Fixos. Um equipamento eliminado arrasta consigo os seus valores; uma característica
        // eliminada em Dados Fixos arrasta os valores gravados para essa característica.
        modelBuilder.Entity<EquipamentoCaracteristicaValor>()
            .HasOne(v => v.Equipamento)
            .WithMany()
            .HasForeignKey(v => v.EquipamentoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<EquipamentoCaracteristicaValor>()
            .HasOne(v => v.CaracteristicaEquipamento)
            .WithMany()
            .HasForeignKey(v => v.CaracteristicaEquipamentoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<EquipamentoCaracteristicaValor>()
            .HasIndex(v => new { v.EquipamentoId, v.CaracteristicaEquipamentoId }).IsUnique();

        // (1.4) Lista de valores sugeridos (opcionais) de uma característica adicional. Eliminar a
        // característica em Dados Fixos arrasta consigo os valores sugeridos da sua lista.
        modelBuilder.Entity<CaracteristicaEquipamentoOpcao>()
            .HasOne(o => o.CaracteristicaEquipamento)
            .WithMany()
            .HasForeignKey(o => o.CaracteristicaEquipamentoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AtividadeDisia>()
            .HasOne(a => a.Categoria)
            .WithMany()
            .HasForeignKey(a => a.CategoriaDisiaId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<EquipamentoAbatido>()
            .HasOne(a => a.Intervencao)
            .WithMany()
            .HasForeignKey(a => a.IntervencaoId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<EquipamentoRecolhido>()
            .HasOne(r => r.Equipamento)
            .WithMany()
            .HasForeignKey(r => r.EquipamentoId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EquipamentoRecolhido>()
            .HasOne(r => r.Intervencao)
            .WithMany()
            .HasForeignKey(r => r.IntervencaoId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<EquipamentoRecolhido>()
            .HasOne(r => r.IntervencaoDisia)
            .WithMany()
            .HasForeignKey(r => r.IntervencaoDisiaId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<EquipamentoRecolhido>()
            .HasOne(r => r.AtividadeDisia)
            .WithMany(a => a.EquipamentosRecolhidos)
            .HasForeignKey(r => r.AtividadeDisiaId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<IntervencaoEquipamento>()
            .HasOne(ie => ie.Intervencao)
            .WithMany(i => i.EquipamentosIntervencionados)
            .HasForeignKey(ie => ie.IntervencaoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<IntervencaoEquipamento>()
            .HasOne(ie => ie.Equipamento)
            .WithMany()
            .HasForeignKey(ie => ie.EquipamentoId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Comunicacao>()
            .HasOne(c => c.Escola)
            .WithMany()
            .HasForeignKey(c => c.EscolaId)
            .OnDelete(DeleteBehavior.Cascade);

        // Enums guardados como texto para legibilidade direta na BD
        modelBuilder.Entity<Escola>().Property(e => e.Tipo).HasConversion<string>();
        modelBuilder.Entity<Intervencao>().Property(i => i.Estado).HasConversion<string>();
        modelBuilder.Entity<PedidoIntervencao>().Property(p => p.Estado).HasConversion<string>();
        modelBuilder.Entity<AtividadeDisia>().Property(a => a.Estado).HasConversion<string>();

        modelBuilder.Entity<Usuario>()
            .HasIndex(u => u.NomeUtilizador).IsUnique();
        modelBuilder.Entity<Usuario>().Property(u => u.Perfil).HasConversion<string>();

        // ---- Planeamento de Rotas ----
        modelBuilder.Entity<PlanoRota>()
            .HasOne(p => p.CriadoPorUsuario)
            .WithMany()
            .HasForeignKey(p => p.CriadoPorUsuarioId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<PlanoRota>().Property(p => p.Estado).HasConversion<string>();

        modelBuilder.Entity<PlanoRotaParagem>()
            .HasOne(pp => pp.PlanoRota)
            .WithMany(p => p.Paragens)
            .HasForeignKey(pp => pp.PlanoRotaId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict (não Cascade): um Pedido/Escola nunca deve poder ser apagado silenciosamente só
        // porque está associado a uma paragem de rota — o utilizador tem de resolver isso primeiro
        // (cancelar o plano ou remover a paragem), tal como acontece com as restantes relações a
        // Escola/PedidoIntervencao na aplicação.
        modelBuilder.Entity<PlanoRotaParagem>()
            .HasOne(pp => pp.PedidoIntervencao)
            .WithMany()
            .HasForeignKey(pp => pp.PedidoIntervencaoId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PlanoRotaParagem>()
            .HasOne(pp => pp.Escola)
            .WithMany()
            .HasForeignKey(pp => pp.EscolaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Um pedido nunca pode aparecer duas vezes no MESMO plano (a regra de não duplicar em
        // planos ativos para a mesma data é aplicada em código — PlaneamentoRotaService — porque
        // depende do Estado do plano, algo que uma restrição de unicidade da base de dados não
        // consegue exprimir sozinha).
        modelBuilder.Entity<PlanoRotaParagem>()
            .HasIndex(pp => new { pp.PlanoRotaId, pp.PedidoIntervencaoId }).IsUnique();

        modelBuilder.Entity<PedidoIntervencao>().Property(p => p.Prioridade).HasConversion<string>();
    }
}
