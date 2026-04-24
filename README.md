# EnviroEquipmentFinalEdition

## Phase 1 - Siemens S7 MVP

This repository now contains a minimal phase-1 implementation skeleton for Siemens S7 communication verification.

### Scope implemented
- `IPlcClient` abstraction for PLC access.
- `SiemensS7Client` with per-device request serialization (`SemaphoreSlim`).
- In-memory adapter (`InMemoryS7Adapter`) for development without PLC hardware.
- Polling and write services.
- Sample protocol file: `src/SiemensS7Demo/Config/siemens_s7_sample.xml`.
- Console demo entrypoint in `src/SiemensS7Demo/Program.cs`.

### Hardware validation to replace mock adapter
Use a production adapter (S7NetPlus/Sharp7-based) implementing `IS7Adapter` and run these tests:
1. Connect to S7-200 SMART.
2. Connect to S7-1500.
3. Read/write BOOL / INT16 / DINT / REAL.
4. 2-hour continuous polling.
5. Auto reconnect after disconnection.

### Notes
- Current implementation is a scaffold to accelerate phase-1 coding and validation.
- Replace `InMemoryS7Adapter` with real PLC adapter before field tests.
