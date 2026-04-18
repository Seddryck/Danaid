# api-restafarian.instructions.md

## Purpose

Define the Restafarian API style as a specialization over HTTP.

---

## Philosophy

- APIs reflect business concepts
- URLs express meaning, not storage
- consistency across endpoints

---

## Resource Modeling

- resources = business entities
- paths express ownership
- nested resources when meaningful

---

## Commands & Queries

- commands represented as sub-resources or actions
- queries reflect business views

---

## URL Conventions

- prefer path segments over query for business meaning
- avoid technical naming

---

## Aggregate Boundaries

- URLs reflect ownership
- avoid crossing aggregates arbitrarily

---

## Representation

- stable identifiers
- consistent naming
- explicit structures

---

## Error Semantics

- consistent use of conflict vs unprocessable
- align with domain meaning

---

## Anti-Patterns

- RPC disguised as REST
- query params for business views
- technical paths instead of business paths
