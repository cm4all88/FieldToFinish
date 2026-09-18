# Checkpoint: drafting straight lines, pre settings-finish (2026-08-20)

State: 613 tests passing. The FTFDRAWLINE drafting world exists end to end:
Boundary/ROW/Section etc. catalog in ftf-settings.json (layers deliberately
unconfigured), one shared construction engine (Pick/Bearing/Azimuth/COGO,
continuous courses, Undo), bearing+distance annotation computed from geometry,
Draft* ownership kinds, FTFCLEAN exclusion + FTFDRAFTCLEAN, Drafting Lines
settings page. Point-notes line identification from the parallel session is in.
Taken BEFORE extending annotation types (BearingOnly/DistanceOnly/FeatureText),
precision dropdowns, and the live straight-line verification pass. Nothing has
run in Civil 3D yet.

Restore: extract source.tar.gz over the repo root. build-net48/ holds the
deployable assemblies as built at this point.
