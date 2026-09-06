#!/usr/bin/env bash
set -euo pipefail
python3 - <<'PY'
import json, subprocess, pathlib, time
from datetime import datetime, timezone

BASE = pathlib.Path("runs/ema_slopecross/data/raw")
SYMBOLS = ["BTCUSD", "XAUUSD", "USTEC"]
TIMEFRAMES = ["m5","m15","m30","h1","h4","d1"]

def to_iso(dt):
    return dt.strftime('%Y-%m-%dT%H:%M:%SZ')

def fetch(symbol, tf, from_dt, to_dt, limit=1000):
    cmd = f'ctrader_get_trendbars symbolName={symbol} timeframe={tf} from={to_iso(from_dt)} to={to_iso(to_dt)} limit={limit}'
    # Use MCP tool indirectly via a tiny Python bridge that calls opencode? Not available.
    raise RuntimeError("Use outside bash")

print("This script is a placeholder for manual batching; run the direct ctrader MCP loops instead.")
PY
