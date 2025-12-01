using Microsoft.EntityFrameworkCore;
using ANPR.Shared.Models;

namespace ANPR.Core.Data
{
    public class AnprDbContext : DbContext
    {
        public DbSet<DatabaseVehicle> Vehicles { get; set; }
        public DbSet<AccessLog> AccessLogs { get; set; }

        public AnprDbContext(DbContextOptions<AnprDbContext> options) : base(options)
        {
        }

        // Configuração de fallback caso não seja injetado (bom para testes)
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite("Data Source=anpr.db");
            }
        }
    }
}