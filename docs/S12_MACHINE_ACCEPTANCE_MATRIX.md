# S12 machine acceptance matrix

Status recorded from actual implementation/evidence, not planned tests. `renderer_test_added` does not mean passing.

| ID | State | Evidence / outstanding |
| --- | --- | --- |
| S12-UI-01 | baseline_machine_passed / follow_on_pending | Sole real desktop entry tested on Windows #44315421546; newer Explorer/task head requires final rerun |
| S12-UI-02 | baseline_machine_passed / follow_on_pending | Real-ID trail/history/reload passed prior CI; new cross-module breadcrumbs and Paper ID sync under new E2E |
| S12-UI-03 | prior_tests_passed | S2/S5 real contract regressions passed prior Windows run; genuine Windows user-path testing pending |
| S12-UI-04 | prior_tests_passed | S3/S4/S5 scoped mock + native Rust prior run; cross-module full loop pending manual |
| S12-UI-05 | prior_tests_passed / new_tests_pending | 50-row bounded three-mode browser passed prior Windows run; new marquee + keyboard menu pending new-head CI |
| S12-UI-06 | implementation_new_tests_pending | Current-page marquee, Ctrl/Shift, F2/Delete real confirmation, context keyboard/escape, clipboard/drag; real-action E2E added, Windows GUI manual pending |
| S12-UI-07 | prior_tests_passed / manual_pending | Existing native S3/S5 source/target protection regressions; true Windows native drag gesture still manual |
| S12-UI-08 | prior_tests_passed / follow_on_pending | S3 prepare/start/cancel/retry prior green; new view-only real task tray and navigation lock tests pending latest CI |
| S12-UI-09 | prior_tests_passed | Existing native S6 lifecycle/ref protection regressions passed on baseline; latest head rerun and manual UI pending |
| S12-UI-10 | prior_tests_passed | Native S8 freeze/revise and renderer prior CI; new analysis busy-route guard under latest CI |
| S12-UI-11 | prior_tests_passed / follow_on_pending | Native S9/S10 precise version tests passed prior CI; Paper real-project synchronization E2E added |
| S12-UI-12 | prior_tests_passed / follow_on_pending | Native S10 export/manifest prior regression; new task tray callback only confirms complete after native verified manifest |
| S12-UI-13 | prior_tests_passed / follow_on_pending | Native S7 isolation prior regression; new shared task summary and navigation lock checks under latest CI |
| S12-UI-14 | backend_machine_passed / gui_manual_pending | True SQLite/CAS 10,024 assets benchmark passed CI #44315421546; real Windows GUI performance still unmeasured |
| S12-UI-15 | renderer_passed / gui_manual_pending | Real-ID preference reload tested in browser; true EXE restart with SQLite/Analysis/Panel pending Windows user |
| S12-UI-16 | baseline_machine_passed / latest_head_pending | Windows #44315421546 and UI #44315421442 completed success at f72e67... incl verified NSIS/portable; added interaction head must repeat same-SHA Windows/UI |

No native Windows 11 manual tests have been executed as part of S12. Baseline (f72e67...) machine-passed; follow-on implementation head is `in_progress / manual_pending` until exact-SHA CI.
