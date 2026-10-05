# RVA Architecture Diagrams

Diagrams of the Resilient VPP Aggregator's documented design. They illustrate the rules; they do not replace them. When a diagram and a rule disagree, the rule wins and the diagram should be fixed in the same pull request.

**Sources of truth**

- `.ai/rules/csharp-monolith.md`: solution layout, module ownership, allowed references, shared kernel
- `.ai/plans/PHASE_1_PLAN.md`: modules, data ownership, and build order
- `.ai/rules/domain-glossary.md`: domain terms used in the diagrams

**Diagrams**

| Diagram | Answers |
|---|---|
| [Project structure](project-structure.md) | Where does each project, rule file, and infrastructure file live? |
| [Module dependencies](module-dependencies.md) | Which projects may reference which? |
| [Runtime view](runtime.md) | Which processes run, how they communicate, and which module owns which data? |
| [Module contracts](module-contracts.md) | How does one module use another without seeing its internals? |

These diagrams show the **target design**. For what has been built so far, see the plan's Definition of Done and the repository itself.
