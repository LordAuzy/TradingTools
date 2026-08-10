using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;

namespace TradeDataAnalysis
{
    public class TradeDataService
    {
        public List<TradeBar> AllBars { get; private set; } = new List<TradeBar>();
        public List<Trade> AllTrades { get; private set; } = new List<Trade>();

        public bool HasData => AllBars.Count > 0;

        /// <summary>
        /// Reads all CSV files from a directory and populates the internal bar and trade collections.
        /// </summary>
        public (int TotalBars, int TotalTrades) LoadCsvData(string dirPath)
        {
            if (string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath))
            {
                throw new DirectoryNotFoundException($"Directory '{dirPath}' does not exist.");
            }

            var files = Directory.GetFiles(dirPath, "TradeBarTelemetry*.csv", SearchOption.AllDirectories);
            var loadedBars = new List<TradeBar>();

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                HeaderValidated = null
            };

            foreach (var file in files)
            {
                try
                {
                    using (var reader = new StreamReader(file))
                    using (var csv = new CsvReader(reader, config))
                    {
                        var records = csv.GetRecords<TradeBar>();
                        loadedBars.AddRange(records);
                    }
                }
                catch
                {
                    // Ignore corrupt or non-matching CSV files
                }
            }

            AllBars = loadedBars;
            AllTrades = AllBars
                .GroupBy(b => b.TradeId)
                .Select(g => new Trade
                {
                    TradeId = g.Key,
                    Bars = g.OrderBy(b => b.BarsSinceEntry).ToList()
                })
                .ToList();

            return (AllBars.Count, AllTrades.Count);
        }

        /// <summary>
        /// Clears loaded dataset from memory.
        /// </summary>
        public void Clear()
        {
            AllBars.Clear();
            AllTrades.Clear();
        }
    }
}