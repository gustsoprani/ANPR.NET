using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ANPR.Shared.Models;
using ANPR.Shared.Interfaces;
using ANPR.Core.Data;

namespace ANPR.Core.Services
{
    public class AccessDatabaseService : IAccessDatabase
    {
        // Cache na RAM para leitura ultrarrápida (O YOLO não pode esperar o disco!)
        private List<DatabaseVehicle> _vehicleCache;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly int _maxLevenshteinDistance = 3;

        public AccessDatabaseService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
            _vehicleCache = new List<DatabaseVehicle>();

            // Carrega os dados do SQLite para a RAM ao iniciar
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AnprDbContext>();

                // Cria o arquivo .db se não existir
                db.Database.EnsureCreated();

                // Carrega para o Cache
                _vehicleCache = db.Vehicles.Where(v => v.IsActive).ToList();

                // Se estiver vazio, cria dados de teste (opcional)
                if (!_vehicleCache.Any())
                {
                    var padrao = new DatabaseVehicle { PlateNumber = "GTT0F37", OwnerName = "Carlos Costa", VehicleModel = "Honda Civic", VehicleColor = "Prata", IsActive = true, RegisteredDate = DateTime.Now };
                    db.Vehicles.Add(padrao);
                    db.SaveChanges();
                    _vehicleCache.Add(padrao);
                    Console.WriteLine("📦 Banco criado e populado com dados padrão.");
                }
                else
                {
                    Console.WriteLine($"📦 {_vehicleCache.Count} veículos carregados do SQLite.");
                }
            }
        }

        // --- LEITURA (Usa Cache - Rápido) ---

        public DatabaseVehicle FindVehicle(string plateNumber)
        {
            // Busca exata primeiro
            var exact = _vehicleCache.FirstOrDefault(v => v.PlateNumber == plateNumber);
            if (exact != null) return exact;

            // Busca Fuzzy (Levenshtein) na memória
            foreach (var v in _vehicleCache)
            {
                int dist = GetLevenshteinDistance(plateNumber, v.PlateNumber);
                if (dist <= _maxLevenshteinDistance)
                {
                    Console.WriteLine($"🔍 Match fuzzy encontrado: {v.PlateNumber} (distância: {dist})");
                    return v;
                }
            }
            Console.WriteLine($"❓ Nenhum match encontrado para: {plateNumber}");
            return null;
        }

        public List<DatabaseVehicle> GetAllVehicles()
        {
            return _vehicleCache.ToList(); // Retorna cópia da lista
        }

        // --- ESCRITA (Usa SQLite + Atualiza Cache) ---

        public void AddVehicle(DatabaseVehicle vehicle)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AnprDbContext>();

                // Salva no Disco
                db.Vehicles.Add(vehicle);
                db.SaveChanges();

                // Atualiza a RAM
                _vehicleCache.Add(vehicle);
                Console.WriteLine($"💾 Veículo salvo no SQLite: {vehicle.PlateNumber}");
            }
        }

        public bool RemoveVehicle(string plateNumber)
        {
            // Remove da RAM
            var cachedVehicle = _vehicleCache.FirstOrDefault(v => v.PlateNumber == plateNumber);
            if (cachedVehicle != null) _vehicleCache.Remove(cachedVehicle);

            // Remove do Disco (Soft Delete)
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AnprDbContext>();
                var dbVehicle = db.Vehicles.FirstOrDefault(v => v.PlateNumber == plateNumber);

                if (dbVehicle != null)
                {
                    dbVehicle.IsActive = false; // Soft Delete
                    // Ou db.Vehicles.Remove(dbVehicle); // Hard Delete
                    db.SaveChanges();
                    Console.WriteLine($"❌ Veículo removido do SQLite: {plateNumber}");
                    return true;
                }
            }
            return false;
        }

        // Algoritmo Levenshtein (Mantido igual)
        public int GetLevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return string.IsNullOrEmpty(s2) ? 0 : s2.Length;
            if (string.IsNullOrEmpty(s2)) return s1.Length;

            int n = s1.Length;
            int m = s2.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (s2[j - 1] == s1[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }

        public bool LogAccess(AccessControlResult result)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AnprDbContext>();

                    var log = new AccessLog
                    {
                        PlateNumber = result.PlateText,
                        VehicleInfo = result.VehicleInfo,
                        IsAuthorized = result.IsAuthorized,
                        Reason = result.Reason,
                        Timestamp = result.AccessTime
                    };

                    db.AccessLogs.Add(log);
                    db.SaveChanges();
                    // Console.WriteLine($"📝 Log salvo: {log.PlateNumber}"); // Opcional
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro ao salvar log: {ex.Message}");
                return false;
            }
        }
        public List<AccessLog> GetHistory(int limit = 100)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AnprDbContext>();

                // Retorna os mais recentes primeiro
                return db.AccessLogs
                         .OrderByDescending(x => x.Timestamp)
                         .Take(limit)
                         .ToList();
            }
        }

        public void Dispose() { }
    }
}