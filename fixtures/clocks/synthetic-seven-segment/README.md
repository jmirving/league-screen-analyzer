# Synthetic seven-segment clock fixtures

These tiny ASCII PGM images were deterministically authored from the canonical 5×7 masks used by `league-replay-v1`. They cover template/segmentation mechanics, a missing crop, and insufficient contrast.

They are synthetic, contain no captured League pixels, and must not be used to claim replay recognition accuracy. Add intentionally selected, small, labeled real crops under a separate profile directory with their capture provenance, scaling context, and explicit human labels.

Run:

```text
dotnet run --project src/LeagueScreenAnalyzer.Cli -- evaluate-clock \
  --profile league-replay-v1 \
  --manifest fixtures/clocks/synthetic-seven-segment/manifest.json \
  --output artifacts/clock-evaluation
```
