# S10 Trace / Export handoff (partial)

## Working branch and status
`feat/s10-bidirectional-trace-export` (Draft PR #56, base I1); currently **in_progress**.

## Shipped-to-branch initial contract
`provenance_trace({request:{projectId:string,action:"panel"|"asset"|"version",id:string,offset?:number,limit?:number}})`.

Response `{edges,totalCount,offset,limit,truncated}`. Each edge provides `paperId,figureId,panelId,usage,analysisId,analysisRole,frozenAt,includedReplicateIds,assetId,assetVersionId,versionNumber,blobSha256,sizeBytes,assetDeleted`; paper fields may be null for statistical edges and analysis fields null for Panel edges. The API does **not** infer a Panel→Analysis causal edge by shared filename, Project/Assay or coincidental version usage. Historical analysis inclusion comes from `analysis_samples`. Query is scoped to a recognized local repository and a specified Project. `asset` returns all pinned versions; `version` targets one pinned version. Soft-deleted links continue to establish historic membership, not active visibility.

## Not yet available
The real Paper workspace now displays a paginated Panel version-provenance view and version-to-Paper/Analysis reverse query. The renderer test mocks IPC and cannot verify native DB. There is no supported Figure Manifest v1 production contract, no validated directory layout, no CAS-byte exporter, no EXE and no completed all-gates CI evidence. A representative **proposed** manifest schema, not real export output:
```json
{"manifestVersion":1,"schemaVersion":3,"projectId":"project-id","paperId":"paper-id","figureId":"figure-id","status":"proposed-not-exported","references":[{"panelId":"panel-id","usage":"display","assetVersionId":"version-id","assetId":"asset-id","sha256":"64-hex-digest","sizeBytes":0,"relativePath":"files/version-id.bin"}]}
```
S11 cannot rely on this proposal until implemented, hashed, tested, and proven by Windows CI.

## Added partial API (code not CI-accepted)
S9 `paper_workspace` action `candidates` accepts optional `candidateQuery`, `candidateStage`, `candidateOffset`, `candidateLimit`; response includes `candidateTotalCount` and `candidateTruncated`. Figure native exporter is deliberately **not** published as a Tauri IPC until a trusted directory authorization and Windows handle-level safety implementation exists. No user-facing production manifest or successful S10 EXE can be claimed.
