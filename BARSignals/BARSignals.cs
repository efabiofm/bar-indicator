using cAlgo.API;
using System;
using System.Collections.Generic;

namespace cAlgo.Indicators
{
    [Indicator(IsOverlay = true, AccessRights = AccessRights.None)]
    public class BARSignals : Indicator
    {
        [Output("BuySignal", PlotType = PlotType.Points, LineColor = "LimeGreen")]
        public IndicatorDataSeries BuySignal { get; set; }

        [Output("SellSignal", PlotType = PlotType.Points, LineColor = "Red")]
        public IndicatorDataSeries SellSignal { get; set; }

        [Parameter("Alertas: Sonido", DefaultValue = true)]
        public bool AlertsSound { get; set; }

        // OR15
        private double rangeHigh = double.NaN;
        private double rangeLow  = double.NaN;

        // Previous Day session (regular hours)
        private double prevSessHigh = double.NaN;
        private double prevSessLow  = double.NaN;

        // Current day premarket
        private double preMarketHigh = double.NaN;
        private double preMarketLow  = double.NaN;

        private DateTime currentEtDate = DateTime.MinValue;

        // Estado de breakouts por nivel (habilita retests)
        private bool brokeUpORH,  brokeDownORL;
        private bool brokeUpPDH,  brokeDownPDL;
        private bool brokeUpPMH,  brokeDownPML;

        // IDs por día (mantener históricos visibles)
        private string HighId => $"OR15_H_SEG_{currentEtDate:yyyyMMdd}";
        private string LowId  => $"OR15_L_SEG_{currentEtDate:yyyyMMdd}";
        private string MidId  => $"OR15_M_SEG_{currentEtDate:yyyyMMdd}";
        private string PDHId  => $"PDH_SEG_{currentEtDate:yyyyMMdd}";
        private string PDLId  => $"PDL_SEG_{currentEtDate:yyyyMMdd}";
        private string PMHId  => $"PMH_SEG_{currentEtDate:yyyyMMdd}";
        private string PMLId  => $"PML_SEG_{currentEtDate:yyyyMMdd}";

        // Evitar duplicados intra-bar
        private readonly HashSet<string> _marked = new HashSet<string>();

        public override void Calculate(int index)
        {
            BuySignal[index]  = double.NaN;
            SellSignal[index] = double.NaN;

            var utc = Bars.OpenTimes[index];
            var et  = UtcToEt(utc);

            // Nuevo día ET
            if (et.Date != currentEtDate)
            {
                currentEtDate = et.Date;

                rangeHigh = double.NaN;
                rangeLow  = double.NaN;
                preMarketHigh = double.NaN;
                preMarketLow  = double.NaN;

                brokeUpORH = brokeDownORL = false;
                brokeUpPDH = brokeDownPDL = false;
                brokeUpPMH = brokeDownPML = false;

                ComputePreviousSessionHL();
            }

            var orStartEt    = currentEtDate.AddHours(9).AddMinutes(30);
            var orEndEt      = orStartEt.AddMinutes(15);
            var sessEndEt    = currentEtDate.AddHours(16);
            var preMarketEt1 = currentEtDate.AddHours(4);
            var preMarketEt2 = orStartEt.AddMinutes(-1);

            var orStartUtc = EtToUtc(orStartEt);
            var sessEndUtc = EtToUtc(sessEndEt);

            // Construcción OR 9:30–9:45
            if (et >= orStartEt && et < orEndEt)
            {
                var h = Bars.HighPrices[index];
                var l = Bars.LowPrices[index];
                rangeHigh = double.IsNaN(rangeHigh) ? h : Math.Max(rangeHigh, h);
                rangeLow  = double.IsNaN(rangeLow)  ? l : Math.Min(rangeLow,  l);
            }

            // Construcción Premarket 4:00–9:29
            if (et >= preMarketEt1 && et <= preMarketEt2)
            {
                var h = Bars.HighPrices[index];
                var l = Bars.LowPrices[index];
                preMarketHigh = double.IsNaN(preMarketHigh) ? h : Math.Max(preMarketHigh, h);
                preMarketLow  = double.IsNaN(preMarketLow)  ? l : Math.Min(preMarketLow,  l);
            }

            // Dibujar líneas durante sesión
            if (et >= orStartEt && et <= sessEndEt)
                DrawSessionSegments(orStartUtc, sessEndUtc);

            // Actualizar estado de breakouts del bar actual
            if (et >= orStartEt && et <= sessEndEt)
                UpdateBreakoutState(index);

            // Marcar SOLO retests en tiempo real sobre el último bar
            if (index == Bars.Count - 1 && et >= orStartEt && et <= sessEndEt)
                CheckAndMarkRetests(index);
        }

