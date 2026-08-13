using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;
using DuckDB.NET.Data;

namespace TradeDataAnalysis
{
    public class TradeDataService
    {
        public List<TradeBar> AllBars { get; private set; } = new List<TradeBar>();
        public List<Trade> AllTrades { get; private set; } = new List<Trade>();

        public bool HasData => AllBars.Count > 0;

        private readonly string _parquetDirectory;

        public string BarsParquetPath { get; }
        public string TradesParquetPath { get; }

        public TradeDataService()
        {
            _parquetDirectory = Path.Combine(Path.GetTempPath(), "TradeDataAnalysis");
            Directory.CreateDirectory(_parquetDirectory);

            BarsParquetPath = Path.Combine(_parquetDirectory, "bars.parquet");
            TradesParquetPath = Path.Combine(_parquetDirectory, "trades.parquet");
        }

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

            ExportToParquet();

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

        /// <summary>
        /// Writes the currently loaded bars (and a derived per-trade summary) to Parquet files
        /// using DuckDB, so they can later be queried via SQL.
        /// </summary>
        private void ExportToParquet()
        {
            if (File.Exists(BarsParquetPath)) File.Delete(BarsParquetPath);
            if (File.Exists(TradesParquetPath)) File.Delete(TradesParquetPath);

            using var connection = new DuckDBConnection("Data Source=:memory:");
            connection.Open();

            using (var createCmd = connection.CreateCommand())
            {
                createCmd.CommandText = @"
                    CREATE TABLE Bars (
                        StrategyVersion VARCHAR,
                        TradeId VARCHAR,
                        BarsSinceEntry INTEGER,
                        Time TIMESTAMP,
                        Open DOUBLE,
                        High DOUBLE,
                        Low DOUBLE,
                        Close DOUBLE,
                        Volume BIGINT,
                        MarketPosition VARCHAR,
                        EntryPrice DOUBLE,
                        Quantity INTEGER,
                        CurrentStop DOUBLE,
                        InitialRisk DOUBLE,
                        CurrentR DOUBLE,
                        OpenPnL DOUBLE,
                        MFE DOUBLE,
                        MAE DOUBLE,
                        VWAP DOUBLE,
                        VWAPSlope5 DOUBLE,
                        DistanceFromVWAP DOUBLE,
                        EMAFast DOUBLE,
                        EMASlow DOUBLE,
                        EMASpread DOUBLE,
                        EMASpreadSlope5 DOUBLE,
                        ATR DOUBLE,
                        ADX DOUBLE,
                        ADXSlope DOUBLE,
                        DIMinus DOUBLE,
                        DIPlus DOUBLE
                    );";
                createCmd.ExecuteNonQuery();
            }

            using (var appender = connection.CreateAppender("Bars"))
            {
                foreach (var bar in AllBars)
                {
                    appender.CreateRow()
                        .AppendValue(bar.StrategyVersion)
                        .AppendValue(bar.TradeId)
                        .AppendValue(bar.BarsSinceEntry)
                        .AppendValue(bar.Time)
                        .AppendValue(bar.Open)
                        .AppendValue(bar.High)
                        .AppendValue(bar.Low)
                        .AppendValue(bar.Close)
                        .AppendValue(bar.Volume)
                        .AppendValue(bar.MarketPosition)
                        .AppendValue(bar.EntryPrice)
                        .AppendValue(bar.Quantity)
                        .AppendValue(bar.CurrentStop)
                        .AppendValue(bar.InitialRisk)
                        .AppendValue(bar.CurrentR)
                        .AppendValue(bar.OpenPnL)
                        .AppendValue(bar.MFE)
                        .AppendValue(bar.MAE)
                        .AppendValue(bar.VWAP)
                        .AppendValue(bar.VWAPSlope5)
                        .AppendValue(bar.DistanceFromVWAP)
                        .AppendValue(bar.EMAFast)
                        .AppendValue(bar.EMASlow)
                        .AppendValue(bar.EMASpread)
                        .AppendValue(bar.EMASpreadSlope5)
                        .AppendValue(bar.ATR)
                        .AppendValue(bar.ADX)
                        .AppendValue(bar.ADXSlope)
                        .AppendValue(bar.DIMinus)
                        .AppendValue(bar.DIPlus)
                        .EndRow();
                }
            }

            using (var copyBarsCmd = connection.CreateCommand())
            {
                copyBarsCmd.CommandText =
                    $"COPY Bars TO '{BarsParquetPath.Replace("'", "''")}' (FORMAT PARQUET);";
                copyBarsCmd.ExecuteNonQuery();
            }

            // Derived per-trade summary (mirrors the old Trade.FinalR/FinalPnL/IsWinner logic,
            // picking values from the bar with the highest BarsSinceEntry per trade).
            using (var tradesCmd = connection.CreateCommand())
            {
                tradesCmd.CommandText = @"
                    CREATE TABLE Trades AS
                    SELECT
                        TradeId,
                        COUNT(*) AS BarCount,
                        MIN(Time) AS EntryTime,
                        MAX(Time) AS FinalTime,
                        arg_max(CurrentR, BarsSinceEntry) AS FinalR,
                        arg_max(OpenPnL, BarsSinceEntry) AS FinalPnL,
                        arg_max(CurrentR, BarsSinceEntry) > 0 AS IsWinner,
                        MIN(MAE) AS MAE,
                        MAX(MFE) AS MFE
                    FROM Bars
                    GROUP BY TradeId;";
                tradesCmd.ExecuteNonQuery();
            }

            using (var copyTradesCmd = connection.CreateCommand())
            {
                copyTradesCmd.CommandText =
                    $"COPY Trades TO '{TradesParquetPath.Replace("'", "''")}' (FORMAT PARQUET);";
                copyTradesCmd.ExecuteNonQuery();
            }
        }
    }
}