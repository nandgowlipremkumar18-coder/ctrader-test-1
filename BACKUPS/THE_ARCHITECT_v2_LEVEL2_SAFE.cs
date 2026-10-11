using System;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    // ════════════════════════════════════════════════════════════════════
    //
    //   ████████╗██╗  ██╗███████╗     █████╗ ██████╗  ██████╗██╗  ██╗
    //      ██╔══╝██║  ██║██╔════╝    ██╔══██╗██╔══██╗██╔════╝██║  ██║
    //      ██║   ███████║█████╗      ███████║██████╔╝██║     ███████║
    //      ██║   ██╔══██║██╔══╝      ██╔══██║██╔══██╗██║     ██╔══██║
    //      ██║   ██║  ██║███████╗    ██║  ██║██║  ██║╚██████╗██║  ██║
    //      ╚═╝   ╚═╝  ╚═╝╚══════╝   ╚═╝  ╚═╝╚═╝  ╚═╝ ╚═════╝╚═╝  ╚═╝
    //
    //   ██╗████████╗███████╗ ██████╗████████╗
    //   ██║╚══██╔══╝██╔════╝██╔════╝╚══██╔══╝
    //   ██║   ██║   █████╗  ██║        ██║
    //   ██║   ██║   ██╔══╝  ██║        ██║
    //   ██║   ██║   ███████╗╚██████╗   ██║
    //   ╚═╝   ╚═╝   ╚══════╝ ╚═════╝   ╚═╝
    //
    //   ╔══════════════════════════════════════════════════════════════╗
    //   ║   LEVEL 2 — SAFE BACKUP                                     ║
    //   ║   Saved: 2026-06-19                                         ║
    //   ║   Fixes included vs Level 1:                                ║
    //   ║   ✅ VolumeInUnitsMin + ProtectionType.Absolute             ║
    //   ║   ✅ ONE trade per setup (_buySetupDone/_sellSetupDone)      ║
    //   ║   ✅ Aggressive SL = Line1 (buy) / Line0 (sell) always      ║
    //   ║   ✅ SL pinned via ModifyPosition (absolute price, not pips) ║
    //   ║   ✅ Panel shows ACTIVE / DONE / Waiting states             ║
    //   ║   Commander: You place the lines. CEO: Does everything else. ║
    //   ╚══════════════════════════════════════════════════════════════╝
    //
    // ════════════════════════════════════════════════════════════════════

    // ─── Stop Loss Option Enum ───────────────────────────────────────────
    public enum SlOption
    {
        Zero    = 0,    // SL at entry boundary  (Line1 for Buy | Line0 for Sell)
        Fifty   = 50,   // SL at mid-zone        (0.75 for Buy  | 0.25  for Sell)
        Hundred = 100   // SL at the MIDLINE     (0.5  for BOTH sides)
    }

    // ════════════════════════════════════════════════════════════════════
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class TheArchitect : Robot
    {
        // ────────────────────────────────────────────────────────────────
        //  P A R A M E T E R S
        // ────────────────────────────────────────────────────────────────

        [Parameter("Line 1 Price  (Top Anchor)", Group = "📍 Anchor Lines", DefaultValue = 0)]
        public double Line1Price { get; set; }

        [Parameter("Line 0 Price  (Bottom Anchor)", Group = "📍 Anchor Lines", DefaultValue = 0)]
        public double Line0Price { get; set; }

        [Parameter("Stop Loss Option", Group = "⚠️ Risk Management", DefaultValue = SlOption.Fifty)]
        public SlOption StopLossOption { get; set; }

        [Parameter("Risk % Per Trade", Group = "⚠️ Risk Management", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 10.0, Step = 0.1)]
        public double RiskPercent { get; set; }

        [Parameter("Aggressive Mode  (ON = fire on touch, OFF = wait candle close)", Group = "⚡ Entry Settings", DefaultValue = false)]
        public bool AggressiveMode { get; set; }

        // ────────────────────────────────────────────────────────────────
        //  C H A R T   L I N E   N A M E S
        // ────────────────────────────────────────────────────────────────

        private const string NAME_L1        = "SN_LINE1";
        private const string NAME_L0        = "SN_LINE0";
        private const string NAME_L075      = "SN_L075";
        private const string NAME_L050      = "SN_L050";          // ← MIDLINE (kill switch)
        private const string NAME_L025      = "SN_L025";
        private const string NAME_AGGR_BUY  = "SN_AGGR_BUY";     // 1.2 level
        private const string NAME_AGGR_SELL = "SN_AGGR_SELL";    // -0.2 level
        private const string NAME_BTP225    = "SN_BTP225";
        private const string NAME_BTP250    = "SN_BTP250";        // ← BUY middle target
        private const string NAME_BTP300    = "SN_BTP300";
        private const string NAME_STP125    = "SN_STP125";
        private const string NAME_STP150    = "SN_STP150";        // ← SELL middle target
        private const string NAME_STP200    = "SN_STP200";

        // ────────────────────────────────────────────────────────────────
        //  L E V E L   P R I C E   V A R I A B L E S
        // ────────────────────────────────────────────────────────────────

        private double _l1;          // Line 1  price
        private double _l0;          // Line 0  price
        private double _gap;         // L1 - L0 (the unit for all calculations)

        // Internal zone levels  (between L0 and L1)
        private double _l075;        // 0.75 of gap above L0
        private double _l050;        // 0.50 MIDLINE — SL kill switch for both sides
        private double _l025;        // 0.25 of gap above L0

        // Aggressive entry levels
        private double _aggrBuy;     // L0 + gap × 1.2   → aggressive buy trigger
        private double _aggrSell;    // L0 - gap × 0.2   → aggressive sell trigger

        // Buy TP levels  (above Line 1)
        private double _bTP225;      // L0 + gap × 2.25
        private double _bTP250;      // L0 + gap × 2.50  (BUY middle target — unlocks trail)
        private double _bTP300;      // L0 + gap × 3.00

        // Sell TP levels (below Line 0)
        private double _sTP125;      // L0 - gap × 1.25
        private double _sTP150;      // L0 - gap × 1.50  (SELL middle target — unlocks trail)
        private double _sTP200;      // L0 - gap × 2.00

        // ────────────────────────────────────────────────────────────────
        //  S T A T E   V A R I A B L E S
        // ────────────────────────────────────────────────────────────────

        private bool   _isArmed;         // Is the bot live and watching?
        private double _prevL1;          // Previous Line 1 price (movement detection)
        private double _prevL0;          // Previous Line 0 price (movement detection)
        private bool   _initialized;     // Did OnStart() complete successfully?

        // ── ONE TRADE PER SETUP — locks after first trade fires ──────────
        private bool _buySetupDone;      // true = BUY already taken this setup
        private bool _sellSetupDone;     // true = SELL already taken this setup

        // ────────────────────────────────────────────────────────────────
        //  U I   C O N T R O L S
        // ────────────────────────────────────────────────────────────────

        private StackPanel _panel;
        private TextBlock  _statusText;
        private TextBlock  _infoText;
        private Button     _armBtn;

        // ════════════════════════════════════════════════════════════════
        //  O N   S T A R T
        // ════════════════════════════════════════════════════════════════
        protected override void OnStart()
        {
            _l1 = Line1Price;
            _l0 = Line0Price;

            CreatePanel();

            if (_l1 == 0 && _l0 == 0)
            {
                Print("⚠️  Enter Line 1 and Line 0 prices in parameters, then restart.");
                return;
            }

            if (_l1 <= _l0)
            {
                Print("⚠️  Line 1 must be ABOVE Line 0. Please correct and restart.");
                return;
            }

            CalculateLevels();
            DrawAllLines();

            _prevL1      = _l1;
            _prevL0      = _l0;
            _initialized = true;

            UpdatePanel();
            Print($"✅  THE ARCHITECT initialized | Gap = {(_gap / Symbol.PipSize):F1} pips | L1: {_l1:F5} | L0: {_l0:F5}");
        }

        // ════════════════════════════════════════════════════════════════
        //  O N   T I C K   (fires every price tick)
        // ════════════════════════════════════════════════════════════════
        protected override void OnTick()
        {
            if (!_initialized) return;

            SyncLinePositions();

            if (LinesMoved())
            {
                OnLinesMoved();
                return;
            }

            if (!_isArmed) return;

            if (AggressiveMode)
                CheckAggressiveEntry();

            ManageTrailingSL();
            UpdatePanelInfo();
        }

        // ════════════════════════════════════════════════════════════════
        //  O N   B A R
        // ════════════════════════════════════════════════════════════════
        protected override void OnBar()
        {
            if (!_initialized || !_isArmed) return;
            CheckNormalEntry();
        }

        // ════════════════════════════════════════════════════════════════
        //  C A L C U L A T E   A L L   L E V E L S
        // ════════════════════════════════════════════════════════════════
        private void CalculateLevels()
        {
            _gap = _l1 - _l0;

            _l075 = _l0 + _gap * 0.75;
            _l050 = _l0 + _gap * 0.50;
            _l025 = _l0 + _gap * 0.25;

            _aggrBuy  = _l0 + _gap * 1.20;
            _aggrSell = _l0 - _gap * 0.20;

            _bTP225 = _l0 + _gap * 2.25;
            _bTP250 = _l0 + _gap * 2.50;
            _bTP300 = _l0 + _gap * 3.00;

            _sTP125 = _l0 - _gap * 1.25;
            _sTP150 = _l0 - _gap * 1.50;
            _sTP200 = _l0 - _gap * 2.00;
        }

        // ════════════════════════════════════════════════════════════════
        //  D R A W   A L L   L I N E S
        // ════════════════════════════════════════════════════════════════
        private void DrawAllLines()
        {
            Chart.DrawHorizontalLine(NAME_L1, _l1, Color.White, 2, LineStyle.Solid);
            Chart.DrawHorizontalLine(NAME_L0, _l0, Color.White, 2, LineStyle.Solid);

            Chart.DrawHorizontalLine(NAME_L075, _l075, Color.Orange, 1, LineStyle.Dots);
            Chart.DrawHorizontalLine(NAME_L050, _l050, Color.Yellow, 1, LineStyle.Dots);
            Chart.DrawHorizontalLine(NAME_L025, _l025, Color.Orange, 1, LineStyle.Dots);

            if (AggressiveMode)
            {
                Chart.DrawHorizontalLine(NAME_AGGR_BUY,  _aggrBuy,  Color.Cyan, 1, LineStyle.DotsRare);
                Chart.DrawHorizontalLine(NAME_AGGR_SELL, _aggrSell, Color.Cyan, 1, LineStyle.DotsRare);
            }

            Chart.DrawHorizontalLine(NAME_BTP225, _bTP225, Color.LimeGreen, 1, LineStyle.Dots);
            Chart.DrawHorizontalLine(NAME_BTP250, _bTP250, Color.LimeGreen, 1, LineStyle.Dots);
            Chart.DrawHorizontalLine(NAME_BTP300, _bTP300, Color.LimeGreen, 2, LineStyle.Solid);

            Chart.DrawHorizontalLine(NAME_STP125, _sTP125, Color.Crimson, 1, LineStyle.Dots);
            Chart.DrawHorizontalLine(NAME_STP150, _sTP150, Color.Crimson, 1, LineStyle.Dots);
            Chart.DrawHorizontalLine(NAME_STP200, _sTP200, Color.Crimson, 2, LineStyle.Solid);
        }

        private void RemoveLevelLines()
        {
            string[] levelNames = {
                NAME_L075, NAME_L050, NAME_L025,
                NAME_AGGR_BUY, NAME_AGGR_SELL,
                NAME_BTP225, NAME_BTP250, NAME_BTP300,
                NAME_STP125, NAME_STP150, NAME_STP200
            };
            foreach (var n in levelNames)
                Chart.RemoveObject(n);
        }

        // ════════════════════════════════════════════════════════════════
        //  L I N E   M O V E M E N T   D E T E C T I O N
        // ════════════════════════════════════════════════════════════════
        private void SyncLinePositions()
        {
            var l1Line = Chart.FindObject(NAME_L1) as ChartHorizontalLine;
            var l0Line = Chart.FindObject(NAME_L0) as ChartHorizontalLine;
            if (l1Line != null) _l1 = l1Line.Y;
            if (l0Line != null) _l0 = l0Line.Y;
        }

        private bool LinesMoved()
        {
            double tolerance = Symbol.PipSize * 0.5;
            return Math.Abs(_l1 - _prevL1) > tolerance ||
                   Math.Abs(_l0 - _prevL0) > tolerance;
        }

        private void OnLinesMoved()
        {
            if (_l1 <= _l0)
            {
                Print("⚠️  Invalid: Line 1 must be ABOVE Line 0.");
                _l1 = _prevL1;
                _l0 = _prevL0;
                return;
            }

            CalculateLevels();
            RemoveLevelLines();
            DrawAllLines();

            if (_isArmed)
            {
                _isArmed = false;
                Print("🔴  AUTO-DISARMED — Lines moved. Setup reset. Re-ARM when ready.");
                UpdatePanel();
            }

            _buySetupDone  = false;
            _sellSetupDone = false;
            _prevL1 = _l1;
            _prevL0 = _l0;
        }

        // ════════════════════════════════════════════════════════════════
        //  E N T R Y   L O G I C
        // ════════════════════════════════════════════════════════════════

        private void CheckNormalEntry()
        {
            double lastClose = Bars[Bars.Count - 2].Close;

            if (lastClose > _l1 && !_buySetupDone)
                OpenTrade(TradeType.Buy, "Normal");

            if (lastClose < _l0 && !_sellSetupDone)
                OpenTrade(TradeType.Sell, "Normal");
        }

        private void CheckAggressiveEntry()
        {
            if (Symbol.Ask >= _aggrBuy && !_buySetupDone)
                OpenTrade(TradeType.Buy, "Aggressive");

            if (Symbol.Bid <= _aggrSell && !_sellSetupDone)
                OpenTrade(TradeType.Sell, "Aggressive");
        }

        private bool HasOpenPos(TradeType type)
        {
            foreach (var p in Positions)
                if (p.SymbolName == SymbolName && p.TradeType == type)
                    return true;
            return false;
        }

        // ════════════════════════════════════════════════════════════════
        //  O P E N   T R A D E
        //
        //  SL is set in TWO steps:
        //    1. slPips  → used ONLY for volume/risk calculation
        //    2. After fill → ModifyPosition with ABSOLUTE slPx price
        //
        //  This pins the SL to the exact price level (e.g. Line 1 / Line 0)
        //  regardless of Ask/Bid spread at the moment of fill.
        // ════════════════════════════════════════════════════════════════
        private void OpenTrade(TradeType type, string mode)
        {
            double entry  = type == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            double slPx   = GetSLPrice(type, mode);                          // exact SL price level
            double slPips = Math.Abs(entry - slPx) / Symbol.PipSize;        // for volume calc only

            if (slPips < 1)
            {
                Print($"⚠️  SL distance too tight ({slPips:F1} pips). Trade skipped.");
                return;
            }

            double vol = CalcVolume(slPips);
            if (vol <= 0)
            {
                Print($"⚠️  Volume calculation failed. Trade skipped.");
                return;
            }

            string label  = $"ARCHITECT_{type}_{mode}";

            // Step 1: Open the order WITHOUT SL
            var result = ExecuteMarketOrder(type, SymbolName, vol, label, null, null);

            if (result.IsSuccessful)
            {
                // Step 2: Pin SL to the EXACT absolute price level
                ModifyPosition(result.Position, slPx, null, ProtectionType.Absolute);

                if (type == TradeType.Buy)  _buySetupDone  = true;
                if (type == TradeType.Sell) _sellSetupDone = true;

                Print($"✅  {mode} {type} OPENED | Vol: {vol:F2} | SL pinned at: {slPx:F5} | Risk: {RiskPercent}%");
                Print($"🔒  {type} setup locked — no re-entry until DISARM or line move.");
            }
            else
                Print($"❌  Order failed: {result.Error}");

            UpdatePanel();
        }

        // ════════════════════════════════════════════════════════════════
        //  S T O P   L O S S   P R I C E   C A L C U L A T I O N
        //
        //  AGGRESSIVE → SL always at anchor line (tight, logical)
        //    Aggr BUY  fires at 1.2  → SL at Line 1
        //    Aggr SELL fires at -0.2 → SL at Line 0
        //
        //  NORMAL → SL uses selected SlOption parameter
        // ════════════════════════════════════════════════════════════════
        private double GetSLPrice(TradeType type, string mode)
        {
            bool isAggressive = mode == "Aggressive";

            if (type == TradeType.Buy)
            {
                if (isAggressive) return _l1;

                switch (StopLossOption)
                {
                    case SlOption.Zero:    return _l1;
                    case SlOption.Fifty:   return _l075;
                    case SlOption.Hundred: return _l050;
                    default:               return _l075;
                }
            }
            else
            {
                if (isAggressive) return _l0;

                switch (StopLossOption)
                {
                    case SlOption.Zero:    return _l0;
                    case SlOption.Fifty:   return _l025;
                    case SlOption.Hundred: return _l050;
                    default:               return _l025;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  L O T   S I Z E   C A L C U L A T I O N
        //  Always risks exactly RiskPercent% of account balance
        // ════════════════════════════════════════════════════════════════
        private double CalcVolume(double slPips)
        {
            if (slPips <= 0) return 0;

            double riskAmount = Account.Balance * (RiskPercent / 100.0);
            double rawVolume  = riskAmount / (slPips * Symbol.PipValue);

            return Symbol.NormalizeVolumeInUnits(
                       Math.Max(rawVolume, Symbol.VolumeInUnitsMin),
                       RoundingMode.Down);
        }

        // ════════════════════════════════════════════════════════════════
        //  T R A I L I N G   S L   M A N A G E M E N T
        // ════════════════════════════════════════════════════════════════
        private void ManageTrailingSL()
        {
            foreach (var pos in Positions)
            {
                if (pos.SymbolName != SymbolName)           continue;
                if (!pos.Label.StartsWith("ARCHITECT"))     continue;

                if (pos.TradeType == TradeType.Buy)
                    TrailBuySL(pos);
                else
                    TrailSellSL(pos);
            }
        }

        private void TrailBuySL(Position pos)
        {
            double  bid = Symbol.Bid;
            double? sl  = pos.StopLoss;

            if (bid >= _bTP300 && (sl == null || sl < _bTP250))
            {
                ModifyPosition(pos, _bTP250, pos.TakeProfit, ProtectionType.Absolute);
                Print($"🔒  BUY SL stepped → 2.5 level ({_bTP250:F5})");
            }
            else if (bid >= _bTP250 && (sl == null || sl < _bTP225))
            {
                ModifyPosition(pos, _bTP225, pos.TakeProfit, ProtectionType.Absolute);
                Print($"🔒  BUY SL stepped → 2.25 level ({_bTP225:F5})");
            }
        }

        private void TrailSellSL(Position pos)
        {
            double  ask = Symbol.Ask;
            double? sl  = pos.StopLoss;

            if (ask <= _sTP200 && (sl == null || sl > _sTP150))
            {
                ModifyPosition(pos, _sTP150, pos.TakeProfit, ProtectionType.Absolute);
                Print($"🔒  SELL SL stepped → -1.5 level ({_sTP150:F5})");
            }
            else if (ask <= _sTP150 && (sl == null || sl > _sTP125))
            {
                ModifyPosition(pos, _sTP125, pos.TakeProfit, ProtectionType.Absolute);
                Print($"🔒  SELL SL stepped → -1.25 level ({_sTP125:F5})");
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  P A N E L   U I
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
                Width               = 300
            };

            var title = new TextBlock
            {
                Text            = "🏛️  T H E   A R C H I T E C T",
                FontSize        = 13,
                FontWeight      = FontWeight.Bold,
                ForegroundColor = Color.FromArgb(255, 255, 215, 0),
                Margin          = new Thickness(10, 10, 10, 2),
                TextAlignment   = TextAlignment.Center
            };

            _statusText = new TextBlock
            {
                Text            = "▪ DISARMED — Ready to ARM",
                FontSize        = 11,
                ForegroundColor = Color.Orange,
                Margin          = new Thickness(10, 0, 10, 6),
                TextAlignment   = TextAlignment.Center
            };

            _infoText = new TextBlock
            {
                Text            = "Set Line 1 & Line 0 prices\nin parameters, then restart.",
                FontSize        = 10,
                ForegroundColor = Color.Silver,
                Margin          = new Thickness(14, 2, 10, 8)
            };

            _armBtn = new Button
            {
                Text            = "□   ARM THE TRAP",
                BackgroundColor = Color.FromArgb(255, 220, 120, 0),
                ForegroundColor = Color.White,
                FontSize        = 12,
                FontWeight      = FontWeight.Bold,
                Margin          = new Thickness(10, 0, 10, 10),
                Height          = 40
            };
            _armBtn.Click += OnArmClicked;

            _panel.AddChild(title);
            _panel.AddChild(_statusText);
            _panel.AddChild(_infoText);
            _panel.AddChild(_armBtn);

            Chart.AddControl(_panel);
        }

        private void OnArmClicked(ButtonClickEventArgs args)
        {
            if (!_initialized)
            {
                Print("⚠️  Bot not initialized. Enter valid Line 1 & Line 0 prices and restart.");
                return;
            }

            _isArmed = !_isArmed;

            if (!_isArmed)
            {
                _buySetupDone  = false;
                _sellSetupDone = false;
                Print("🔴  DISARMED — Setup reset. Move lines or re-ARM for a new setup.");
            }
            else
            {
                Print("🟢  ARMED — The Architect is live. Watching for entries.");
            }

            UpdatePanel();
        }

        private void UpdatePanel()
        {
            if (_statusText == null || _armBtn == null) return;

            if (_isArmed)
            {
                _statusText.Text            = "● ARMED — ARCHITECT ACTIVE ●";
                _statusText.ForegroundColor = Color.FromArgb(255, 0, 255, 136);
                _armBtn.Text                = "■   DISARM";
                _armBtn.BackgroundColor     = Color.FromArgb(255, 180, 20, 20);
            }
            else
            {
                _statusText.Text            = "▪ DISARMED — Ready to ARM";
                _statusText.ForegroundColor = Color.Orange;
                _armBtn.Text                = "□   ARM THE TRAP";
                _armBtn.BackgroundColor     = Color.FromArgb(255, 220, 120, 0);
            }

            UpdatePanelInfo();
        }

        private void UpdatePanelInfo()
        {
            if (_infoText == null || !_initialized) return;

            string slLabel = StopLossOption == SlOption.Zero    ? "0%  — Entry Line" :
                             StopLossOption == SlOption.Fifty   ? "50% — Mid Zone"   :
                                                                  "100%— Midline 0.5";

            string aggrLabel = AggressiveMode ? "ON  ⚡" : "OFF";

            // 3 states: ACTIVE (open) → DONE (closed after firing) → Waiting (not yet)
            string buyStatus;
            if (_buySetupDone && HasOpenPos(TradeType.Buy))
                buyStatus = "🟢  BUY  — ACTIVE";
            else if (_buySetupDone && !HasOpenPos(TradeType.Buy))
                buyStatus = "✔️  BUY  — DONE  (DISARM to reset)";
            else
                buyStatus = "⏳  BUY  — Waiting...";

            string sellStatus;
            if (_sellSetupDone && HasOpenPos(TradeType.Sell))
                sellStatus = "🔴  SELL — ACTIVE";
            else if (_sellSetupDone && !HasOpenPos(TradeType.Sell))
                sellStatus = "✔️  SELL — DONE  (DISARM to reset)";
            else
                sellStatus = "⏳  SELL — Waiting...";

            _infoText.Text =
                $"Range  :  {(_gap / Symbol.PipSize):F1} pips\n"  +
                $"L1     :  {_l1:F5}\n"                           +
                $"L0     :  {_l0:F5}\n"                           +
                $"SL     :  {slLabel}\n"                          +
                $"Risk   :  {RiskPercent}%  |  Bal: ${Account.Balance:F2}\n" +
                $"Aggr   :  {aggrLabel}\n"                        +
                $"Buy    :  {buyStatus}\n"                        +
                $"Sell   :  {sellStatus}";
        }

        // ════════════════════════════════════════════════════════════════
        //  O N   S T O P
        // ════════════════════════════════════════════════════════════════
        protected override void OnStop()
        {
            Print("🛑  The Architect stopped.");
        }
    }
}
