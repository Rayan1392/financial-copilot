---
name: implementing-domain-driven-design
description: # OBEY Implementing Domain-Driven Design by Vaughn Vernon

## When to use

Use when DDD implementation choices affect bounded contexts, language, aggregates, repositories, events, application services, package structure, or cross-context integration.

## Primary bias to correct

Practical DDD is not renamed CRUD. Model the operational domain inside an explicit Bounded Context, with local language, small invariant boundaries, identity references across Aggregates, and explicit translation across context and infrastructure boundaries.

## Decision rules

- Name the Bounded Context before interpreting terms, modules, services, repositories, events, APIs, persistence, or integrations; never force one global company model.
- Use the local Ubiquitous Language consistently: one concept gets one term inside the context, one term must not carry multiple meanings, and code, tests, events, commands, repositories, services, and packages must speak that language.
- Protect the Core Domain from generic abstractions and ven
modeSlugs:
  - code
  - architect
  - debug
---

# Implementing Domain Driven Design

## Instructions

Add your skill instructions here.