        private void DrawSessionSegments(DateTime x1Utc, DateTime x2Utc)
        {
            // OR15 gris
            if (!double.IsNaN(rangeHigh))
                Chart.DrawTrendLine(HighId, x1Utc, rangeHigh, x2Utc, rangeHigh, Color.Gray, 1, LineStyle.Solid);
            if (!double.IsNaN(rangeLow))
                Chart.DrawTrendLine(LowId,  x1Utc, rangeLow,  x2Utc, rangeLow,  Color.Gray, 1, LineStyle.Solid);

            // Mid en LINEAS (rayas)
            var mid = (!double.IsNaN(rangeHigh) && !double.IsNaN(rangeLow)) ? (rangeHigh + rangeLow) * 0.5 : double.NaN;
            if (!double.IsNaN(mid))
                Chart.DrawTrendLine(MidId, x1Utc, mid, x2Utc, mid, Color.Gray, 1, LineStyle.Lines);

            // PDH/PDL en AMARILLO
            if (!double.IsNaN(prevSessHigh))
                Chart.DrawTrendLine(PDHId, x1Utc, prevSessHigh, x2Utc, prevSessHigh, Color.Yellow, 1, LineStyle.Solid);
            if (!double.IsNaN(prevSessLow))
                Chart.DrawTrendLine(PDLId, x1Utc, prevSessLow,  x2Utc, prevSessLow,  Color.Yellow, 1, LineStyle.Solid);

            // PMH/PML azul
            if (!double.IsNaN(preMarketHigh))
                Chart.DrawTrendLine(PMHId, x1Utc, preMarketHigh, x2Utc, preMarketHigh, Color.Blue, 1, LineStyle.Solid);
            if (!double.IsNaN(preMarketLow))
                Chart.DrawTrendLine(PMLId, x1Utc, preMarketLow,  x2Utc, preMarketLow,  Color.Blue, 1, LineStyle.Solid);
        }

        // Marca estado de breakout por nivel usando CUERPO
        private void UpdateBreakoutState(int index)
        {
            double open = Bars.OpenPrices[index];
            double close = Bars.ClosePrices[index];

            // OR cuando existe
            if (!double.IsNaN(rangeHigh) && !double.IsNaN(rangeLow))
            {
                if (!brokeUpORH   && open < rangeHigh && close >= rangeHigh && close > open)  brokeUpORH  = true;
                if (!brokeDownORL && open > rangeLow  && close <= rangeLow  && close < open) brokeDownORL = true;
            }

            if (!double.IsNaN(prevSessHigh))
                if (!brokeUpPDH   && open < prevSessHigh && close >= prevSessHigh && close > open)  brokeUpPDH = true;

            if (!double.IsNaN(prevSessLow))
                if (!brokeDownPDL && open > prevSessLow  && close <= prevSessLow  && close < open)  brokeDownPDL = true;

            if (!double.IsNaN(preMarketHigh))
                if (!brokeUpPMH   && open < preMarketHigh && close >= preMarketHigh && close > open) brokeUpPMH = true;

            if (!double.IsNaN(preMarketLow))
                if (!brokeDownPML && open > preMarketLow  && close <= preMarketLow  && close < open)  brokeDownPML = true;
        }

        // Un triángulo por vela. Prioridad OR > PD > PM.
        private void CheckAndMarkRetests(int index)
        {
            if (index < 1) return;

            double open  = Bars.OpenPrices[index];
            double close = Bars.ClosePrices[index];
            double high  = Bars.HighPrices[index];
            double low   = Bars.LowPrices[index];

            double prevHigh = Bars.HighPrices[index - 1];
            double prevLow  = Bars.LowPrices[index - 1];

            // Color de vela
            bool isGreen = close > open;
            bool isRed   = close < open;
            if (!isGreen && !isRed) return;

            // Geometría de mechas
            double bodyTop = Math.Max(open, close);
            double bodyBot = Math.Min(open, close);
            double upperWick = Math.Max(0.0, high - bodyTop);
            double lowerWick = Math.Max(0.0, bodyBot - low);

            // Rechazo
            bool isBullRejection = lowerWick >= 2.0 * upperWick;
            bool isBearRejection = upperWick >= 2.0 * lowerWick;

            // Largos: tras breakout UP, retest con mecha tocando nivel, cuerpo encima,
            // prevHigh DEBE ser > high actual, vela verde y de rechazo.
            if (TryBullRetest(index, "ORH", rangeHigh,   brokeUpORH,   low, bodyBot, bodyTop, prevHigh, isBullRejection, isGreen)) return;
            if (TryBullRetest(index, "PDH", prevSessHigh, brokeUpPDH,   low, bodyBot, bodyTop, prevHigh, isBullRejection, isGreen)) return;
            if (TryBullRetest(index, "PMH", preMarketHigh, brokeUpPMH,  low, bodyBot, bodyTop, prevHigh, isBullRejection, isGreen)) return;

            // Cortos: tras breakout DOWN, retest con mecha tocando nivel, cuerpo debajo,
            // prevLow DEBE ser < low actual, vela roja y de rechazo.
            if (TryBearRetest(index, "ORL", rangeLow,    brokeDownORL,  high, bodyBot, bodyTop, prevLow, isBearRejection, isRed)) return;
            if (TryBearRetest(index, "PDL", prevSessLow,  brokeDownPDL,  high, bodyBot, bodyTop, prevLow, isBearRejection, isRed)) return;
            if (TryBearRetest(index, "PML", preMarketLow, brokeDownPML,  high, bodyBot, bodyTop, prevLow, isBearRejection, isRed)) return;
        }

