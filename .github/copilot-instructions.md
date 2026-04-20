# Copilot Instructions

## Project Guidelines
- When reviewing instruction files in this repo, always check both top-level files and files under `.github/instructions/` before planning or making changes. Verify both top-level instruction files and files under `.github/instructions/` before concluding they are missing.

## Feature Specifications
- For feature spec documents in this repo, implementation plans must be decisive (no conditional 'if' statements) and must not contain test details; test details belong in the feature tests.md file.
- Do not add a 'Test implementation tasks' section in feature tests.md when those directives are already covered by repository-wide tests.instructions.md.

## Test Specifications
- Use scope as the primary classification axis for test specifications; concerns like Contract/Serialization should be treated as secondary tags, not top-level sections.
