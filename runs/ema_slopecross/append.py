#!/usr/bin/env python3
import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
new_data = json.loads(sys.argv[2])
if not isinstance(new_data, list):
    new_data = [new_data]
if path.exists():
    data = json.loads(path.read_text())
else:
    data = []
data.extend(new_data)
path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(json.dumps(data))
print(json.dumps({"path": str(path), "count": len(data)}))
