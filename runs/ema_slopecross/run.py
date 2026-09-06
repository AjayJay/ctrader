from __future__ import annotations
import json
from pathlib import Path
from typing import List

import math

SYMBOLS = ["BTCUSD", "XAUUSD", "USTEC"]
TIMEFRAMES = ["m5", "m15", "m30", "h1", "h4", "d1"]
FAST = [2, 5, 9]
MID = [4, 8, 14, 21]
SLOW = [20, 50, 100]
SLOT_MAP = {"m5": 5, "m15": 15, "m30": 30, "h1": 60, "h4": 240, "d1": 1440}
VOLUME = 0.01

DATA_DIR = Path("runs/ema_slopecross/data")
REPORT_DIR = Path("runs/ema_slopecross")


def price_at_close(pos: float | None, exit_price: float | None, side: str | None, open_price: float) -> float:
    if pos is None or side is None or exit_price is None:
        return 0.0
    sign = 1.0 if side == "buy" else -1.0
    return sign * (exit_price - open_price)


def ema_series(closes: List[float], period: int):
    if len(closes) < period:
        return [None] * len(closes)
    out = [None] * (period - 1)
    sma = sum(closes[:period]) / period
    out.append(sma)
    m = 2 / (period + 1)
    prev = sma
    for c in closes[period:]:
        prev = (c - prev) * m + prev
        out.append(prev)
    return out


def ema_at(closes: List[float], period: int, idx: int):
    ser = ema_series(closes, period)
    return None if ser[idx] is None else float(ser[idx])


def simulate_pair(closes: List[float], timestamps: List[str], fast_p: int, mid_p: int, slow_p: int):
    n = len(closes)
    warmup = max(fast_p, mid_p, slow_p) + 2
    if n <= warmup + 1:
        return {"trades": [], "net_pnl": 0.0, "count": 0}

    pos_side = None
    pos_open_price = None
    pos_open_idx = None
    trades = []

    for idx in range(warmup, n - 1):
        price0 = closes[idx]
        price1 = closes[idx - 1]
        if idx < 1:
            continue

        fast0 = ema_at(closes, fast_p, idx)
        fast1 = ema_at(closes, fast_p, idx - 1)
        mid0 = ema_at(closes, mid_p, idx)
        mid1 = ema_at(closes, mid_p, idx - 1)
        slow0 = ema_at(closes, slow_p, idx)
        slow1 = ema_at(closes, slow_p, idx - 1)

        if None in (fast0, fast1, mid0, mid1, slow0, slow1):
            continue

        cross_under_slow = price1 >= slow1 and price0 < slow0
        cross_over_slow = price1 <= slow1 and price0 > slow0
        cross_under_fast = price1 >= fast1 and price0 < fast0
        cross_over_fast = price1 <= fast1 and price0 > fast0

        long_signal = cross_under_slow or ((price0 - price1) < 0 and (fast0 - fast1) < 0 and cross_under_fast and (mid0 - mid1) > 0)
        short_signal = cross_over_slow or ((price0 - price1) > 0 and (fast0 - fast1) > 0 and cross_over_fast and (mid0 - mid1) < 0)

        signal = "long" if long_signal else ("short" if short_signal else None)
        if signal is None:
            continue

        if pos_side is not None:
            pnl = price_at_close(pos_open_price, price0, pos_side, pos_open_price)
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

        if signal == "long":
            pos_side = "long"
            pos_open_price = price0
            pos_open_idx = idx
        else:
            pos_side = "short"
            pos_open_price = price0
            pos_open_idx = idx

    if pos_side is not None and pos_open_price is not None:
        final_price = closes[-1]
        pnl = price_at_close(pos_open_price, final_price, pos_side, pos_open_price)
        trades.append({
            "open_idx": pos_open_idx,
            "close_idx": n - 1,
            "side": pos_side,
            "open_time": timestamps[pos_open_idx],
            "close_time": timestamps[-1],
            "open": pos_open_price,
            "close": final_price,
            "pnl_points": round(pnl, 6),
        })

    net = round(sum(t["pnl_points"] for t in trades), 6)
    return {"trades": trades, "net_pnl": net, "count": len(trades)}


def main():
    REPORT_DIR.mkdir(parents=True, exist_ok=True)
    (REPORT_DIR / "results.json").write_text(json.dumps([], indent=2))
    results = []

    for symbol in SYMBOLS:
        for tf in TIMEFRAMES:
            # Merge raw chunk files if present
            merged_path = DATA_DIR / f"{symbol}_{tf}.json"
            merged_data = []
            if merged_path.exists():
                merged_data = json.loads(merged_path.read_text())
            # If missing or partial, allow post-fetch merging outside
            raw_files = sorted((DATA_DIR / "raw").glob(f"{symbol}_{tf}_*.json")) if (DATA_DIR / "raw").exists() else []
            if not merged_data and raw_files:
                for p in raw_files:
                    merged_data.extend(json.loads(p.read_text()))
                merged_path.write_text(json.dumps(merged_data))
            if not merged_data:
                continue

            closes = [float(b["close"]) for b in merged_data]
            timestamps = [b["timestamp"] for b in merged_data]
            for f in FAST:
                for mi in MID:
                    for s in SLOW:
                        if f >= mi or mi >= s:
                            continue
                        sim = simulate_pair(closes, timestamps, f, mi, s)
                        results.append({
                            "symbol": symbol,
                            "timeframe": tf,
                            "fast": f,
                            "mid": mi,
                            "slow": s,
                            "net_pnl_points": sim["net_pnl"],
                            "trades": sim["count"],
                        })

    results.sort(key=lambda r: r["net_pnl_points"], reverse=True)
    (REPORT_DIR / "results.json").write_text(json.dumps(results, indent=2))
    print(json.dumps({"count": len(results)}))

if __name__ == "__main__":
    main()
