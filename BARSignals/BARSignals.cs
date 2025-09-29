using cAlgo.API;
using System;
using Utils;

namespace cAlgo.Indicators
{
    public enum SignalKind { Retest, Breakout }

    [Indicator(IsOverlay = true, AccessRights = AccessRights.None)]
    public class BARSignals : Indicator
    {
        [Output("BuySignal", PlotType = PlotType.Points, LineColor = "Transparent")]
        public IndicatorDataSeries BuySignal { get; set; }

        [Output("SellSignal", PlotType = PlotType.Points, LineColor = "Transparent")]
        public IndicatorDataSeries SellSignal { get; set; }

        [Parameter("Opening Range Timeframe", DefaultValue = 15)]
        public int OpeningRangeTF { get; set; }

        [Parameter("Usar Opening Range", DefaultValue = true, Group = "Niveles")]
        public bool UseOpeningRange { get; set; }

        [Parameter("Usar Previous Market (Premarket)", DefaultValue = true, Group = "Niveles")]
        public bool UsePremarket { get; set; }

        [Parameter("Usar Previous Day", DefaultValue = true, Group = "Niveles")]
        public bool UsePreviousDay { get; set; }

        [Parameter("Modo de Señal", DefaultValue = SignalKind.Retest)]
        public SignalKind Mode { get; set; }

        [Parameter("Alertas: Sonido", DefaultValue = false)]
        public bool AlertsSound { get; set; }

        // OR15
        private double rangeHigh = double.NaN;
        private double rangeLow = double.NaN;

        // Previous Day session (regular hours)
        private double prevSessHigh = double.NaN;
        private double prevSessLow = double.NaN;

        // Current day premarket
        private double preMarketHigh = double.NaN;
        private double preMarketLow = double.NaN;

        private DateTime currentEtDate = DateTime.MinValue;

        // Estado de breakouts por nivel (habilita retests)
        private bool brokeUpORH, brokeDownORL;
        private bool brokeUpPDH, brokeDownPDL;
        private bool brokeUpPMH, brokeDownPML;

        // IDs por día (mantener históricos visibles)
        private string HighId => $"OR15_H_SEG_{currentEtDate:yyyyMMdd}";
        private string LowId => $"OR15_L_SEG_{currentEtDate:yyyyMMdd}";
        private string MidId => $"OR15_M_SEG_{currentEtDate:yyyyMMdd}";
        private string PDHId => $"PDH_SEG_{currentEtDate:yyyyMMdd}";
        private string PDLId => $"PDL_SEG_{currentEtDate:yyyyMMdd}";
        private string PMHId => $"PMH_SEG_{currentEtDate:yyyyMMdd}";
        private string PMLId => $"PML_SEG_{currentEtDate:yyyyMMdd}";

