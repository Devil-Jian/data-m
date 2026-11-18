# S12 machine acceptance matrix

Status recorded from actual implementation/evidence, not planned tests. `renderer_test_added` does not mean passing.

| ID | State | Evidence / outstanding |
| --- | --- | --- |
| S12-UI-01 | renderer_test_added | Production root no longer mounts Demo; @S12 smoke added; no successful S12 Windows gate yet |
| S12-UI-02 | implemented_unverified | Project breadcrumb, Alt-arrow history, deleted-ID filtering and explicit renderer test; CI confirmation pending |
| S12-UI-03 | pending | Group/Replicate n and copy invariants in unified workflow |
| S12-UI-04 | partial_unverified | Existing distinct sample/Assay scope preserved; browser key now isolates query lifecycle on context change |
| S12-UI-05 | implemented_unverified | Grid/list/table selector added on existing real `list_assets` paging; no CI pass evidence |
| S12-UI-06 | partial_unverified | Existing real lifecycle/clipboard tests retained; demo-only right-click/F2 tests preserved in isolated `tests/e2e`; complete real Explorer parity pending |
| S12-UI-07 | pending | Native file drag vs AssetLink drag safety |
| S12-UI-08 | partial_unverified | Existing S3 import logic retained; S12 sidebar navigation now blocked during import lock; end-to-end real repository verification pending |
| S12-UI-09 | pending | Lifecycle conflict and reference protection |
| S12-UI-10 | pending | Independent inclusion n; freeze/revise |
| S12-UI-11 | partial_unverified | Existing real Paper/Provenance APIs retained and shared project sidebar added; end-to-end pinned-reference regression pending |
| S12-UI-12 | pending | Figure SHA manifest/confirm/cancel |
| S12-UI-13 | pending | Read-only audit, snapshot, isolated restore |
| S12-UI-14 | pending | GUI at 10,024 real assets; SQLite-only S11 benchmark not GUI evidence |
| S12-UI-15 | pending | Persistent state after Windows EXE restart |
| S12-UI-16 | blocked | Windows 44308671524 failed old S0 source contract; 44311352353 passed source test but failed old S0 Demo-first renderer expectation; those tests migrated. No passing latest S12 SHA or NSIS/portable evidence yet |

No native Windows 11 manual tests have been executed as part of S12. Current status: `in_progress / manual_pending`.
