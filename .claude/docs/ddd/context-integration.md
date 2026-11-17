# DDD: Context Integration

Read before working on context relationships, Anticorruption Layers, cross-context imports, or module layout. Extends `.claude/rules/implementing-domain-driven-design.mini.md`.

## Rules

- Make every context interaction explicit: show the relationship, translation responsibility, and upstream/downstream influence before sharing data, terms, models, or integration code.
- Translate foreign, legacy, partner, external, and infrastructure models into the local language; keep foreign schemas, statuses, contract models, and aggregates out of local domain objects.
- Organize modules by Bounded Context first and by domain or use-case ownership within the context; avoid giant `shared` or `common` packages for domain concepts.

## Triggers

- When code wants to import another context's domain package, share enums across contexts, or couple through another context's database, add explicit translation instead.
- When legacy, vendor, partner, API, transport, persistence, or UI shape appears in local domain code, add an Anticorruption Layer or mapping boundary before modeling locally.