        public override void Calculate(int index)
        {
            BuySignal[index] = double.NaN;
            SellSignal[index] = double.NaN;

            var utc = Bars.OpenTimes[index];
            var et = TimeUtils.UtcToEt(utc);

            // Nuevo día ET
            if (et.Date != currentEtDate)
            {
                currentEtDate = et.Date;

                rangeHigh = double.NaN;
                rangeLow = double.NaN;
                preMarketHigh = double.NaN;
                preMarketLow = double.NaN;
                prevSessHigh = double.NaN;
                prevSessLow = double.NaN;

                brokeUpORH = brokeDownORL = false;
                brokeUpPDH = brokeDownPDL = false;
                brokeUpPMH = brokeDownPML = false;

                if (UsePreviousDay)
                    ComputePreviousSessionHL();
            }

            var orStartEt = currentEtDate.AddHours(9).AddMinutes(30);
            var orEndEt = orStartEt.AddMinutes(OpeningRangeTF);
            var sessEndEt = currentEtDate.AddHours(16);
            var preMarketEt1 = currentEtDate.AddHours(4);
            var preMarketEt2 = orStartEt.AddMinutes(-1);

            var orStartUtc = TimeUtils.EtToUtc(orStartEt);
            var sessEndUtc = TimeUtils.EtToUtc(sessEndEt);

            // Construcción OR 9:30–9:45
            if (UseOpeningRange && et >= orStartEt && et < orEndEt)
            {
                var h = Bars.HighPrices[index];
                var l = Bars.LowPrices[index];
                rangeHigh = double.IsNaN(rangeHigh) ? h : Math.Max(rangeHigh, h);
                rangeLow = double.IsNaN(rangeLow) ? l : Math.Min(rangeLow, l);
            }

            // Construcción Premarket 4:00–9:29
            if (UsePremarket && et >= preMarketEt1 && et <= preMarketEt2)
            {
                var h = Bars.HighPrices[index];
                var l = Bars.LowPrices[index];
                preMarketHigh = double.IsNaN(preMarketHigh) ? h : Math.Max(preMarketHigh, h);
                preMarketLow = double.IsNaN(preMarketLow) ? l : Math.Min(preMarketLow, l);
            }

            // Dibujar líneas durante sesión
            if (et >= orStartEt && et <= sessEndEt)
                DrawSessionSegments(orStartUtc, sessEndUtc);

            // Actualizar estado de breakouts del bar actual
            if (et >= orStartEt && et <= sessEndEt)
                UpdateBreakoutState(index);

            // Señales en tiempo real sobre el último bar
            if (index == Bars.Count - 1 && et >= orStartEt && et <= sessEndEt)
            {
                if (Mode == SignalKind.Retest)
                    CheckAndMarkRetests(index);
                else
                    CheckAndMarkBreakouts(index);
            }
        }

        private void DrawSessionSegments(DateTime x1Utc, DateTime x2Utc)
        {
            if (UseOpeningRange && !double.IsNaN(rangeHigh))
                Chart.DrawTrendLine(HighId, x1Utc, rangeHigh, x2Utc, rangeHigh, Color.Gray, 1, LineStyle.Solid);
            if (UseOpeningRange && !double.IsNaN(rangeLow))
                Chart.DrawTrendLine(LowId, x1Utc, rangeLow, x2Utc, rangeLow, Color.Gray, 1, LineStyle.Solid);

            if (UseOpeningRange)
            {
                var mid = (!double.IsNaN(rangeHigh) && !double.IsNaN(rangeLow)) ? (rangeHigh + rangeLow) * 0.5 : double.NaN;
                if (!double.IsNaN(mid))
                    Chart.DrawTrendLine(MidId, x1Utc, mid, x2Utc, mid, Color.Gray, 1, LineStyle.Lines);
            }

            if (UsePreviousDay && !double.IsNaN(prevSessHigh))
                Chart.DrawTrendLine(PDHId, x1Utc, prevSessHigh, x2Utc, prevSessHigh, Color.Yellow, 1, LineStyle.Solid);
            if (UsePreviousDay && !double.IsNaN(prevSessLow))
                Chart.DrawTrendLine(PDLId, x1Utc, prevSessLow, x2Utc, prevSessLow, Color.Yellow, 1, LineStyle.Solid);

            if (UsePremarket && !double.IsNaN(preMarketHigh))
                Chart.DrawTrendLine(PMHId, x1Utc, preMarketHigh, x2Utc, preMarketHigh, Color.Blue, 1, LineStyle.Solid);
            if (UsePremarket && !double.IsNaN(preMarketLow))
                Chart.DrawTrendLine(PMLId, x1Utc, preMarketLow, x2Utc, preMarketLow, Color.Blue, 1, LineStyle.Solid);
        }

