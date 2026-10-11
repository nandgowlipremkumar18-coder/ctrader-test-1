using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    // ════════════════════════════════════════════════════════════════════
    //
    //   ███████╗███╗   ██╗██╗██████╗ ███████╗██████╗     ██████╗  ██████╗ ████████╗
    //   ██╔════╝████╗  ██║██║██╔══██╗██╔════╝██╔══██╗    ██╔══██╗██╔═══██╗╚══██╔══╝
    //   ███████╗██╔██╗ ██║██║██████╔╝█████╗  ██████╔╝    ██████╔╝██║   ██║   ██║
    //   ╚════██║██║╚██╗██║██║██╔═══╝ ██╔══╝  ██╔══██╗    ██╔══██╗██║   ██║   ██║
    //   ███████║██║ ╚████║██║██║     ███████╗██║  ██║    ██████╔╝╚██████╔╝   ██║
    //   ╚══════╝╚═╝  ╚═══╝╚═╝╚═╝     ╚══════╝╚═╝  ╚═╝    ╚═════╝  ╚═════╝    ╚═╝
    //
    //   LEVEL 4.1 — Structural Lockdown Edition
    //   ► Zero-Repaint Scanner   : L1/L0 anchored to closed HTF bars ONLY
    //   ► Iron Vault Sizer       : Risk = Math.Abs(L1-L0) + Spread Shield
    //   ► Profit Manager         : 50% partial @ 2.25x → SL to midpoint zone
    //   ► 3x Bar-Extreme Trail   : SL ratchets on each closed bar extreme
    //
    // ════════════════════════════════════════════════════════════════════

    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class TheArchitectAuto : Robot
    {
        // ────────────────────────────────────────────────────────────────
        //  P A R A M E T E R S  —  Scanner
        // ────────────────────────────────────────────────────────────────

        [Parameter("Range / HMA Length", Group = "🔍 Scanner Settings", DefaultValue = 14, MinValue = 5)]
        public int RangeLen { get; set; }

        [Parameter("HTF (minutes: 15/30/60/240)", Group = "🔍 Scanner Settings", DefaultValue = 60, MinValue = 1)]
        public int ManualTFMinutes { get; set; }

        [Parameter("Smoothing", Group = "🔍 Scanner Settings", DefaultValue = 4, MinValue = 1, MaxValue = 20)]
        public int ManualSmooth { get; set; }

        [Parameter("NR Threshold %", Group = "🔍 Scanner Settings", DefaultValue = 80.0, MinValue = 50.0, MaxValue = 100.0, Step = 0.5)]
        public double ManualNR { get; set; }

        [Parameter("Consecutive Bars", Group = "🔍 Scanner Settings", DefaultValue = 3, MinValue = 1, MaxValue = 20)]
        public int ManualConsec { get; set; }

        // ────────────────────────────────────────────────────────────────
        //  P A R A M E T E R S  —  Execution
        // ────────────────────────────────────────────────────────────────

        [Parameter("Risk % Per Trade", Group = "⚠️ Risk Management", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 10.0, Step = 0.1)]
        public double RiskPercent { get; set; }

        [Parameter("Spread Shield (pips)", Group = "⚠️ Risk Management", DefaultValue = 2.0, MinValue = 0.0, MaxValue = 20.0, Step = 0.5)]
        public double SpreadShieldPips { get; set; }

        // ────────────────────────────────────────────────────────────────
        //  H T F   S C A N N E R   S T A T E
        // ────────────────────────────────────────────────────────────────

        private TimeFrame _activeTF;
        private Bars      _htfBars;

        // HTF data arrays (rebuilt only when new HTF bar closes — no repaint)
        private double[] _htfPriceRange;
        private double[] _htfAvgRange;
        private bool[]   _htfNarrowRange;
        private int[]    _htfCpt;
        private bool[]   _htfBreakout;
        private int      _lastHTFCount = 0;

        // HMA buffer (chart-bar indexed)
        private double[] _hmaBuffer;

        // BOX coordinates — LOCKED to closed bars, NEVER touched on tick
        private double _boxHigh        = double.MinValue;
        private double _boxLow         = double.MaxValue;
        private int    _boxBarCount    = 0;
        private bool   _inSqueezeZone = false;

        // Previous closed-bar state (edge detection)
        private bool _prevWasNR   = false;
        private bool _prevLong    = false;
        private bool _prevShort   = false;

        // ────────────────────────────────────────────────────────────────
        //  E X E C U T I O N   S T A T E
        // ────────────────────────────────────────────────────────────────

        private bool   _isArmed;
        private double _l1;    // Box ceiling  — exact highest high of squeeze cluster
        private double _l0;    // Box floor    — exact lowest  low  of squeeze cluster
        private double _gap;   // = Math.Abs(L1 - L0)  — the Iron Vault risk anchor

        // Entry prices (just outside box + spread shield)
        private double _aggrBuy;
        private double _aggrSell;

        // Architectural TP levels (measured in gap multiples from L0)
        private double _buyTP225;    // L0 + gap * 2.25
        private double _buyTP300;    // L0 + gap * 3.00
        private double _sellTP225;   // L0 - gap * 2.25  (mirrored below L0)
        private double _sellTP300;   // L0 - gap * 3.00

        // Midpoint protection SL (locked in after 50% partial at 2.25x)
        private double _buyMidSL;    // L0 + gap * 0.75   (buy:  75% into box from floor)
        private double _sellMidSL;   // L0 + gap * 0.25   (sell: 25% from floor = 75% from ceiling)

        // Partial close flags
        private bool _buyPartialDone  = false;
        private bool _sellPartialDone = false;

        // 3x bar-extreme trailing state
        private bool   _buyTrailingActive  = false;
        private bool   _sellTrailingActive = false;
        private double _buyTrailSL         = double.MinValue;   // ratchets UP on bar lows
        private double _sellTrailSL        = double.MaxValue;   // ratchets DOWN on bar highs

        // Pending stop orders
        private PendingOrder _aggrBuyOrder;
        private PendingOrder _aggrSellOrder;

        // UI
        private StackPanel _panel;
        private TextBlock  _statusText;

        // ════════════════════════════════════════════════════════════════
        //  O N   S T A R T
        // ════════════════════════════════════════════════════════════════
        protected override void OnStart()
        {
            _activeTF  = TFFromMinutes(ManualTFMinutes);
            _htfBars   = MarketData.GetBars(_activeTF);
            _hmaBuffer = new double[Bars.Count + 10000];
            for (int i = 0; i < _hmaBuffer.Length; i++) _hmaBuffer[i] = double.NaN;

            CreatePanel();
            Positions.Opened += OnPositionOpened;

            // ── ZERO-REPAINT HISTORY REPLAY ──────────────────────────────
            // Process only CLOSED bars (index 0 to Count-2).
            // The live open bar (Count-1) is intentionally NEVER touched here.
            int historyEnd = Bars.Count - 2;
            for (int i = 0; i <= historyEnd; i++)
                ProcessClosedBar(i);

            Print($"🌟 Autonomous Architect v4.1 LOCKED | HTF: {_activeTF} | NR%: {ManualNR} | Consec: {ManualConsec} | Shield: {SpreadShieldPips}p");
        }

        // ════════════════════════════════════════════════════════════════
        //  O N   B A R   — fires AFTER a bar closes (zero repaint)
        // ════════════════════════════════════════════════════════════════
        protected override void OnBar()
        {
            // Bars.Count - 2 is the index of the bar that JUST closed
            int closedIdx = Bars.Count - 2;
            if (closedIdx >= 0)
                ProcessClosedBar(closedIdx);

            // Ratchet bar-extreme trailing SL on the newly closed bar
            if (_isArmed)
                UpdateBarExtremTrail(closedIdx);
        }

        // ════════════════════════════════════════════════════════════════
        //  O N   T I C K   — live trade management ONLY
        // ════════════════════════════════════════════════════════════════
        protected override void OnTick()
        {
            if (!_isArmed) return;
            ManageActivePositions();
        }

        // ════════════════════════════════════════════════════════════════
        //  C O R E   S C A N N E R  — closed bar processing
        //  ► Called ONLY from OnStart (history) and OnBar (new close)
        //  ► NEVER called from OnTick — this is the repaint firewall
        // ════════════════════════════════════════════════════════════════
        private void ProcessClosedBar(int index)
        {
            // Expand HMA buffer if chart has grown
            if (index >= _hmaBuffer.Length)
            {
                int newLen = _hmaBuffer.Length + 10000;
                Array.Resize(ref _hmaBuffer, newLen);
                for (int i = index; i < _hmaBuffer.Length; i++) _hmaBuffer[i] = double.NaN;
            }

            // Rebuild HTF arrays only when new HTF bars have closed
            if (_htfBars.Count != _lastHTFCount)
                ComputeHTFData();

            if (_htfPriceRange == null) return;

            // ── BAR VALUES (locked at close — no future change) ─────────
            double high  = Bars.HighPrices[index];
            double low   = Bars.LowPrices[index];

            int  htfIdx      = FindHTFIndex(index);
            bool narrowRange = htfIdx < _htfNarrowRange.Length && _htfNarrowRange[htfIdx];
            bool breakout    = htfIdx < _htfBreakout.Length    && _htfBreakout[htfIdx];
            int  cpt         = htfIdx < _htfCpt.Length         ? _htfCpt[htfIdx] : 0;

            // ── HMA DIRECTION on this closed bar ────────────────────────
            double hma = HMA(index, RangeLen);
            _hmaBuffer[index] = hma;

            bool isBullish = false, isBearish = false;
            if (!double.IsNaN(hma) && index >= 2)
            {
                double hmaPrev2 = _hmaBuffer[index - 2];
                if (!double.IsNaN(hmaPrev2))
                {
                    isBullish = hma > hmaPrev2;
                    isBearish = hma < hmaPrev2;
                }
            }

            // ── SQUEEZE BOX TRACKING (anchored exclusively to closed bars) ──
            bool nrJustStarted = narrowRange && !_prevWasNR;
            bool nrJustEnded   = !narrowRange && _prevWasNR;

            if (nrJustStarted)
            {
                // Fresh squeeze — seed the box with this bar's exact extremes
                _boxHigh       = high;
                _boxLow        = low;
                _boxBarCount   = 1;
                _inSqueezeZone = true;
                if (_isArmed) DisarmTrap();  // Invalidate previous setup
            }
            else if (narrowRange && _inSqueezeZone)
            {
                // Extend box — always take the absolute extreme of ALL squeeze bars
                _boxHigh = Math.Max(_boxHigh, high);
                _boxLow  = Math.Min(_boxLow,  low);
                _boxBarCount++;
            }
            else if (nrJustEnded)
            {
                _inSqueezeZone = false;  // Squeeze cluster complete — box coordinates are now final
            }

            // ── BREAKOUT SIGNAL DETECTION ────────────────────────────────
            bool isFirstOfHTF = index == 0 || FindHTFIndex(index - 1) != htfIdx;
            bool longSignal   = breakout && isBullish && isFirstOfHTF;
            bool shortSignal  = breakout && isBearish && isFirstOfHTF;

            if (longSignal && !_prevLong)
            {
                Print($"🟢 LONG Breakout on closed bar {Bars[index].OpenTime:HH:mm} | Box: {_boxHigh:F5}/{_boxLow:F5} [{_boxBarCount} bars]");
                if (_boxBarCount >= ManualConsec && _boxHigh > _boxLow && !_isArmed)
                    ArmTheTrap(_boxHigh, _boxLow);
            }

            if (shortSignal && !_prevShort)
            {
                Print($"🔴 SHORT Breakout on closed bar {Bars[index].OpenTime:HH:mm} | Box: {_boxHigh:F5}/{_boxLow:F5} [{_boxBarCount} bars]");
                if (_boxBarCount >= ManualConsec && _boxHigh > _boxLow && !_isArmed)
                    ArmTheTrap(_boxHigh, _boxLow);
            }

            // Lock state for next bar's edge detection
            _prevWasNR  = narrowRange;
            _prevLong   = longSignal;
            _prevShort  = shortSignal;
        }

        // ════════════════════════════════════════════════════════════════
        //  H T F   D A T A   C O M P U T A T I O N
        // ════════════════════════════════════════════════════════════════
        private void ComputeHTFData()
        {
            int n = _htfBars.Count;
            if (n < Math.Max(RangeLen, ManualSmooth) + 4) return;

            _htfPriceRange  = new double[n];
            _htfAvgRange    = new double[n];
            _htfNarrowRange = new bool[n];
            _htfCpt         = new int[n];
            _htfBreakout    = new bool[n];

            for (int i = 0; i < n; i++)
            {
                // Smoothed bar range (SMA of High-Low)
                double smaSum = 0; int cnt = 0;
                for (int j = i; j >= Math.Max(0, i - ManualSmooth + 1); j--)
                {
                    smaSum += _htfBars[j].High - _htfBars[j].Low;
                    cnt++;
                }
                _htfPriceRange[i] = cnt > 0 ? smaSum / cnt : 0;

                // EMA of range (the average range baseline)
                _htfAvgRange[i] = i == 0
                    ? _htfPriceRange[i]
                    : (_htfAvgRange[i - 1] * (RangeLen - 1) + _htfPriceRange[i]) / RangeLen;

                // Narrow Range: bar range compresses below the NR threshold %
                _htfNarrowRange[i] = _htfAvgRange[i] > 0 &&
                                     _htfPriceRange[i] < ManualNR * _htfAvgRange[i] / 100.0;
            }

            // Consecutive NR bar count
            _htfCpt[0] = _htfNarrowRange[0] ? 1 : 0;
            for (int i = 1; i < n; i++)
                _htfCpt[i] = _htfNarrowRange[i] ? _htfCpt[i - 1] + 1 : 0;

            // Breakout: previous bar completed the squeeze, this bar expands
            for (int i = 1; i < n; i++)
                _htfBreakout[i] = _htfCpt[i - 1] >= ManualConsec && !_htfNarrowRange[i];

            _lastHTFCount = n;
        }

        private int FindHTFIndex(int chartIndex)
        {
            if (_htfBars == null || _htfBars.Count == 0) return 0;
            DateTime t  = Bars[chartIndex].OpenTime;
            int lo = 0, hi = _htfBars.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_htfBars[mid].OpenTime <= t) lo = mid;
                else                              hi = mid - 1;
            }
            return lo;
        }

        // ════════════════════════════════════════════════════════════════
        //  H U L L   M O V I N G   A V E R A G E
        // ════════════════════════════════════════════════════════════════
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
                double weight  = sqrtP - i;
                wSum   += (2.0 * wmaHalf - wmaFull) * weight;
                wTotal += weight;
            }
            return wTotal > 0 ? wSum / wTotal : double.NaN;
        }

        private TimeFrame TFFromMinutes(int m)
        {
            if (m <= 5)   return TimeFrame.Minute5;
            if (m <= 15)  return TimeFrame.Minute15;
            if (m <= 30)  return TimeFrame.Minute30;
            if (m <= 60)  return TimeFrame.Hour;
            if (m <= 120) return TimeFrame.Hour2;
            return TimeFrame.Hour4;
        }

        // ════════════════════════════════════════════════════════════════
        //  I R O N   V A U L T  —  A R M   T H E   T R A P
        // ════════════════════════════════════════════════════════════════
        private void ArmTheTrap(double l1, double l0)
        {
            if (_isArmed) return;

            _l1  = l1;
            _l0  = l0;
            _gap = Math.Abs(_l1 - _l0);   // Iron Vault: full box gap = risk anchor

            if (_gap < Symbol.PipSize)
            {
                Print("⚠️ ArmTheTrap aborted — gap too small.");
                return;
            }

            double shield = SpreadShieldPips * Symbol.PipSize;

            // Entry orders: stop just outside the box + spread shield
            _aggrBuy  = _l1 + shield;   // Buy Stop above ceiling
            _aggrSell = _l0 - shield;   // Sell Stop below floor

            // ── Architectural TP levels (gap multiples from L0) ─────────
            // BUY  profits measured upward from L0
            _buyTP225  = _l0 + _gap * 2.25;
            _buyTP300  = _l0 + _gap * 3.00;

            // SELL profits measured downward from L0 (mirror below the box floor)
            _sellTP225 = _l0 - _gap * 2.25;
            _sellTP300 = _l0 - _gap * 3.00;

            // ── Midpoint protection SL (used after 50% partial at 2.25x) ─
            _buyMidSL  = _l0 + _gap * 0.75;   // Buy:  75% into box from floor
            _sellMidSL = _l0 + _gap * 0.25;   // Sell: 25% from floor (75% from ceiling)

            // Reset all execution state
            _buyPartialDone     = false;
            _sellPartialDone    = false;
            _buyTrailingActive  = false;
            _sellTrailingActive = false;
            _buyTrailSL         = double.MinValue;
            _sellTrailSL        = double.MaxValue;

            _isArmed = true;
            UpdateStatus($"🔫 ARMED  L1:{_l1:F5}  L0:{_l0:F5}  Gap:{(_gap / Symbol.PipSize):F1}p", Color.LimeGreen);
            Print($"🔫 TRAP ARMED | L1={_l1:F5} L0={_l0:F5} Gap={(_gap / Symbol.PipSize):F1}p | Shield={SpreadShieldPips}p");

            PlaceAggressiveOrders();
        }

        private void DisarmTrap()
        {
            CancelAggressiveOrders();
            _isArmed            = false;
            _buyTrailingActive  = false;
            _sellTrailingActive = false;
            UpdateStatus("👁 FLOATING MONITOR | Scanning...", Color.Silver);
        }

        // ════════════════════════════════════════════════════════════════
        //  O R D E R   P L A C E M E N T  —  Iron Vault Position Sizer
        // ════════════════════════════════════════════════════════════════
        private void PlaceAggressiveOrders()
        {
            // ── Iron Vault Rule: risk distance = Math.Abs(L1 - L0) ──────
            double riskPips = _gap / Symbol.PipSize;

            // BUY STOP — entry above ceiling, initial SL at box floor (L0)
            if (_aggrBuyOrder == null && Symbol.Ask < _aggrBuy)
            {
                double vol = CalcVolume(riskPips);
                if (vol > 0)
                {
                    var r = PlaceStopOrder(TradeType.Buy, SymbolName, vol, _aggrBuy, "ARCHITECT_Buy", null, null);
                    if (r.IsSuccessful) _aggrBuyOrder = r.PendingOrder;
                    else Print($"⚠️ Buy Stop failed: {r.Error}");
                }
            }

            // SELL STOP — entry below floor, initial SL at box ceiling (L1)
            if (_aggrSellOrder == null && Symbol.Bid > _aggrSell)
            {
                double vol = CalcVolume(riskPips);
                if (vol > 0)
                {
                    var r = PlaceStopOrder(TradeType.Sell, SymbolName, vol, _aggrSell, "ARCHITECT_Sell", null, null);
                    if (r.IsSuccessful) _aggrSellOrder = r.PendingOrder;
                    else Print($"⚠️ Sell Stop failed: {r.Error}");
                }
            }
        }

        private void CancelAggressiveOrders()
        {
            if (_aggrBuyOrder  != null) { CancelPendingOrder(_aggrBuyOrder);  _aggrBuyOrder  = null; }
            if (_aggrSellOrder != null) { CancelPendingOrder(_aggrSellOrder); _aggrSellOrder = null; }
        }

        private double CalcVolume(double slPips)
        {
            if (slPips <= 0) return 0;
            double riskAmount = Account.Balance * (RiskPercent / 100.0);
            double rawVolume  = riskAmount / (slPips * Symbol.PipValue);
            return Symbol.NormalizeVolumeInUnits(
                Math.Max(rawVolume, Symbol.VolumeInUnitsMin), RoundingMode.Down);
        }

        // ════════════════════════════════════════════════════════════════
        //  P O S I T I O N   O P E N E D  —  Set Initial SL
        // ════════════════════════════════════════════════════════════════
        private void OnPositionOpened(PositionOpenedEventArgs args)
        {
            var pos = args.Position;
            if (pos.SymbolName != SymbolName || !pos.Label.StartsWith("ARCHITECT_")) return;

            // Initial SL = exact opposite side of the box (full gap risk)
            double slPrice = pos.TradeType == TradeType.Buy ? _l0 : _l1;

            ModifyPosition(pos, slPrice, null, ProtectionType.Absolute);

            if (pos.TradeType == TradeType.Buy)  _aggrBuyOrder  = null;
            else                                 _aggrSellOrder = null;

            Print($"✅ {pos.TradeType} OPENED | Entry:{pos.EntryPrice:F5} | SL:{slPrice:F5} | Risk:{(_gap / Symbol.PipSize):F1}p");
            UpdateStatus($"🔥 IN TRADE ({pos.TradeType}) | Watching 2.25x...", Color.Cyan);
        }

        // ════════════════════════════════════════════════════════════════
        //  A R C H I T E C T U R A L   P R O F I T   M A N A G E R
        //  Called on every tick — pure price-level logic, no calculations
        // ════════════════════════════════════════════════════════════════
        private void ManageActivePositions()
        {
            var snapshot = new List<Position>();
            foreach (var p in Positions)
                if (p.SymbolName == SymbolName && p.Label.StartsWith("ARCHITECT_"))
                    snapshot.Add(p);

            foreach (var pos in snapshot)
            {
                if (pos.TradeType == TradeType.Buy)  ManageBuy(pos);
                else                                 ManageSell(pos);
            }
        }

        // ── BUY MANAGEMENT ──────────────────────────────────────────────
        private void ManageBuy(Position pos)
        {
            double bid = Symbol.Bid;

            // PHASE 1 — 2.25x hit: 50% partial close + SL → midpoint zone
            if (!_buyPartialDone && bid >= _buyTP225)
            {
                double halfVol = Symbol.NormalizeVolumeInUnits(pos.VolumeInUnits * 0.5, RoundingMode.Down);
                if (halfVol >= Symbol.VolumeInUnitsMin)
                {
                    var res = ClosePosition(pos, halfVol);
                    if (res.IsSuccessful)
                    {
                        _buyPartialDone = true;
                        // Immediately shift SL to midpoint protection zone
                        ModifyPosition(pos, _buyMidSL, pos.TakeProfit, ProtectionType.Absolute);
                        Print($"✂️ BUY 50% banked @ {bid:F5} ({(_buyTP225 / Symbol.PipSize):F0}p) | SL → midpoint {_buyMidSL:F5}");
                        UpdateStatus($"✂️ 50% BANKED @ 2.25x | SL locked to midpoint", Color.Yellow);
                    }
                }
            }

            // PHASE 2 — 3.0x hit: activate bar-extreme trailing
            if (_buyPartialDone && !_buyTrailingActive && bid >= _buyTP300)
            {
                _buyTrailingActive = true;
                int lastClosed = Bars.Count - 2;
                if (lastClosed >= 0)
                    _buyTrailSL = Bars.LowPrices[lastClosed];   // Seed from last closed bar low

                Print($"🔒 BUY 3x REACHED — Bar-extreme trail activated | Trail SL init: {_buyTrailSL:F5}");
                UpdateStatus("🚀 3x BREACHED — BAR TRAIL ACTIVE", Color.MediumAquamarine);
            }

            // PHASE 3 — Apply bar-extreme trail SL on each tick (ratchet UP only)
            if (_buyTrailingActive && _buyTrailSL > double.MinValue)
            {
                double? curSL = pos.StopLoss;
                if (curSL == null || _buyTrailSL > curSL.Value)
                    ModifyPosition(pos, _buyTrailSL, pos.TakeProfit, ProtectionType.Absolute);
            }
        }

        // ── SELL MANAGEMENT ─────────────────────────────────────────────
        private void ManageSell(Position pos)
        {
            double ask = Symbol.Ask;

            // PHASE 1 — 2.25x hit: 50% partial close + SL → midpoint zone
            if (!_sellPartialDone && ask <= _sellTP225)
            {
                double halfVol = Symbol.NormalizeVolumeInUnits(pos.VolumeInUnits * 0.5, RoundingMode.Down);
                if (halfVol >= Symbol.VolumeInUnitsMin)
                {
                    var res = ClosePosition(pos, halfVol);
                    if (res.IsSuccessful)
                    {
                        _sellPartialDone = true;
                        // Shift SL to midpoint protection zone (L0 + gap * 0.25)
                        ModifyPosition(pos, _sellMidSL, pos.TakeProfit, ProtectionType.Absolute);
                        Print($"✂️ SELL 50% banked @ {ask:F5} | SL → midpoint {_sellMidSL:F5}");
                        UpdateStatus($"✂️ 50% BANKED @ 2.25x | SL locked to midpoint", Color.Yellow);
                    }
                }
            }

            // PHASE 2 — 3.0x hit: activate bar-extreme trailing
            if (_sellPartialDone && !_sellTrailingActive && ask <= _sellTP300)
            {
                _sellTrailingActive = true;
                int lastClosed = Bars.Count - 2;
                if (lastClosed >= 0)
                    _sellTrailSL = Bars.HighPrices[lastClosed];  // Seed from last closed bar high

                Print($"🔒 SELL 3x REACHED — Bar-extreme trail activated | Trail SL init: {_sellTrailSL:F5}");
                UpdateStatus("🚀 3x BREACHED — BAR TRAIL ACTIVE", Color.MediumAquamarine);
            }

            // PHASE 3 — Apply bar-extreme trail SL on each tick (ratchet DOWN only)
            if (_sellTrailingActive && _sellTrailSL < double.MaxValue)
            {
                double? curSL = pos.StopLoss;
                if (curSL == null || _sellTrailSL < curSL.Value)
                    ModifyPosition(pos, _sellTrailSL, pos.TakeProfit, ProtectionType.Absolute);
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  B A R - E X T R E M E   T R A I L   U P D A T E R
        //  Called from OnBar() — ratchets trail SL on each new closed bar.
        //  Buy  trail: advances UP  to the new bar's LOW  (protects capital)
        //  Sell trail: advances DOWN to the new bar's HIGH (protects capital)
        // ════════════════════════════════════════════════════════════════
        private void UpdateBarExtremTrail(int closedIdx)
        {
            if (closedIdx < 0) return;

            if (_buyTrailingActive)
            {
                double barLow = Bars.LowPrices[closedIdx];
                if (barLow > _buyTrailSL)   // Only ratchet UP — never loosen
                {
                    _buyTrailSL = barLow;
                    Print($"📐 BUY trail ratcheted → {_buyTrailSL:F5} (bar low of {Bars[closedIdx].OpenTime:HH:mm})");
                }
            }

            if (_sellTrailingActive)
            {
                double barHigh = Bars.HighPrices[closedIdx];
                if (barHigh < _sellTrailSL)  // Only ratchet DOWN — never loosen
                {
                    _sellTrailSL = barHigh;
                    Print($"📐 SELL trail ratcheted → {_sellTrailSL:F5} (bar high of {Bars[closedIdx].OpenTime:HH:mm})");
                }
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  U I   P A N E L
        // ════════════════════════════════════════════════════════════════
        private void CreatePanel()
        {
            _panel = new StackPanel
            {
                Orientation         = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment   = VerticalAlignment.Top,
                Margin              = new Thickness(10),
                BackgroundColor     = Color.FromArgb(215, 8, 10, 20),
                Width               = 340
            };

            var title = new TextBlock
            {
                Text            = "⚡ AUTONOMOUS ARCHITECT v4.1",
                FontSize        = 13,
                FontWeight      = FontWeight.Bold,
                ForegroundColor = Color.FromArgb(255, 255, 215, 0),
                Margin          = new Thickness(10, 10, 10, 2),
                TextAlignment   = TextAlignment.Center
            };

            var subtitle = new TextBlock
            {
                Text            = "[ STRUCTURAL LOCKDOWN EDITION ]",
                FontSize        = 9,
                ForegroundColor = Color.FromArgb(180, 160, 140, 255),
                Margin          = new Thickness(10, 0, 10, 4),
                TextAlignment   = TextAlignment.Center
            };

            _statusText = new TextBlock
            {
                Text            = "👁 FLOATING MONITOR | Scanning...",
                FontSize        = 11,
                ForegroundColor = Color.Silver,
                Margin          = new Thickness(10, 0, 10, 10),
                TextAlignment   = TextAlignment.Center
            };

            _panel.AddChild(title);
            _panel.AddChild(subtitle);
            _panel.AddChild(_statusText);
            Chart.AddControl(_panel);
        }

        private void UpdateStatus(string text, Color color)
        {
            if (_statusText == null) return;
            _statusText.Text            = text;
            _statusText.ForegroundColor = color;
        }
    }
}
