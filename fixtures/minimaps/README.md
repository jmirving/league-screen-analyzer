# Minimap fixtures

`diagnostics/` contains small deterministic feature/result fixtures for a valid-like
replay minimap, black/missing crop, menu/overlay crop, and uncertain low-information
crop. They exercise calibration commands and are not real League accuracy evidence.

`synthetic-gap-sequence/session.json` covers both valid, clock-valid/map-invalid,
map-valid/clock-invalid, both invalid, and later valid anchoring. The standard fixture
processor can consume it.

`league-replay-minimap-v1/profile.json` is the versioned initial calibration profile.
Its provenance and `calibratedForCanonicalRecording: false` prevent synthetic evidence
from being presented as real replay validation.