        // Retest alcista
        private bool TryBullRetest(int index, string tag, double level, bool brokeUp,
                                   double curLow, double bodyBot, double bodyTop,
                                   double prevHigh, bool isRejection, bool isGreen)
        {
            if (!brokeUp || double.IsNaN(level) || !isGreen || !isRejection) return false;

            bool wickTouches = curLow <= level && bodyBot >= level; // mecha toca y cuerpo por encima
            bool prevHighOk  = prevHigh > Bars.HighPrices[index];   // máximo previo DEBE ser mayor al actual

            if (wickTouches && prevHighOk)
            {
                string id = $"RT_UP_{tag}_{Bars.OpenTimes[index]:yyyyMMddHHmm}_{index}";
                if (_marked.Add(id))
                {
                    double offset = Symbol.PipSize * 3;
                    Chart.DrawIcon(id, ChartIconType.UpTriangle, Bars.OpenTimes[index], curLow - offset, Color.LimeGreen);
                }
                BuySignal[index] = 1.0;
                FireAlert($"RT_{tag}", "BUY", index, Bars.ClosePrices[index]);
                return true;
            }
            return false;
        }

        // Retest bajista
        private bool TryBearRetest(int index, string tag, double level, bool brokeDown,
                                   double curHigh, double bodyBot, double bodyTop,
                                   double prevLow, bool isRejection, bool isRed)
        {
            if (!brokeDown || double.IsNaN(level) || !isRed || !isRejection) return false;

            bool wickTouches = curHigh >= level && bodyTop <= level; // mecha toca y cuerpo por debajo
            bool prevLowOk   = prevLow < Bars.LowPrices[index];      // mínimo previo DEBE ser menor al actual

            if (wickTouches && prevLowOk)
            {
                string id = $"RT_DN_{tag}_{Bars.OpenTimes[index]:yyyyMMddHHmm}_{index}";
                if (_marked.Add(id))
                {
                    double offset = Symbol.PipSize * 3;
                    Chart.DrawIcon(id, ChartIconType.DownTriangle, Bars.OpenTimes[index], curHigh + offset, Color.Red);
                }
                SellSignal[index] = -1.0;
                FireAlert($"RT_{tag}", "SELL", index, Bars.ClosePrices[index]);
                return true;
            }
            return false;
        }

        private void ComputePreviousSessionHL()
        {
            prevSessHigh = double.NaN;
            prevSessLow  = double.NaN;

            // Busca hasta 7 días hacia atrás para saltar fines de semana/feriados
            for (int back = 1; back <= 7; back++)
            {
                var d = currentEtDate.AddDays(-back);
                var startEt = d.AddHours(9).AddMinutes(30);
                var endEt   = d.AddHours(16);

                double hi = double.NaN, lo = double.NaN;
                bool found = false;

                for (int i = 0; i < Bars.Count; i++)
                {
                    var et = UtcToEt(Bars.OpenTimes[i]);
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
                    prevSessLow  = lo;
                    break;
                }
            }
        }

        // ---- Conversión UTC <-> ET con DST EE. UU. ----
        private DateTime UtcToEt(DateTime utc)
        {
            return IsEtDstByLocalDate(utc.AddHours(-5)) ? utc.AddHours(-4) : utc.AddHours(-5);
        }

        private DateTime EtToUtc(DateTime etLocal)
        {
            int offset = IsEtDstByLocalDate(etLocal) ? -4 : -5;
            return etLocal.AddHours(-offset);
        }

        private bool IsEtDstByLocalDate(DateTime etLocal)
        {
            int y = etLocal.Year;
            var start = NthWeekdayOfMonth(y, 3, DayOfWeek.Sunday, 2).AddHours(2);
            var end   = NthWeekdayOfMonth(y, 11, DayOfWeek.Sunday, 1).AddHours(2);
            return etLocal >= start && etLocal < end;
        }

        private DateTime NthWeekdayOfMonth(int year, int month, DayOfWeek dow, int n)
        {
            var first = new DateTime(year, month, 1);
            int offset = ((int)dow - (int)first.DayOfWeek + 7) % 7;
            int day = 1 + offset + (n - 1) * 7;
            return new DateTime(year, month, day);
        }

        private void FireAlert(string context, string side, int index, double price)
        {
            // Ejecutar solo en tiempo real (no backtest)
            if (!IsLastBar) return;

            string msg = $"{side} {context} @ {price:0.#####}  {SymbolName}  {TimeFrame}  {Bars.OpenTimes[index]:yyyy-MM-dd HH:mm}";

            if (AlertsSound)
                Notifications.PlaySound(SoundType.PositiveNotification); // o Warning/Alert, etc.

            Print("[ALERTA] {0}", msg);
        }
    }
}
