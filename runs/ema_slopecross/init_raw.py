#!/usr/bin/env python3
from pathlib import Path
import json

DIR = Path("runs/ema_slopecross/data/raw")
DIR.mkdir(parents=True, exist_ok=True)

for symbol in ["BTCUSD", "XAUUSD", "USTEC"]:
    for tf in ["m5", "m15", "m30", "h1", "h4", "d1"]:
        (DIR / f"{symbol}_{tf}.json").write_text("[]")