        private void UpdateBreakoutState(int index)
        {
            double open = Bars.OpenPrices[index];
            double close = Bars.ClosePrices[index];

            if (UseOpeningRange && !double.IsNaN(rangeHigh) && !double.IsNaN(rangeLow))
            {
                if (!brokeUpORH && open < rangeHigh && close >= rangeHigh && close > open) brokeUpORH = true;
                if (!brokeDownORL && open > rangeLow && close <= rangeLow && close < open) brokeDownORL = true;
            }

            if (UsePreviousDay && !double.IsNaN(prevSessHigh))
                if (!brokeUpPDH && open < prevSessHigh && close >= prevSessHigh && close > open) brokeUpPDH = true;

            if (UsePreviousDay && !double.IsNaN(prevSessLow))
                if (!brokeDownPDL && open > prevSessLow && close <= prevSessLow && close < open) brokeDownPDL = true;

            if (UsePremarket && !double.IsNaN(preMarketHigh))
                if (!brokeUpPMH && open < preMarketHigh && close >= preMarketHigh && close > open) brokeUpPMH = true;

            if (UsePremarket && !double.IsNaN(preMarketLow))
                if (!brokeDownPML && open > preMarketLow && close <= preMarketLow && close < open) brokeDownPML = true;
        }

        private bool IsBullPinBar(int index, double level)
        {
            double open = Bars.OpenPrices[index];
            double close = Bars.ClosePrices[index];
            double high = Bars.HighPrices[index];
            double low = Bars.LowPrices[index];
            double prevHigh = Bars.HighPrices[index - 1];

            double bodyTop = Math.Max(open, close);
            double bodyBot = Math.Min(open, close);
            double upperWick = Math.Max(0.0, high - bodyTop);
            double lowerWick = Math.Max(0.0, bodyBot - low);

            bool openAboveLevel = open > level;
            bool lowerWickTouches = low < level;
            bool isBullRejection = lowerWick >= 1.5 * upperWick;
            bool isGreen = close > open;
            bool prevHighOk = prevHigh > high;

            return openAboveLevel && lowerWickTouches && isBullRejection && isGreen && prevHighOk;
        }

        private bool IsBearPinBar(int index, double level)
        {
            double open = Bars.OpenPrices[index];
            double close = Bars.ClosePrices[index];
            double high = Bars.HighPrices[index];
            double low = Bars.LowPrices[index];
            double prevLow = Bars.LowPrices[index - 1];

            double bodyTop = Math.Max(open, close);
            double bodyBot = Math.Min(open, close);
            double upperWick = Math.Max(0.0, high - bodyTop);
            double lowerWick = Math.Max(0.0, bodyBot - low);

            bool openBelowLevel = open < level;
            bool upperWickTouches = high > level;
            bool isBearRejection = upperWick >= 1.5 * lowerWick;
            bool isRed = close < open;
            bool prevLowOk = prevLow < low;

            return openBelowLevel && upperWickTouches && isBearRejection && isRed && prevLowOk;
        }

        private void CheckAndMarkRetests(int index)
        {
            if (index < 1) return;

            if (UseOpeningRange && TryBullRetest(index, "ORH", rangeHigh, brokeUpORH)) return;
            if (UsePreviousDay && TryBullRetest(index, "PDH", prevSessHigh, brokeUpPDH)) return;
            if (UsePremarket && TryBullRetest(index, "PMH", preMarketHigh, brokeUpPMH)) return;

            if (UseOpeningRange && TryBearRetest(index, "ORL", rangeLow, brokeDownORL)) return;
            if (UsePreviousDay && TryBearRetest(index, "PDL", prevSessLow, brokeDownPDL)) return;
            if (UsePremarket && TryBearRetest(index, "PML", preMarketLow, brokeDownPML)) return;
        }

        private void CheckAndMarkBreakouts(int index)
        {
            double open = Bars.OpenPrices[index];
            double close = Bars.ClosePrices[index];

            bool isGreen = close > open;
            bool isRed = close < open;
            if (!isGreen && !isRed) return;

            if (UseOpeningRange && !double.IsNaN(rangeHigh) && isGreen && open < rangeHigh && close >= rangeHigh)
            {
                TryMarkBreakout(index, "ORH", true);
                return;
            }
            if (UseOpeningRange && !double.IsNaN(rangeLow) && isRed && open > rangeLow && close <= rangeLow)
            {
                TryMarkBreakout(index, "ORL", false);
                return;
            }

            if (UsePreviousDay && !double.IsNaN(prevSessHigh) && isGreen && open < prevSessHigh && close >= prevSessHigh)
            {
                TryMarkBreakout(index, "PDH", true);
                return;
            }
            if (UsePreviousDay && !double.IsNaN(prevSessLow) && isRed && open > prevSessLow && close <= prevSessLow)
            {
                TryMarkBreakout(index, "PDL", false);
                return;
            }

            if (UsePremarket && !double.IsNaN(preMarketHigh) && isGreen && open < preMarketHigh && close >= preMarketHigh)
            {
                TryMarkBreakout(index, "PMH", true);
                return;
            }
            if (UsePremarket && !double.IsNaN(preMarketLow) && isRed && open > preMarketLow && close <= preMarketLow)
            {
                TryMarkBreakout(index, "PML", false);
                return;
            }
        }

