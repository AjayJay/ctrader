#!/usr/bin/env python3
import json
from pathlib import Path

DIR = Path("runs/ema_slopecross/data/raw")
DIR.mkdir(parents=True, exist_ok=True)

for symbol in ["BTCUSD", "XAUUSD", "USTEC"]:
    for tf in ["m5", "m15", "m30", "h1", "h4", "d1"]:
        out = DIR / f"{symbol}_{tf}.json"
        print(json.dumps({
            "tool": "ctrader_get_trendbars",
            "args": {
                "symbolName": symbol,
                "timeframe": tf,
                "from": "2026-06-05T00:00:00Z",
                "to": "2026-09-05T00:00:00Z",
                "limit": 1000
            },
            "out": str(out)
        }))
