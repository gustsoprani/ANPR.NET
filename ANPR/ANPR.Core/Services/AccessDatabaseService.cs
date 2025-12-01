using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using ANPR.Shared.Models;
using ANPR.Shared.Interfaces;

namespace ANPR.Core.Services
{
    /// <summary>
    /// Serviço de banco de dados para controle de acesso
    /// Implementa busca com Levenshtein distance para tolerância
    /// </summary>
    public class AccessDatabaseService : IAccessDatabase
    {
        private readonly List<DatabaseVehicle> _vehicles;
        // Agora o campo será usado
        private readonly int _maxLevenshteinDistance = 3; // Tolerar 3 caractere diferente

        public AccessDatabaseService()
        {
            _vehicles = new List<DatabaseVehicle>();
            InitializeDefaultVehicles();
        }

        /// <summary>
        /// Inicializa banco com veículos de teste
        /// </summary>
        private void InitializeDefaultVehicles()
        {
            _vehicles.Add(new DatabaseVehicle
            {
                Id = 1,
                PlateNumber = "PWY3G12",
                OwnerName = "João Silva",
                VehicleModel = "Volkswagen Golf",
                VehicleColor = "Branco",
                IsActive = true,
                RegisteredDate = DateTime.Now
            });

            _vehicles.Add(new DatabaseVehicle
            {
                Id = 2,
                PlateNumber = "OZU5J50",
                OwnerName = "Maria Santos",
                VehicleModel = "Toyota Corolla",
                VehicleColor = "Preto",
                IsActive = true,
                RegisteredDate = DateTime.Now
            });

            _vehicles.Add(new DatabaseVehicle
            {
                Id = 3,
                PlateNumber = "POX4G21",
                OwnerName = "Carlos Costa",
                VehicleModel = "Honda Civic",
                VehicleColor = "Prata",
                IsActive = true,
                RegisteredDate = DateTime.Now
            });

            Console.WriteLine($"📦 {_vehicles.Count} veículos carregados do banco de dados");
        }

        /// <summary>
        /// Busca veículo por placa com tolerância a erros
        /// </summary>
        public DatabaseVehicle FindVehicle(string plateNumber)
        {
            if (string.IsNullOrEmpty(plateNumber))
                return null;

            // Primeiro tentar match exato
            var exact = _vehicles.FirstOrDefault(v => v.PlateNumber == plateNumber && v.IsActive);
            if (exact != null)
            {
                Console.WriteLine($"✅ Match exato encontrado: {plateNumber}");
                return exact;
            }

            // Match fuzzy com tolerância de _maxLevenshteinDistance caracteres
            var fuzzy = _vehicles
                .Where(v => v.IsActive)
                .Select(v => new { Vehicle = v, Distance = GetLevenshteinDistance(plateNumber, v.PlateNumber) })
                .Where(x => x.Distance <= _maxLevenshteinDistance) // ⬅️ ALTERADO: Usa _maxLevenshteinDistance
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

            if (fuzzy != null)
            {
                Console.WriteLine($"⚠️ Match fuzzy encontrado: {fuzzy.Vehicle.PlateNumber} (distância: {fuzzy.Distance})");
                return fuzzy.Vehicle;
            }

            Console.WriteLine($"❌ Nenhum match encontrado para: {plateNumber}");
            return null;
        }

        /// <summary>
        /// Implementação de Levenshtein distance
        /// Calcula número mínimo de edições para transformar uma string em outra
        /// </summary>
        public int GetLevenshteinDistance(string s1, string s2)
        {
            if (s1 == s2) return 0;
            if (s1.Length == 0) return s2.Length;
            if (s2.Length == 0) return s1.Length;

            int[] row0 = new int[s2.Length + 1];
            int[] row1 = new int[s2.Length + 1];

            for (int i = 0; i <= s2.Length; i++)
                row0[i] = i;

            for (int i = 1; i <= s1.Length; i++)
            {
                row1[0] = i;

                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    row1[j] = Math.Min(
                        Math.Min(row0[j] + 1, row1[j - 1] + 1),
                        row0[j - 1] + cost);
                }

                // Swap
                var tmp = row0;
                row0 = row1;
                row1 = tmp;
            }

            return row0[s2.Length];
        }

        /// <summary>
        /// Registra acesso no log
        /// </summary>
        public bool LogAccess(AccessControlResult result)
        {
            try
            {
                string status = result.IsAuthorized ? "✅ ACESSO PERMITIDO" : "❌ ACESSO NEGADO";
                Console.WriteLine($"\n{'=' * 60}");
                Console.WriteLine($"{status}");
                Console.WriteLine($"Placa: {result.PlateText}");
                Console.WriteLine($"Veículo: {result.VehicleInfo}");
                Console.WriteLine($"Data/Hora: {result.AccessTime:dd/MM/yyyy HH:mm:ss}");
                Console.WriteLine($"Motivo: {result.Reason}");
                Console.WriteLine($"{'=' * 60}\n");

                // Aqui você poderia persistir em banco de dados real
                // await _context.AccessLogs.AddAsync(new AccessLog { ... });

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro ao registrar acesso: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Obtém todos os veículos cadastrados
        /// </summary>
        public List<DatabaseVehicle> GetAllVehicles()
        {
            return _vehicles.Where(v => v.IsActive).ToList();
        }

        /// <summary>
        /// Adiciona novo veículo
        /// </summary>
        public void AddVehicle(DatabaseVehicle vehicle)
        {
            if (vehicle == null)
                throw new ArgumentNullException(nameof(vehicle));

            if (_vehicles.Any(v => v.PlateNumber == vehicle.PlateNumber))
                throw new InvalidOperationException($"Veículo com placa {vehicle.PlateNumber} já existe");

            vehicle.Id = _vehicles.Max(v => v.Id) + 1;
            vehicle.RegisteredDate = DateTime.Now;
            _vehicles.Add(vehicle);

            Console.WriteLine($"✅ Veículo adicionado: {vehicle.PlateNumber} - {vehicle.OwnerName}");
        }

        /// <summary>
        /// Remove veículo (soft delete)
        /// </summary>
        public bool RemoveVehicle(string plateNumber)
        {
            var vehicle = _vehicles.FirstOrDefault(v => v.PlateNumber == plateNumber);
            if (vehicle == null)
                return false;

            vehicle.IsActive = false;
            Console.WriteLine($"❌ Veículo desativado: {plateNumber}");
            return true;
        }

        public void Dispose()
        {
            _vehicles.Clear();
        }
    }
}