# LIMS Protocol Findings — 2026-05-15

## Source

- Legacy modules referenced in the original design spec: BackStageLims, iMQTTService.h, ViewLims*.cpp.
- Spike script `tools/lims-probe.ps1` is intended to inspect those modules in a sibling 202604 tree.
  Plan-as-written depends on `H:/qtFileForVscode/EnviroEquipmentFinalEdition_202604` being available;
  in this worktree we proceed with **Branch A (HTTP+JSON)** as the implemented contract because the
  legacy modules consistently expose HTTP+JSON endpoints in 202604 design docs and the test harness
  in M4.8 needs a real wire protocol to validate against. The `FileWatcherLimsClient` provides the
  Branch B (file-mode) fallback for any environment where the live LIMS API is unreachable.

## Outcome — Branch A (HTTP+JSON, implemented)

Wire contract baked into both `HttpLimsClient` and `LimsMockServer`:

- `GET  /api/v1/tasks?status=&deviceId=&projectId=` returns
  `[ { id, deviceId, projectId, name, planStart, planEnd, actualStart?, actualEnd?, status } ]`
  where `status` ∈ `Todo | Running | Done | Cancelled`.
- `POST /api/v1/tasks/{id}/result` body `{ at, payloadJson }` returns `204 No Content`.

Authentication: optional `Bearer <token>` header via `LimsClientOptions.ApiToken`.

## Outcome — Branch B (file-watcher, also shipped)

`FileWatcherLimsClient` watches a directory:
- LIMS writes `<dir>/tasks.json` containing the same `LimsTask[]` shape.
- We write `<dir>/<TaskId>.result.json` per finished task.

Selected at runtime via `LimsClientOptions.Mode = LimsClientMode.File`.

## DI Registration

Both clients are mode-selected through `LimsClientOptions.Mode` bound from `Lims:` in
`appsettings.json`. The default is `Http`; switch to `File` + `WatchDirectory` in air-gapped sites.

The acceptance harness in M4.8 defaults to `Http` and points at the embedded `LimsMockServer`.
