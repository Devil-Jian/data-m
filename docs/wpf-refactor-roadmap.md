# PaperArchive WPF migration · PRD V1.2

## Model

ExperimentProject -> ExperimentResult -> DataGroup -> ExperimentAsset
-> PaperUsage (show_data to paper Figure/Panel).

Folder path defines raw/process/show stage. Filename prefix groups assets
inside **one** experiment result. Different experiments with the same prefix
must not merge. Names do not automatically prove process ordering.

## Migration constraints

- Old WinUI3 app and `archive.db` stay unchanged.
- New WPF app reads legacy archived papers through `ArchiveRepository`.
- New pre-archive experimental workspace uses `workspace.db` and shared
  immutable content blobs.
- Never overwrite an original research file.
- Debug warehouse must be separate from the production warehouse.

## Implementation stages

1. WPF desktop shell: Explorer-style tree/list/thumbnail/detail pane,
   status bar, two main views.
2. Experimental project import and prefix data groups; original byte snapshots.
3. Image browsing, clipboard, hash-verified export.
4. Figure/Panel export records and usage confirmation workflow.
5. Read-only archive library linked from Figure to source experiment.
6. Manuscript/PDF rendering and paper finalization into versioned archive.
7. Backup, portable migration/export, advanced indexing, large image codecs.

Stage 1-3 foundations and an initial stage-4 export are included in this
branch. Unfinished features are not treated as complete.