        private void TryMarkBreakout(int index, string tag, bool isUp)
        {
            string id = $"BK_{(isUp ? "UP" : "DN")}_{tag}_{index}";
            Chart.RemoveObject(id);

            double close = Bars.ClosePrices[index];
            Chart.DrawIcon(id, isUp ? ChartIconType.UpTriangle : ChartIconType.DownTriangle,
                           Bars.OpenTimes[index], close, isUp ? Color.LimeGreen : Color.Red);

            if (isUp)
                BuySignal[index] = 1.0;
            else
                SellSignal[index] = -1.0;

            FireAlert($"BK_{tag}", isUp ? "BUY" : "SELL", index, close);
        }

        private bool TryBullRetest(int index, string tag, double level, bool brokeUp)
        {
            string id = $"RT_UP_{tag}_{index}";
            Chart.RemoveObject(id);

            if (!brokeUp || double.IsNaN(level)) return false;

            bool pin = IsBullPinBar(index, level);
            if (pin)
            {
                Chart.DrawIcon(id, ChartIconType.UpTriangle, Bars.OpenTimes[index], Bars.LowPrices[index], Color.LimeGreen);
                BuySignal[index] = level;
                FireAlert($"RT_{tag}", "BUY", index, Bars.ClosePrices[index]);
                return true;
            }
            return false;
        }

        private bool TryBearRetest(int index, string tag, double level, bool brokeDown)
        {
            string id = $"RT_DN_{tag}_{index}";
            Chart.RemoveObject(id);

            if (!brokeDown || double.IsNaN(level)) return false;

            bool pin = IsBearPinBar(index, level);
            if (pin)
            {
                Chart.DrawIcon(id, ChartIconType.DownTriangle, Bars.OpenTimes[index], Bars.HighPrices[index], Color.Red);
                SellSignal[index] = level;
                FireAlert($"RT_{tag}", "SELL", index, Bars.ClosePrices[index]);
                return true;
            }
            return false;
        }

        private void ComputePreviousSessionHL()
        {
            prevSessHigh = double.NaN;
            prevSessLow = double.NaN;

            for (int back = 1; back <= 7; back++)
            {
                var d = currentEtDate.AddDays(-back);
                var startEt = d.AddHours(9).AddMinutes(30);
                var endEt = d.AddHours(16);

                double hi = double.NaN, lo = double.NaN;
                bool found = false;

                for (int i = 0; i < Bars.Count; i++)
                {
                    var et = TimeUtils.UtcToEt(Bars.OpenTimes[i]);
                    if (et >= startEt && et <= endEt)
                    {
                        var h = Bars.HighPrices[i];
                        var l = Bars.LowPrices[i];
                        hi = double.IsNaN(hi) ? h : Math.Max(hi, h);
                        lo = double.IsNaN(lo) ? l : Math.Min(lo, l);
                        found = true;
                    }
                }

                if (found)
                {
                    prevSessHigh = hi;
                    prevSessLow = lo;
                    break;
                }
            }
        }

        private void FireAlert(string context, string side, int index, double price)
        {
            if (!IsLastBar) return;

            string msg = $"{side} {context} @ {price:0.#####}  {SymbolName}  {TimeFrame}  {Bars.OpenTimes[index]:yyyy-MM-dd HH:mm}";

            if (AlertsSound)
                Notifications.PlaySound(SoundType.PositiveNotification);

            Print("[ALERTA] {0}", msg);
        }
    }
}
