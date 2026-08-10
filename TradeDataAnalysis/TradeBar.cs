using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradeDataAnalysis
{
    public class TradeBar
    {
        public string StrategyVersion { get; set; }
        public string TradeId { get; set; }
        public int BarsSinceEntry { get; set; }
        public DateTime Time { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public long Volume { get; set; }
        public string MarketPosition { get; set; }
        public double EntryPrice { get; set; }
        public int Quantity { get; set; }
        public double CurrentStop { get; set; }
        public double InitialRisk { get; set; }
        public double CurrentR { get; set; }
        public double OpenPnL { get; set; }
        public double MFE { get; set; }
        public double MAE { get; set; }
        public double VWAP { get; set; }
        public double VWAPSlope5 { get; set; }
        public double DistanceFromVWAP { get; set; }
        public double EMAFast { get; set; }
        public double EMASlow { get; set; }
        public double EMASpread { get; set; }
        public double EMASpreadSlope5 { get; set; }
        public double ATR { get; set; }
        public double ADX { get; set; }
        public double ADXSlope { get; set; }
        public double DIMinus { get; set; }
        public double DIPlus { get; set; }
    }

    public class Trade
    {
        public string TradeId { get; set; }
        public List<TradeBar> Bars { get; set; } = new List<TradeBar>();

        public TradeBar EntryBar => Bars.OrderBy(b => b.BarsSinceEntry).FirstOrDefault();
        public TradeBar FinalBar => Bars.OrderBy(b => b.BarsSinceEntry).LastOrDefault();
        public double FinalR => FinalBar?.CurrentR ?? 0;
        public double FinalPnL => FinalBar?.OpenPnL ?? 0;
        public bool IsWinner => FinalR > 0;
    }

    // Global scope visible to Roslyn Scripting
    public class ScriptGlobals
    {
        public List<TradeBar> Bars { get; set; }
        public List<Trade> Trades { get; set; }
    }

    public class SavedQuery
    {
        public string Name { get; set; }
        public string QueryText { get; set; }
        public override string ToString() => Name;
    }
}
