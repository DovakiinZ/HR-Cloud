---
title: Dependency Graph Execution
aliases: [Dependency Graph, Execution Order, Topological Ordering]
tags: [architecture, engine, finance]
---

# Dependency Graph Execution

> Orders interdependent calculations correctly — execution order comes from dependencies, not authoring sequence.
> Up: [[Financial Calculation Engine]]

`HR.Domain/Engines/Finance/Graph/DependencyGraph.cs` builds a graph of which [[Rule Engine|rules]] read which variables, then **topologically orders** evaluation so a rule never runs before its inputs exist.

This lets policy authors write rules in any order; the engine figures out the correct sequence. Cyclic definitions are rejected. Tested by `DependencyGraphTests` ([[Test Suite]]).

## Related
[[Rule Engine]] · [[Formula Engine]] · [[Financial Calculation Engine]]
