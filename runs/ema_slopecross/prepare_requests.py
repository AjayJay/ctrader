#!/usr/bin/env python3
import json
import time
from pathlib import Path

BASE = Path("runs/ema_slopecross/data/raw")
BASE.mkdir(parents=True, exist_ok=True)

SYMBOLS = ["BTCUSD", "XAUUSD", "USTEC"]
TIMEFRAMES = ["m5", "m15", "m30", "h1", "h4", "d1"]
START = "2026-06-05T00:00:00Z"
END = "2026-09-05T00:00:00Z"

requests = []
for symbol in SYMBOLS:
    for tf in TIMEFRAMES:
        requests.append({"symbol": symbol, "tf": tf})

print(json.dumps({"requests": requests, "start": START, "end": END}))
