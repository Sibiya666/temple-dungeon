# Task20 frozen engine extraction compatibility

Apply only engine-legacy-fix.patch. Single test-source file; patch unapplied. Supersedes the initial current-cohort-only proposal. No baseline or fixture regeneration.

## Evidence
full-after-reference-refinements.log1505–1513: failure after75s at CombatEngineTests.cs52, run-key equality, expected starts attributes/sorcerer..., actual starts acolyte/apostate.... AssertMatchesBaseline(_ => true) was collecting current47 references against a frozen32 reference golden. Task21 reoptimization also makes current32 JSON the wrong historical inputs even if filtered correctly.

Verified BEFORE adopting fixture inputs: imported every existing legacy_codes.txt code using native BuildCodec, required Warrior/Sorcerer class and32 unique paths, and simulated each using native existing Simulator and exact original CombatEngineTests.Fingerprint via reflection. Compared every golden field as case-sensitive exact strings. Result:32 paths,576 runs,8640 field comparisons, zero mismatches. historical-verification.json records result and unchanged baseline SHA256. This proves these codec fixtures correspond to the original engine golden, beyond the earlier training-DPS-only evidence.

## Change
LegacyBuilds reads existing copied Fixtures/legacy_codes.txt, fails missing/malformed rows, failed imports, non-W/S class, duplicates or count other than32. AssertMatchesBaseline checks exact full corpus paths against frozen golden BEFORE applying any caller filter. It then evaluates those historical builds with the original caller filter, preserving nonempty selection, exact run-key equality and every original field comparison. It does not merely filter by golden paths or filename patterns. Deleted/added corpus paths cannot silently disappear behind the filter.

Other callers (DamageEventTests.Simulator_numbers_unchanged and Task16EventTests filtered two-build check) retain their original filters/signatures; only their input source becomes the verified historical codes. No caller source edits. New Apostate current-reference live/simulator tests remain separate.

Fingerprint receives optional historical inputs. No dummy, duration, defense, Average/Roll seeds1/7, number representation, log count, log first200 SHA, resource-series hash or compared field changes. Fingerprint and downstream formatter/hash source checked byte-identical. Existing optional current-file route remains available but compatibility callers use fixture builds.

Explicit DIALOID_WRITE_BASELINE=1 writer remains opt-in and functional, now computes from the SAME historical32 inputs. It still writes current-engine results deliberately when requested; no writer invocation was made. It does not ingest Task21 optimized JSON or new15 Apostate builds. Existing Writing guard unchanged. Test renamed to state frozen32 legacy scope.

## Validation / limitations
- git apply --check native PASS.
- Full historical golden comparison performed without a build, via existing Debug assemblies. Native code/data/baseline unchanged.
- verify-historical-inputs.ps1 reproducible; it sets APP_CONTEXT_BASE_DIRECTORY only in its own PowerShell process to let native FixturePaths resolve native data. It loads assemblies, never compiles. Initial verify-existing-helper.ps1 attempted before setting that application context and failed data/ lookup; not used as green evidence. Historical verifier succeeds and is authoritative.
- Patched C# not compiled/run per no-build scope. Controller should review/apply, compile, run renamed test and the two helper callers. Natural green expected from exact576-run evidence, not claimed as a compiled-test result.
- Source hashes captured for concurrency review. All writes under main/scratch20/engine-legacy-fix. No native writes/build/optimizer or production changes.
