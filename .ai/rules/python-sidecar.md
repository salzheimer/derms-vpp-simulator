# Python AI Sidecar Rules (`src/Rva.Intelligence`)

## The Consultant constraint

This is the single most important rule for this module: **the sidecar has no dispatch authority.** It runs LSTM-based load forecasting (PyTorch) behind FastAPI and returns recommendations only. The C# core decides; the sidecar advises. Never implement a code path where sidecar output directly triggers a grid-impacting command without passing through the C# core's decision/audit path.

## Resilience

- Calls from the C# core to this sidecar are wrapped in a Polly Circuit Breaker. Assume the sidecar can be slow, down, or "tripped," and that the core has a Safe-Fail default for that case — the sidecar should not need to implement its own fallback logic for core-side failures.
- Real-time state comes from Redis; time-series data from TimescaleDB; device telemetry via MQTT (paho-mqtt).

## Stack

FastAPI, PyTorch, pandas, scikit-learn, tslearn. Python 3.12.
