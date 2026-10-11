using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Indicators
{
    // ════════════════════════════════════════════════════════════════════
    //
    //   ██████╗  ██████╗ ██████╗     ██╗███╗   ██╗██████╗ ██╗ ██████╗ █████╗ ████████╗ ██████╗ ██████╗
    //  ██╔════╝ ██╔═══██╗██╔══██╗    ██║████╗  ██║██╔══██╗██║██╔════╝██╔══██╗╚══██╔══╝██╔═══██╗██╔══██╗
    //  ██║  ███╗██║   ██║██║  ██║    ██║██╔██╗ ██║██║  ██║██║██║     ███████║   ██║   ██║   ██║██████╔╝
    //  ██║   ██║██║   ██║██║  ██║    ██║██║╚██╗██║██║  ██║██║██║     ██╔══██║   ██║   ██║   ██║██╔══██╗
    //  ╚██████╔╝╚██████╔╝██████╔╝    ██║██║ ╚████║██████╔╝██║╚██████╗██║  ██║   ██║   ╚██████╔╝██║  ██║
    //   ╚═════╝  ╚═════╝ ╚═════╝     ╚═╝╚═╝  ╚═══╝╚═════╝ ╚═╝ ╚═════╝╚═╝  ╚═╝   ╚═╝    ╚═════╝ ╚═╝  ╚═╝
    //
    //   God Indicator v2.3 — Auto-Detection Engine
    //   Original Pine Script : © cyatophilum & Prem Kumar (MD)
    //   Converted to cAlgo   : 2026-07-01
    //
    //   Features:
    //     ✅  EMA 44 & 222  — dynamic color (yellow/blue, gold/steel)
    //     ✅  HTF Narrow Range consolidation detection  (SMA + Wilder RMA)
    //     ✅  HMA direction filter  (WMA-based Hull MA)
    //     ✅  Long / Short breakout arrows on chart
    //     ✅  Consolidation candle body highlighting  (yellow, opacity scales with cpt)
    //     ✅  Info table  (top-right, live status)
    //     ✅  Telegram alert  (instant phone message)
    //     ✅  Email alert     (cTrader Notifications)
    //     ✅  cTrader push    (mobile app notification)
    //
    // ════════════════════════════════════════════════════════════════════

    [Indicator(IsOverlay = true, AccessRights = AccessRights.FullAccess)]
    public class GodIndicatorMD : Indicator
    {
        // ────────────────────────────────────────────────────────────────
        //  P A R A M E T E R S
        // ────────────────────────────────────────────────────────────────

        [Parameter("EMA 44 Length", Group = "📈 Moving Averages", DefaultValue = 44, MinValue = 1)]
        public int Len44 { get; set; }

        [Parameter("EMA 222 Length", Group = "📈 Moving Averages", DefaultValue = 222, MinValue = 1)]
        public int Len222 { get; set; }

        [Parameter("Show EMAs", Group = "📈 Moving Averages", DefaultValue = true)]
        public bool ShowEMAs { get; set; }

        // ─────────────────────────────────────────────────────────────────
        [Parameter("Range / HMA Length", Group = "⚙️ Core Settings", DefaultValue = 14, MinValue = 5)]
        public int RangeLen { get; set; }

        // ─────────────────────────────────────────────────────────────────
        [Parameter("Use Manual Settings", Group = "🔧 Manual Override", DefaultValue = false)]
        public bool UseManual { get; set; }

        [Parameter("Manual HTF (minutes: 30 or 60)", Group = "🔧 Manual Override", DefaultValue = 60, MinValue = 1)]
        public int ManualTFMinutes { get; set; }

        [Parameter("Manual Smoothing", Group = "🔧 Manual Override", DefaultValue = 4, MinValue = 1, MaxValue = 20)]
        public int ManualSmooth { get; set; }

        [Parameter("Manual NR Threshold %", Group = "🔧 Manual Override", DefaultValue = 80.0, MinValue = 50.0, MaxValue = 100.0, Step = 0.5)]
        public double ManualNR { get; set; }

        [Parameter("Manual Consecutive Bars", Group = "🔧 Manual Override", DefaultValue = 3, MinValue = 1, MaxValue = 20)]
        public int ManualConsec { get; set; }

        // ─────────────────────────────────────────────────────────────────
        //  Telegram setup (one-time):
        //  1. Open Telegram → @BotFather → /newbot → copy Token
        //  2. Send any message to your bot
        //  3. Visit https://api.telegram.org/bot{TOKEN}/getUpdates → copy chat_id
        //  4. Paste both below — done!
        [Parameter("Enable Telegram Alerts", Group = "🔔 Notifications", DefaultValue = false)]
        public bool EnableTelegram { get; set; }

        [Parameter("Telegram Bot Token", Group = "🔔 Notifications", DefaultValue = "")]
        public string TelegramToken { get; set; }

        [Parameter("Telegram Chat ID", Group = "🔔 Notifications", DefaultValue = "")]
        public string TelegramChatId { get; set; }

        [Parameter("Enable Email Alerts", Group = "🔔 Notifications", DefaultValue = false)]
        public bool EnableEmail { get; set; }

        [Parameter("Alert Email Address", Group = "🔔 Notifications", DefaultValue = "")]
        public string AlertEmail { get; set; }

        [Parameter("Enable cTrader Push Alert", Group = "🔔 Notifications", DefaultValue = true)]
        public bool EnablePush { get; set; }

        // ────────────────────────────────────────────────────────────────
        //  O U T P U T S
        //  Two series per line = dynamic color switch (yellow/blue, gold/steel)
        // ────────────────────────────────────────────────────────────────

        [Output("EMA 44  — Above (Yellow)", LineColor = "FFF200", Thickness = 2, PlotType = PlotType.Line)]
        public IndicatorDataSeries EMA44Above { get; set; }

        [Output("EMA 44  — Below (Blue)", LineColor = "2196F3", Thickness = 2, PlotType = PlotType.Line)]
        public IndicatorDataSeries EMA44Below { get; set; }

        [Output("EMA 222 — Above (Gold)", LineColor = "FFC800", Thickness = 3, PlotType = PlotType.Line)]
        public IndicatorDataSeries EMA222Above { get; set; }

        [Output("EMA 222 — Below (Steel Blue)", LineColor = "6495ED", Thickness = 3, PlotType = PlotType.Line)]
        public IndicatorDataSeries EMA222Below { get; set; }

        // Direction line drawn as chart objects (DrawTrendLine) to guarantee
        // breaks at non-consolidation bars — PlotType.Line connects across NaN.

        // ────────────────────────────────────────────────────────────────
        //  P R I V A T E   F I E L D S
        // ────────────────────────────────────────────────────────────────

        private TimeFrame _activeTF;
        private int       _activeSmooth;
        private double    _activeNR;
        private int       _activeConsec;

        private Bars     _htfBars;

        private double[] _htfPriceRange;   // SMA(H-L, smooth) per HTF bar
        private double[] _htfAvgRange;     // RMA(priceRange, rangeLen) per HTF bar
        private bool[]   _htfNarrowRange;  // priceRange < NR% * avgRange
        private int[]    _htfCpt;          // consecutive narrow-range counter
        private bool[]   _htfBreakout;     // cpt[i-1] >= consec AND NOT NR[i]

        private int  _lastHTFCount;
        private bool _htfReady;

        // HMA cache — avoids calling the expensive HMA() twice per bar
        private IndicatorDataSeries _hmaBuffer;

        private ExponentialMovingAverage _ema44Ind;
        private ExponentialMovingAverage _ema222Ind;

        // Alert dedup state
        private bool _prevConsolidation;
        private bool _prevLong;
        private bool _prevShort;

        // ── Consolidation Zone Box tracking ──
        private DateTime _boxStart    = DateTime.MinValue;  // first bar of current NR cluster
        private double   _boxHigh     = double.MinValue;    // max HIGH seen in current cluster
        private double   _boxLow      = double.MaxValue;    // min LOW  seen in current cluster
        private int      _boxBarCount = 0;                  // how many NR bars in current cluster
        private int      _boxId       = 0;                  // unique ID per cluster
        private bool     _prevNR      = false;              // NR state of previous bar

        // ════════════════════════════════════════════════════════════════
        //  I N I T I A L I Z E
        // ════════════════════════════════════════════════════════════════
        protected override void Initialize()
        {
            ApplySettings();

            _htfBars    = MarketData.GetBars(_activeTF);
            _ema44Ind   = Indicators.ExponentialMovingAverage(Bars.ClosePrices, Len44);
            _ema222Ind  = Indicators.ExponentialMovingAverage(Bars.ClosePrices, Len222);
            _hmaBuffer  = CreateDataSeries();   // stores HMA per chart bar

            Print($"🌟  God Indicator v2.3 | {Symbol.Name} | HTF: {HTFLabel()} | " +
                  $"Smooth: {_activeSmooth} | NR%: {_activeNR} | " +
                  $"Consec: {_activeConsec} | Mode: {(UseManual ? "MANUAL" : "AUTO")}");
        }

        // ────────────────────────────────────────────────────────────────
        //  A U T O - D E T E C T I O N   (exact mirror of Pine Script)
        // ────────────────────────────────────────────────────────────────
        private void ApplySettings()
        {
            if (UseManual)
            {
                _activeTF     = TFFromMinutes(ManualTFMinutes);
                _activeSmooth = ManualSmooth;
                _activeNR     = ManualNR;
                _activeConsec = ManualConsec;
                return;
            }

            string t = Symbol.Name.ToUpper();

            if      (t.Contains("XAUUSD") || t.Contains("GOLD"))    SetCfg(TimeFrame.Hour,     5, 75.0, 3);
            else if (t.Contains("GBPUSD"))                           SetCfg(TimeFrame.Minute30, 6, 72.0, 3);
            else if (t.Contains("USDCAD"))                           SetCfg(TimeFrame.Hour,     4, 78.0, 3);
            else if (t.Contains("XAGUSD") || t.Contains("SILVER"))   SetCfg(TimeFrame.Minute30, 6, 70.0, 3);
            else if (t.Contains("EURUSD"))                           SetCfg(TimeFrame.Hour,     4, 78.0, 4);
            else if (t.Contains("GBPJPY"))                           SetCfg(TimeFrame.Minute30, 6, 70.0, 3);
            else if (t.Contains("AUDUSD"))                           SetCfg(TimeFrame.Hour,     5, 75.0, 3);
            else if (t.Contains("USDJPY"))                           SetCfg(TimeFrame.Hour,     5, 75.0, 3);
            else if (t.Contains("USDCHF"))                           SetCfg(TimeFrame.Hour,     4, 80.0, 4);
            else if (t.Contains("EURJPY"))                           SetCfg(TimeFrame.Minute30, 6, 72.0, 3);
            else if (t.Contains("BTCUSD") || t.Contains("BTCUSDT")) SetCfg(TimeFrame.Hour,     6, 70.0, 4);
            else if (t.Contains("NZDUSD"))                           SetCfg(TimeFrame.Hour,     5, 75.0, 4);
            else if (t.Contains("EURGBP"))                           SetCfg(TimeFrame.Hour,     4, 82.0, 5);
            else                                                      SetCfg(TimeFrame.Hour,     4, 80.0, 3);
        }

        private void SetCfg(TimeFrame tf, int smooth, double nr, int consec)
        {
            _activeTF     = tf;
            _activeSmooth = smooth;
            _activeNR     = nr;
            _activeConsec = consec;
        }

        private TimeFrame TFFromMinutes(int m)
        {
            if (m <=  1)  return TimeFrame.Minute;
            if (m <=  2)  return TimeFrame.Minute2;
            if (m <=  3)  return TimeFrame.Minute3;
            if (m <=  5)  return TimeFrame.Minute5;
            if (m <= 10)  return TimeFrame.Minute10;
            if (m <= 15)  return TimeFrame.Minute15;
            if (m <= 30)  return TimeFrame.Minute30;
            if (m <= 45)  return TimeFrame.Minute45;
            if (m <= 60)  return TimeFrame.Hour;
            if (m <= 120) return TimeFrame.Hour2;
            if (m <= 240) return TimeFrame.Hour4;
            if (m <= 480) return TimeFrame.Hour8;
            return TimeFrame.Daily;
        }

        private string HTFLabel()
        {
            if (_activeTF == TimeFrame.Hour)     return "1H";
            if (_activeTF == TimeFrame.Minute30) return "30m";
            if (_activeTF == TimeFrame.Minute15) return "15m";
            if (_activeTF == TimeFrame.Hour4)    return "4H";
            return _activeTF.ToString();
        }

        // ════════════════════════════════════════════════════════════════
        //  H T F   P R E - C O M P U T A T I O N
        //  Rebuilds arrays only when _htfBars.Count changes (new HTF bar).
        // ════════════════════════════════════════════════════════════════
        private void ComputeHTFData()
        {
            int n = _htfBars.Count;
            if (n < Math.Max(RangeLen, _activeSmooth) + 4) return;

            _htfPriceRange  = new double[n];
            _htfAvgRange    = new double[n];
            _htfNarrowRange = new bool[n];
            _htfCpt         = new int[n];
            _htfBreakout    = new bool[n];

            // ── priceRange = SMA(High−Low, smooth) ──────────────────────
            // ── avgRange   = RMA(priceRange, rangeLen)  [Wilder's MA] ────
            for (int i = 0; i < n; i++)
            {
                double smaSum = 0;
                int    cnt    = 0;
                for (int j = i; j >= Math.Max(0, i - _activeSmooth + 1); j--)
                {
                    smaSum += _htfBars[j].High - _htfBars[j].Low;
                    cnt++;
                }
                _htfPriceRange[i] = cnt > 0 ? smaSum / cnt : 0;

                // RMA formula:  new = (prev*(n-1) + curr) / n
                _htfAvgRange[i] = i == 0
                    ? _htfPriceRange[i]
                    : (_htfAvgRange[i - 1] * (RangeLen - 1) + _htfPriceRange[i]) / RangeLen;

                _htfNarrowRange[i] = _htfAvgRange[i] > 0 &&
                                     _htfPriceRange[i] < _activeNR * _htfAvgRange[i] / 100.0;
            }

            // ── cpt = consecutive narrow-range counter per HTF bar ───────
            _htfCpt[0] = _htfNarrowRange[0] ? 1 : 0;
            for (int i = 1; i < n; i++)
                _htfCpt[i] = _htfNarrowRange[i] ? _htfCpt[i - 1] + 1 : 0;

            // ── breakout[i] = cpt[i-1] >= consec  AND  NOT NR[i] ────────
            for (int i = 1; i < n; i++)
                _htfBreakout[i] = _htfCpt[i - 1] >= _activeConsec && !_htfNarrowRange[i];

            _lastHTFCount = n;
            _htfReady     = true;
        }

        // ════════════════════════════════════════════════════════════════
        //  H E L P E R S
        // ════════════════════════════════════════════════════════════════

        /// <summary>Binary search: HTF bar whose OpenTime is <= chartBar OpenTime.</summary>
        private int FindHTFIndex(int chartIndex)
        {
            if (!_htfReady || _htfBars.Count == 0) return 0;
            DateTime t  = Bars[chartIndex].OpenTime;
            int lo = 0, hi = _htfBars.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_htfBars[mid].OpenTime <= t) lo = mid;
                else                             hi = mid - 1;
            }
            return lo;
        }

        /// <summary>Weighted Moving Average on Bars.ClosePrices.</summary>
        private double WMA(int index, int period)
        {
            if (index < period - 1) return double.NaN;
            double sum = 0, wSum = 0;
            for (int i = 0; i < period; i++)
            {
                double w = period - i;
                sum  += Bars.ClosePrices[index - i] * w;
                wSum += w;
            }
            return wSum > 0 ? sum / wSum : double.NaN;
        }

        /// <summary>Hull MA = WMA(2×WMA(n/2) − WMA(n), √n).</summary>
        private double HMA(int index, int period)
        {
            int halfP = Math.Max(1, period / 2);
            int sqrtP = Math.Max(1, (int)Math.Floor(Math.Sqrt(period)));
            if (index < period + sqrtP) return double.NaN;

            double wSum = 0, wTotal = 0;
            for (int i = 0; i < sqrtP; i++)
            {
                int    idx     = index - i;
                double wmaHalf = WMA(idx, halfP);
                double wmaFull = WMA(idx, period);
                if (double.IsNaN(wmaHalf) || double.IsNaN(wmaFull)) continue;
                double weight = sqrtP - i;
                wSum   += (2.0 * wmaHalf - wmaFull) * weight;
                wTotal += weight;
            }
            return wTotal > 0 ? wSum / wTotal : double.NaN;
        }

        // ════════════════════════════════════════════════════════════════
        //  C A L C U L A T E   (called once per chart bar)
        // ════════════════════════════════════════════════════════════════
        public override void Calculate(int index)
        {
            // Rebuild HTF arrays only when new HTF bars arrive
            if (!_htfReady || _htfBars.Count != _lastHTFCount)
                ComputeHTFData();

            if (!_htfReady) return;

            double close = Bars.ClosePrices[index];
            double high  = Bars.HighPrices[index];
            double low   = Bars.LowPrices[index];
            double open  = Bars.OpenPrices[index];

            // ── EMA 44 and EMA 222 ────────────────────────────────────
            double e44  = _ema44Ind.Result[index];
            double e222 = _ema222Ind.Result[index];

            if (ShowEMAs && !double.IsNaN(e44) && !double.IsNaN(e222))
            {
                EMA44Above[index]  = close > e44  ? e44  : double.NaN;   // Yellow above
                EMA44Below[index]  = close <= e44 ? e44  : double.NaN;   // Blue below
                EMA222Above[index] = close > e222 ? e222 : double.NaN;   // Gold above
                EMA222Below[index] = close <= e222 ? e222 : double.NaN;  // Steel blue below
            }
            else
            {
                EMA44Above[index] = EMA44Below[index]   = double.NaN;
                EMA222Above[index] = EMA222Below[index] = double.NaN;
            }

            // ── HTF narrow-range lookup ───────────────────────────────
            int  htfIdx      = FindHTFIndex(index);
            bool narrowRange = _htfNarrowRange[htfIdx];
            bool breakout    = _htfBreakout[htfIdx];
            int  cpt         = _htfCpt[htfIdx];

            // Breakout arrow fires ONLY on the first chart bar of the new HTF period
            bool isFirstOfHTF = index == 0 || FindHTFIndex(index - 1) != htfIdx;

            // ── HMA direction filter ──────────────────────────────────
            double hma = HMA(index, RangeLen);
            _hmaBuffer[index] = hma;   // cache so we never call HMA() twice

            bool isBullish = false;
            bool isBearish = false;

            if (!double.IsNaN(hma) && index >= 2)
            {
                double hmaPrev2 = _hmaBuffer[index - 2];  // read from cache — free!
                if (!double.IsNaN(hmaPrev2))
                {
                    isBullish = hma > hmaPrev2;  // ta.rising(hma, 2)
                    isBearish = hma < hmaPrev2;  // ta.falling(hma, 2)
                }
            }

            // ── Signals ───────────────────────────────────────────────
            bool longSignal  = breakout && isBullish && isFirstOfHTF;
            bool shortSignal = breakout && isBearish && isFirstOfHTF;

            // ── Direction snake — DrawTrendLine segments (guaranteed break at non-NR bars)
            // ── PlotType.Line connects across NaN, so we use chart objects instead ──────
            // ── Red = HMA falling | Blue = HMA rising | invisible outside consolidation ─
            string snakeKey = "dsnk_" + index;
            if (narrowRange && !double.IsNaN(hma) && index > 0)
            {
                bool   prevNarrow = _htfNarrowRange[FindHTFIndex(index - 1)];
                double prevHma    = _hmaBuffer[index - 1];

                if (prevNarrow && !double.IsNaN(prevHma))
                {
                    // Color based on HMA direction; gray when flat
                    Color snakeCol = isBullish ? Color.FromArgb(255, 33, 150, 243)   // Blue
                                  : isBearish ? Color.FromArgb(255, 244, 67,  54)    // Red
                                  :             Color.FromArgb(200, 128, 128, 128);  // Gray (flat)

                    DateTime t1   = Bars[index - 1].OpenTime;
                    DateTime t2   = Bars[index].OpenTime;
                    TimeSpan span = t2 - t1;
                    DateTime tMid1 = t1 + TimeSpan.FromTicks(span.Ticks / 2);
                    DateTime tMid2 = t2 + TimeSpan.FromTicks(span.Ticks / 2);

                    Chart.DrawTrendLine(snakeKey, tMid1, prevHma, tMid2, hma,
                                        snakeCol, 3, LineStyle.Solid);
                }
                else
                {
                    Chart.RemoveObject(snakeKey);   // previous bar was NOT NR — break here
                }
            }
            else
            {
                Chart.RemoveObject(snakeKey);       // current bar NOT NR — remove segment
            }

            // ══════════════════════════════════════════════════════════════
            // ── Consolidation Zone Box ────────────────────────────────────
            // ── Draws a rectangle (top wick → bottom wick) around each    ──
            // ── confirmed NR cluster.  Box is permanent — stays as zone.  ──
            // ══════════════════════════════════════════════════════════════
            bool nrJustStarted = narrowRange && !_prevNR;
            bool nrJustEnded   = !narrowRange && _prevNR;

            if (nrJustStarted)
            {
                // ── Brand-new consolidation cluster ──
                _boxId++;
                _boxStart    = Bars[index].OpenTime;
                _boxHigh     = high;
                _boxLow      = low;
                _boxBarCount = 1;
            }
            else if (narrowRange)
            {
                // ── Still inside same cluster — expand boundaries ──
                _boxHigh     = Math.Max(_boxHigh, high);
                _boxLow      = Math.Min(_boxLow,  low);
                _boxBarCount++;

                // Update right edge of box progressively (so it tracks live bars)
                TimeSpan barSpan = index > 0
                    ? Bars[index].OpenTime - Bars[index - 1].OpenTime
                    : TimeSpan.FromMinutes(15);
                DateTime boxRight = Bars[index].OpenTime + barSpan;

                string liveKey = "czone_" + _boxId;
                var liveBox = Chart.DrawRectangle(liveKey, _boxStart, _boxHigh, boxRight, _boxLow,
                                                  Color.FromArgb(180, 255, 255, 255));
                liveBox.IsFilled  = false;
                liveBox.Thickness = 1;
            }

            if (nrJustEnded)
            {
                // ── Cluster finished — keep box if confirmed, remove if too short ──
                if (_boxBarCount < _activeConsec)
                    Chart.RemoveObject("czone_" + _boxId);  // unconfirmed cluster — remove
                // else: confirmed box stays permanently on the chart
            }

            _prevNR = narrowRange;   // remember for next bar's transition detection



            // ── Consolidation candle highlighting (TradingView-style yellow candles) ──
            if (narrowRange)
            {
                // Pine Script formula: transp = 100 - cpt * 100 / consec
                // →  alpha = round(255 * cpt / consec), minimum 25 so first bar is just visible
                int bodyAlpha = cpt <= 0 ? 25
                              : (int)(255.0 * Math.Min(1.0, (double)cpt / Math.Max(1, _activeConsec)));
                bodyAlpha = Math.Max(bodyAlpha, 25);

                Color colWick = Color.FromArgb(255, 251, 255, 0);          // wick   — always 100% opaque
                Color colBody = Color.FromArgb(bodyAlpha, 251, 255, 0);    // body   — scales with cpt

                double bodyT = Math.Max(open, close);
                double bodyB = Math.Min(open, close);

                // Precise bar timing
                DateTime t1 = Bars[index].OpenTime;
                TimeSpan span = index > 0
                    ? t1 - Bars[index - 1].OpenTime
                    : (index < Bars.Count - 1 ? Bars[index + 1].OpenTime - t1 : TimeSpan.FromMinutes(15));
                DateTime t2   = t1 + span;
                DateTime tMid = t1 + TimeSpan.FromTicks(span.Ticks / 2); // exact bar centre

                // ── 1. Wick: vertical line through bar centre, HIGH → LOW (always fully yellow)
                Chart.DrawTrendLine("cnr_wk_" + index, tMid, high, tMid, low, colWick, 2, LineStyle.Solid);

                // ── 2. Body: filled rectangle OPEN → CLOSE, full bar width
                //    Thickness = 1 so the yellow border also covers candle outline edges
                if (bodyT > bodyB)
                {
                    var body = Chart.DrawRectangle("cnr_b_" + index, t1, bodyT, t2, bodyB, colBody);
                    body.IsFilled  = true;
                    body.Thickness = 1;
                }
                else
                {
                    // Doji — draw a horizontal line instead
                    Chart.DrawTrendLine("cnr_b_" + index, t1, bodyT, t2, bodyT, colWick, 2, LineStyle.Solid);
                }
            }
            else
            {
                Chart.RemoveObject("cnr_wk_" + index);
                Chart.RemoveObject("cnr_b_"  + index);
            }


            // ── Signal arrows ─────────────────────────────────────────
            if (longSignal)
                Chart.DrawIcon("sig_L_" + index, ChartIconType.UpTriangle,
                               index, low - Symbol.PipSize * 10, Color.FromArgb(204, 0, 255, 0));

            if (shortSignal)
                Chart.DrawIcon("sig_S_" + index, ChartIconType.DownTriangle,
                               index, high + Symbol.PipSize * 10, Color.FromArgb(204, 255, 0, 0));

            // ── Info table & alerts on last (live) bar only ───────────
            if (index == Bars.Count - 1)
            {
                DrawInfoTable(narrowRange, breakout, cpt, isBullish, isBearish);
                HandleAlerts(index, narrowRange, longSignal, shortSignal, close, high, low);
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  I N F O   T A B L E
        // ════════════════════════════════════════════════════════════════
        private void DrawInfoTable(bool narrowRange, bool breakout, int cpt, bool bull, bool bear)
        {
            string mode   = UseManual ? "◈ MANUAL" : "◎ AUTO";
            string status = breakout   ? "⚡ BREAKOUT!"
                          : narrowRange ? $"🟡 CONSOLIDATING [{cpt}/{_activeConsec}]"
                          : "👁  Watching...";
            string dir    = bull ? "▲ BULLISH" : bear ? "▼ BEARISH" : "── NEUTRAL";

            string text =
                $"  GOD v2.3  |  {mode}  \n" +
                $"  ─────────────────────\n" +
                $"  Pair    {Symbol.Name}\n" +
                $"  HTF     {HTFLabel()}\n" +
                $"  Smooth  {_activeSmooth}\n" +
                $"  NR%     {_activeNR}%\n" +
                $"  Consec  {_activeConsec}\n" +
                $"  ─────────────────────\n" +
                $"  Status  {status}\n" +
                $"  HMA     {dir}  ";

            Chart.DrawStaticText("GodTable", text,
                VerticalAlignment.Top, HorizontalAlignment.Right,
                Color.FromArgb(220, 255, 242, 0));
        }

        // ════════════════════════════════════════════════════════════════
        //  A L E R T S   (deduped — fires once per event)
        // ════════════════════════════════════════════════════════════════
        private void HandleAlerts(int index, bool narrowRange, bool longSignal, bool shortSignal,
                                   double close, double high, double low)
        {
            string time = Bars[index].OpenTime.ToString("dd/MM HH:mm") + " UTC";

            // Consolidation STARTS (first NR bar of a new NR sequence)
            if (narrowRange && !_prevConsolidation)
                FireAlert("🟡 CONSOLIDATION BEGINS",
                    $"🟡 CONSOLIDATION DETECTED\n" +
                    $"Pair:  {Symbol.Name}\n" +
                    $"HTF:   {HTFLabel()}\n" +
                    $"Price: {close:F5}\n" +
                    $"Time:  {time}\n" +
                    $"→ Watch for breakout signal!");

            // Long breakout
            if (longSignal && !_prevLong)
                FireAlert("🟢 LONG BREAKOUT",
                    $"🟢 LONG BREAKOUT!\n" +
                    $"Pair:  {Symbol.Name}\n" +
                    $"HTF:   {HTFLabel()}\n" +
                    $"Price: {close:F5}\n" +
                    $"High:  {high:F5}\n" +
                    $"Time:  {time}\n" +
                    $"→ Place L0/L1 lines and ARM The Architect!");

            // Short breakout
            if (shortSignal && !_prevShort)
                FireAlert("🔴 SHORT BREAKOUT",
                    $"🔴 SHORT BREAKOUT!\n" +
                    $"Pair:  {Symbol.Name}\n" +
                    $"HTF:   {HTFLabel()}\n" +
                    $"Price: {close:F5}\n" +
                    $"Low:   {low:F5}\n" +
                    $"Time:  {time}\n" +
                    $"→ Place L0/L1 lines and ARM The Architect!");

            _prevConsolidation = narrowRange;
            _prevLong          = longSignal;
            _prevShort         = shortSignal;
        }

        private void FireAlert(string title, string message)
        {
            Print($"🔔  [{title}]  {message.Replace('\n', ' ')}");

            if (EnablePush)
            {
                try { Notifications.SendEmail("god@ctrader", "push", title, message); }
                catch { }
            }

            if (EnableEmail && !string.IsNullOrEmpty(AlertEmail))
            {
                try
                {
                    Notifications.SendEmail("god-indicator@the-architect.bot", AlertEmail,
                        $"God Indicator v2.3 — {title}", message);
                }
                catch (Exception ex) { Print($"📧 Email error: {ex.Message}"); }
            }

            if (EnableTelegram &&
                !string.IsNullOrEmpty(TelegramToken) &&
                !string.IsNullOrEmpty(TelegramChatId))
            {
                SendTelegram(message);
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  T E L E G R A M   (HTTP POST — Bot API)
        // ────────────────────────────────────────────────────────────────
        private void SendTelegram(string message)
        {
            try
            {
                string url  = $"https://api.telegram.org/bot{TelegramToken}/sendMessage";
                string json = "{" +
                              $"\"chat_id\":\"{EscJson(TelegramChatId)}\"," +
                              $"\"text\":\"{EscJson(message)}\"," +
                              "\"parse_mode\":\"HTML\"" +
                              "}";

                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    var content = new System.Net.Http.StringContent(
                        json, System.Text.Encoding.UTF8, "application/json");
                    httpClient.PostAsync(url, content).GetAwaiter().GetResult();
                    Print("📲  Telegram sent OK");
                }
            }
            catch (Exception ex)
            {
                Print($"📲  Telegram error: {ex.Message}");
            }
        }

        private static string EscJson(string s)
            => (s ?? "")
               .Replace("\\", "\\\\")
               .Replace("\"",  "\\\"")
               .Replace("\n",  "\\n")
               .Replace("\r",  "");
    }
}
