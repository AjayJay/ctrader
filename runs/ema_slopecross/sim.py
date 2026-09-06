from pathlib import Path
from typing import List
import json
import math
import sys

BASE = Path("runs/ema_slopecross")
RAW_DIR = BASE / "data" / "raw"
RAW_DIR.mkdir(parents=True, exist_ok=True)

SYMBOLS = ["BTCUSD", "XAUUSD", "USTEC"]
TIMEFRAMES = ["m5", "m15", "m30", "h1", "h4", "d1"]
FIXED_VOLUME = 0.01
SLOT_MAP = {"m5": 5, "m15": 15, "m30": 30, "h1": 60, "h4": 240, "d1": 1440}

def symbol_volume_units(symbol: str) -> float:
    # From symbol details: BTCUSD/USTEC lotSize=1, min 0.01; XAUUSD lotSize=100, min 1
    if symbol == "XAUUSD":
        # 0.01 lots * 100 = 1 unit
        return 1.0
    return 0.01

def symbol_pip_size(symbol: str) -> float:
    if symbol == "USTEC":
        return 0.1
    if symbol == "BTCUSD":
        return 1.0
    if symbol == "XAUUSD":
        return 0.01
    return 1.0

def point_to_money(symbol: str, points: float) -> float:
    # In USDM margin account on cTrader, metal USD PnL is ~ 1 unit * price change.
    # We'll rank by "raw points * volume units", noting XAUUSD points are in quote pips.
    return points * symbol_volume_units(symbol)


def ema_series(closes: List[float], period: int):
    if len(closes) < period:
        return [None]*len(closes)
    out = [None]*(period-1)
    sma = sum(closes[:period])/period
    out.append(sma)
    m = 2/(period+1)
    prev = sma
    for c in closes[period:]:
        prev = (c-prev)*m + prev
        out.append(prev)
    return out

def ema_at(closes: List[float], period: int, idx: int):
    ser = ema_series(closes, period)
    v = ser[idx]
    return None if v is None else float(v)

def simulate(closes: List[float], timestamps: List[str], fast_p: int, mid_p: int, slow_p: int):
    n = len(closes)
    warmup = max(fast_p, mid_p, slow_p) + 2
    if n <= warmup + 1:
        return [], 0.0

    trades = []
    pos_side = None
    pos_open_price = None
    pos_open_idx = None

    for idx in range(warmup, n - 1):
        price0 = closes[idx]
        price1 = closes[idx-1]

        fast0 = ema_at(closes, fast_p, idx)
        fast1 = ema_at(closes, fast_p, idx-1)
        mid0 = ema_at(closes, mid_p, idx)
        mid1 = ema_at(closes, mid_p, idx-1)
        slow0 = ema_at(closes, slow_p, idx)
        slow1 = ema_at(closes, slow_p, idx-1)
        if None in (fast0, fast1, mid0, mid1, slow0, slow1):
            continue

        cross_under_slow = (price1 >= slow1) and (price0 < slow0)
        cross_over_slow = (price1 <= slow1) and (price0 > slow0)
        cross_under_fast = (price1 >= fast1) and (price0 < fast0)
        cross_over_fast = (price1 <= fast1) and (price0 > fast0)

        long_sig = cross_under_slow or (((price0 - price1) < 0) and ((fast0 - fast1) < 0) and cross_under_fast and ((mid0 - mid1) > 0))
        short_sig = cross_over_slow or (((price0 - price1) > 0) and ((fast0 - fast1) > 0) and cross_over_fast and ((mid0 - mid1) < 0))

        signal = None
        if long_sig:
            signal = "long"
        elif short_sig:
            signal = "short"
        if signal is None:
            continue

        # Close current position if any
        if pos_side is not None:
            sign = 1.0 if pos_side == "long" else -1.0
            pnl = sign * (price0 - pos_open_price)
            trades.append({
                "open_idx": pos_open_idx,
                "close_idx": idx,
                "side": pos_side,
                "open_time": timestamps[pos_open_idx],
                "close_time": timestamps[idx],
                "open": pos_open_price,
                "close": price0,
                "pnl_points": round(pnl, 6),
            })
            pos_side = None
            pos_open_price = None
            pos_open_idx = None

        pos_side = signal
        pos_open_price = price0
        pos_open_idx = idx

    if pos_side is not None and pos_open_price is not None:
        final_price = closes[-1]
        sign = 1.0 if pos_side == "long" else -1.0
        pnl = sign * (final_price - pos_open_price)
        trades.append({
            "open_idx": pos_open_idx,
            "close_idx": len(closes)-1,
            "side": pos_side,
            "open_time": timestamps[pos_open_idx],
            "close_time": timestamps[-1],
            "open": pos_open_price,
            "close": final_price,
            "pnl_points": round(pnl, 6),
        })

    net = round(sum(t["pnl_points"] for t in trades), 6)
    return trades, net


def main():
    report_dir = BASE
    report_dir.mkdir(parents=True, exist_ok=True)
    merged_dir = BASE / "data"
    merged_dir.mkdir(parents=True, exist_ok=True)

    # Merge raw chunks
    for symbol in SYMBOLS:
        for tf in TIMEFRAMES:
            pattern = f"{symbol}_{tf}_*.json"
            files = sorted(RAW_DIR.glob(pattern))
            if not files:
                continue
            data = []
            for p in files:
                data.extend(json.loads(p.read_text()))
            (merged_dir / f"{symbol}_{tf}.json").write_text(json.dumps(data))

    results = []
    for symbol in SYMBOLS:
        for tf in TIMEFRAMES:
            path = merged_dir / f"{symbol}_{tf}.json"
            if not path.exists():
                continue
            bars = json.loads(path.read_text())
            closes = [float(b["close"]) for b in bars]
            timestamps = [b["timestamp"] for b in bars]
            for f in [2, 5, 9]:
                for mi in [4, 8, 14, 21]:
                    for s in [20, 50, 100]:
                        if f >= mi or mi >= s:
                            continue
                        trades, net_points = simulate(closes, timestamps, f, mi, s)
                        wins = sum(1 for t in trades if t["pnl_points"] > 0)
                        losses = sum(1 for t in trades if t["pnl_points"] <= 0)
                        win_rate = round(100*wins/len(trades), 2) if trades else 0.0
                        profit_factor = None
                        gross_profit = sum(t["pnl_points"] for t in trades if t["pnl_points"] > 0)
                        gross_loss = abs(sum(t["pnl_points"] for t in trades if t["pnl_points"] <= 0))
                        if gross_loss > 0:
                            profit_factor = round(gross_profit/gross_loss, 4)
                        results.append({
                            "symbol": symbol,
                            "timeframe": tf,
                            "fast": f,
                            "mid": mi,
                            "slow": s,
                            "trades": len(trades),
                            "wins": wins,
                            "losses": losses,
                            "win_rate_pct": win_rate,
                            "profit_factor": profit_factor,
                            "net_pnl_points": net_points,
                            "net_pnl_money_adj": round(point_to_money(symbol, net_points), 4),
                        })

    results.sort(key=lambda r: r["net_pnl_points"], reverse=True)
    (report_dir / "results.json").write_text(json.dumps(results, indent=2))
    print(json.dumps({"count": len(results), "top": results[:5]}))


if __name__ == "__main__":
    main()
